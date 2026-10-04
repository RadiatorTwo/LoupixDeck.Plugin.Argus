using LoupixDeck.Plugin.Argus.Rendering.Tiles;
using LoupixDeck.Plugin.Argus.Telemetry;

namespace LoupixDeck.Plugin.Argus.Rendering;

/// <summary>
/// Turns a persisted <c>Argus.Sensor</c> command parameter into the <see cref="SensorRow"/>s a
/// tile draws. Owns the parameter grammar and the labels; values, units, history and alert state
/// come from the <see cref="TelemetrySampler"/>, which tracks every sensor under the same
/// <c>Type:Index</c> key the parameter uses.
///
/// <para>The current model is <b>one sensor per command</b>: a parameter is a
/// <c>&lt;Type&gt;:&lt;Index&gt;[:&lt;Label&gt;]</c> reference and yields a single row; the user
/// composes a multi-sensor button by chaining several commands (the tile lays them out as rows).
/// The optional label (see <see cref="Reference"/>) keeps a button on its sensor when the index
/// shifts; a reference without it resolves by index alone, as it always did.</para>
///
/// <para>For backward compatibility the former packed grammar is still parsed, so buttons saved
/// before the rework keep rendering (as one row per contained sensor):</para>
/// <list type="bullet">
/// <item><c>single:&lt;Type&gt;:&lt;Index&gt;</c> → one row</item>
/// <item><c>double:&lt;Type&gt;:&lt;Index&gt;|&lt;Type&gt;:&lt;Index&gt;</c> → two rows</item>
/// <item><c>memory:&lt;AbsType&gt;:&lt;Index&gt;[|&lt;PctType&gt;:&lt;Index&gt;]</c> → abs (+ percent) row</item>
/// <item><c>multi:&lt;Type&gt;:&lt;Index&gt;,…</c> or <c>multi:&lt;Type&gt;:*</c> → one row per match</item>
/// </list>
/// A parameter with no recognised prefix (e.g. <c>CpuLoad:0</c>) is read as a single reference.
/// </summary>
public static class ArgusReadingBuilder
{
    private static readonly char[] TagSeparators = [':'];

    internal static IReadOnlyList<SensorRow> Build(string? parameter, IReadOnlyList<ArgusSensor> sensors)
    {
        if (string.IsNullOrWhiteSpace(parameter))
            return [Placeholder("Argus")];

        (string tag, string rest) = SplitTag(parameter.Trim());

        return tag switch
        {
            "double" => BuildDouble(rest, sensors),
            "memory" => BuildMemory(rest, sensors),
            "multi" => BuildMulti(rest, sensors),
            _ => [BuildSingle(rest, sensors)]
        };
    }

    private static (string tag, string rest) SplitTag(string raw)
    {
        int colon = raw.IndexOf(':');
        if (colon > 0)
        {
            string head = raw[..colon].ToLowerInvariant();
            if (head is "single" or "double" or "memory" or "multi")
                return (head, raw[(colon + 1)..]);
        }

        // Legacy value without a variant tag → single.
        return ("single", raw);
    }

    private static SensorRow BuildSingle(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        if (!TryFindSensor(rest, sensors, out ArgusSensor? sensor, out string key))
            return Placeholder(HeaderFromRef(rest));

        // The menu's name for the sensor; types the menu does not offer keep the generic header.
        if (TileLabels.For(sensors, key) is { } labels)
            return new SensorRow(labels.Header, labels.Short, key);

        string header = SingleHeader(sensor!, sensors);
        return new SensorRow(header, ShortHeaderFrom(header), key);
    }

    private static IReadOnlyList<SensorRow> BuildDouble(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        string[] refs = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<SensorRow> rows = [];
        foreach (string reference in refs)
        {
            if (!TryFindSensor(reference, sensors, out ArgusSensor? sensor, out string key))
                continue;

            // Distinguish the two same-type readings by their role (cur/avg/max/min).
            string caption = Caption(sensor!.Type);
            rows.Add(new SensorRow($"{Header(sensor)} {caption}", caption, key));
        }

        if (rows.Count == 0)
            return [Placeholder(refs.Length > 0 ? HeaderFromRef(refs[0]) : "Argus")];

        return rows;
    }

    private static IReadOnlyList<SensorRow> BuildMemory(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        string[] refs = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (refs.Length == 0 || !TryFindSensor(refs[0], sensors, out ArgusSensor? abs, out string absKey))
            return [Placeholder(refs.Length > 0 ? HeaderFromRef(refs[0]) : "Memory")];

        string header = Header(abs!);
        List<SensorRow> rows = [new SensorRow(header, header, absKey)];
        if (refs.Length > 1 && TryFindSensor(refs[1], sensors, out _, out string pctKey))
            rows.Add(new SensorRow("Used", "Used", pctKey));

        return rows;
    }

    private static IReadOnlyList<SensorRow> BuildMulti(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        // "<Type>:<idx>,<idx>,..." or "<Type>:*" — indices are ordinal positions within the type.
        int colon = rest.IndexOf(':');
        string typeToken = colon > 0 ? rest[..colon] : rest;
        string indexToken = colon > 0 ? rest[(colon + 1)..] : "*";

        if (!Enum.TryParse(typeToken, ignoreCase: true, out ArgusSensorType type) || type == ArgusSensorType.Invalid)
            return [Placeholder("Argus")];

        IReadOnlyList<ArgusSensor> group = SensorsOfType(sensors, type);
        if (group.Count == 0)
            return [Placeholder(Header(type))];

        HashSet<int>? wanted = null;
        if (indexToken.Trim() != "*")
        {
            wanted = indexToken
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.TryParse(t, out int i) ? i : -1)
                .Where(i => i >= 0)
                .ToHashSet();
        }

        bool isTemp = IsTemperature(type);
        List<SensorRow> rows = [];
        for (int ordinal = 0; ordinal < group.Count; ordinal++)
        {
            if (wanted is not null && !wanted.Contains(ordinal))
                continue;

            string label = isTemp ? $"T{ordinal + 1}" : $"#{ordinal}";
            rows.Add(new SensorRow($"{Header(type)} {label}", label, MetricKeys.ForSensor(type, ordinal)));
        }

        if (rows.Count == 0)
            return [Placeholder(Header(type))];

        return rows;
    }

    // ── Sensor lookup ───────────────────────────────────────────────────────────

    private static bool TryFindSensor(string reference, IReadOnlyList<ArgusSensor> sensors, out ArgusSensor? sensor,
        out string key)
    {
        sensor = null;
        key = string.Empty;
        if (!TryParseRef(reference, out ArgusSensorType type, out uint index, out string? labelHint))
            return false;

        // A sensor is identified by its ordinal position within its type, in Argus report order —
        // Argus does not give per-instance sensors (e.g. CPU cores) a distinct SensorIndex, so the
        // raw field cannot be used to tell them apart. The sampler keys its history the same way.
        // The ordinal shifts when sensors come or go, so a saved label wins when one still matches.
        IReadOnlyList<ArgusSensor> group = SensorsOfType(sensors, type);
        int ordinal = ResolveOrdinal(group, (int)index, labelHint);
        sensor = group.ElementAtOrDefault(ordinal);
        key = MetricKeys.ForSensor(type, ordinal);
        return sensor is not null;
    }

    /// <summary>
    /// The ordinal to read: the saved one when its label still matches or no label was saved,
    /// otherwise the sensor of that type with the saved label (the nearest one when several share
    /// it), and the saved ordinal again when no label matches.
    /// </summary>
    private static int ResolveOrdinal(IReadOnlyList<ArgusSensor> group, int ordinal, string? labelHint)
    {
        if (string.IsNullOrEmpty(labelHint))
            return ordinal;
        if (ordinal < group.Count && LabelMatches(group[ordinal], labelHint))
            return ordinal;

        int best = -1;
        for (int i = 0; i < group.Count; i++)
        {
            if (LabelMatches(group[i], labelHint) && (best < 0 || Math.Abs(i - ordinal) < Math.Abs(best - ordinal)))
                best = i;
        }

        return best >= 0 ? best : ordinal;
    }

    private static bool LabelMatches(ArgusSensor sensor, string labelHint) =>
        string.Equals(sensor.Label?.Trim(), labelHint, StringComparison.Ordinal);

    /// <summary>
    /// The persisted reference for a sensor: <c>&lt;Type&gt;:&lt;Ordinal&gt;:&lt;Label&gt;</c>. The
    /// label is a hint that keeps the button on the same sensor when the ordinal shifts; it is
    /// escaped so the host's command syntax (',' between parameters, ')' closing them, '&amp;&amp;'
    /// between commands) cannot split it. A sensor without a label gets the plain
    /// <c>&lt;Type&gt;:&lt;Ordinal&gt;</c> form.
    /// </summary>
    public static string Reference(ArgusSensor sensor, int ordinal)
    {
        string label = sensor.Label?.Trim() ?? string.Empty;
        return label.Length == 0
            ? $"{sensor.Type}:{ordinal}"
            : $"{sensor.Type}:{ordinal}:{EscapeLabel(label)}";
    }

    private static string EscapeLabel(string label) => label
        .Replace("%", "%25")
        .Replace(",", "%2C")
        .Replace("(", "%28")
        .Replace(")", "%29")
        .Replace("&", "%26");

    /// <summary>The sensors of a given type, in the order Argus reports them (the ordinal position in
    /// this list is the stable per-type index used by both the menu and the lookup). Indexed once per
    /// sensor snapshot, so a lookup does not walk every sensor.</summary>
    private static IReadOnlyList<ArgusSensor> SensorsOfType(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type)
    {
        TypeIndex? index = _typeIndex;
        if (index is null || !ReferenceEquals(index.Sensors, sensors))
            _typeIndex = index = new TypeIndex(sensors, IndexByType(sensors));

        return index.ByType.TryGetValue(type, out List<ArgusSensor>? group) ? group : [];
    }

    private static Dictionary<ArgusSensorType, List<ArgusSensor>> IndexByType(IReadOnlyList<ArgusSensor> sensors)
    {
        Dictionary<ArgusSensorType, List<ArgusSensor>> byType = [];
        foreach (ArgusSensor sensor in sensors)
        {
            if (!byType.TryGetValue(sensor.Type, out List<ArgusSensor>? group))
                byType[sensor.Type] = group = [];
            group.Add(sensor);
        }

        return byType;
    }

    private sealed record TypeIndex(
        IReadOnlyList<ArgusSensor> Sensors,
        Dictionary<ArgusSensorType, List<ArgusSensor>> ByType);

    // Replaced as a whole when the snapshot changes; render threads may race to build it, which
    // only costs a duplicate build.
    private static volatile TypeIndex? _typeIndex;

    private static bool TryParseRef(string raw, out ArgusSensorType type, out uint index, out string? labelHint)
    {
        type = ArgusSensorType.Invalid;
        index = 0;
        labelHint = null;

        // Three parts at most: the label hint may itself contain ':'.
        string[] parts = raw.Split(TagSeparators, 3, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
            return false;

        if (!Enum.TryParse(parts[0], ignoreCase: true, out type) || type == ArgusSensorType.Invalid)
            return false;

        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
            return true;

        if (parts.Length == 3 && parts[2].Length > 0)
            labelHint = Uri.UnescapeDataString(parts[2]);

        return uint.TryParse(parts[1], out index);
    }

    // ── Headers / captions ────────────────────────────────────────────────────

    private static SensorRow Placeholder(string header) => new(header, header, null);

    private static string HeaderFromRef(string reference)
    {
        int colon = reference.IndexOf(':');
        string typeToken = colon > 0 ? reference[..colon] : reference;
        return Enum.TryParse(typeToken, ignoreCase: true, out ArgusSensorType type)
            ? Header(type)
            : "Argus";
    }

    /// <summary>Short, user-facing header for a sensor type. Public so the menu can label the
    /// reading entries it builds with the same names the rendered rows use.</summary>
    public static string HeaderFor(ArgusSensorType type) => Header(type);

    private static string Header(ArgusSensor sensor)
    {
        string mapped = Header(sensor.Type);
        return string.IsNullOrEmpty(mapped) && !string.IsNullOrWhiteSpace(sensor.Label)
            ? sensor.Label
            : mapped;
    }

    /// <summary>
    /// Header for a single reading, disambiguated when the snapshot holds more than one sensor of
    /// the same type (e.g. per-core temperatures): appends the instance number so "CPU Core" reads
    /// "CPU Core 7". The number is taken from the sensor's own label (matching what the menu shows)
    /// with the ordinal position as a fallback.
    /// </summary>
    private static string SingleHeader(ArgusSensor sensor, IReadOnlyList<ArgusSensor> sensors)
    {
        string baseHeader = Header(sensor);

        IReadOnlyList<ArgusSensor> sameType = SensorsOfType(sensors, sensor.Type);
        if (sameType.Count <= 1)
            return baseHeader;

        // Prefer the number the sensor reports in its label (matches the menu); otherwise its ordinal
        // position within the type — SensorIndex is not reliable for per-instance sensors.
        int ordinal = 0;
        for (int i = 0; i < sameType.Count; i++)
        {
            if (ReferenceEquals(sameType[i], sensor))
            {
                ordinal = i;
                break;
            }
        }

        int number = TrailingNumber(sensor.Label) ?? ordinal;
        return $"{baseHeader} {number}";
    }

    /// <summary>
    /// Compact form of a header for use as a row label when several readings share the tile: drops a
    /// leading "CPU "/"GPU " subsystem word so e.g. "CPU Core 7" fits beside its value as "Core 7".
    /// Returns the header unchanged when there is nothing to drop.
    /// </summary>
    private static string ShortHeaderFrom(string header)
    {
        if ((header.StartsWith("CPU ", StringComparison.Ordinal) || header.StartsWith("GPU ", StringComparison.Ordinal))
            && header.Length > 4)
            return header[4..];

        return header;
    }

    /// <summary>Returns the integer at the end of <paramref name="label"/> (e.g. "Core 7" → 7), or
    /// null when the label has no trailing digits.</summary>
    private static int? TrailingNumber(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return null;

        int start = label.Length;
        while (start > 0 && char.IsDigit(label[start - 1]))
            start--;

        if (start == label.Length)
            return null;

        return int.TryParse(label.AsSpan(start), out int value) ? value : null;
    }

    private static string Header(ArgusSensorType type) => type switch
    {
        ArgusSensorType.CpuLoad => "CPU Load",
        ArgusSensorType.CpuPower => "CPU Power",
        ArgusSensorType.CpuTemperature => "CPU Core",
        ArgusSensorType.CpuTemperatureAdditional => "CPU Temp",
        ArgusSensorType.CpuMultiplier => "CPU Mult",
        ArgusSensorType.CpuMultiplierMax or ArgusSensorType.CpuMultiplierMin or ArgusSensorType.CpuMultiplierAvg => "CPU Mult",
        ArgusSensorType.CpuFrequency or ArgusSensorType.CpuFrequencyMax
            or ArgusSensorType.CpuFrequencyMin or ArgusSensorType.CpuFrequencyAvg => "CPU Freq",
        ArgusSensorType.CpuFrequencyFsb => "CPU FSB",
        ArgusSensorType.GpuLoad => "GPU Load",
        ArgusSensorType.GpuPower => "GPU Power",
        ArgusSensorType.GpuTemperature => "GPU Core",
        ArgusSensorType.GpuCoreClk => "GPU Clk",
        ArgusSensorType.GpuMemoryClk => "GPU Mem Clk",
        ArgusSensorType.GpuShaderClk => "GPU Shader",
        ArgusSensorType.GpuMemoryUsedMb or ArgusSensorType.GpuMemoryUsedPercent => "GPU Memory",
        ArgusSensorType.GpuFanSpeedPercent or ArgusSensorType.GpuFanSpeedRpm => "GPU Fan",
        ArgusSensorType.GpuName => "GPU",
        ArgusSensorType.RamUsage => "Memory",
        ArgusSensorType.DiskTemperature => "Storage",
        ArgusSensorType.DiskTransferRate => "Disk",
        ArgusSensorType.Temperature or ArgusSensorType.SyntheticTemperature => "Temp",
        ArgusSensorType.FanSpeedRpm or ArgusSensorType.FanControlValue => "Fan",
        ArgusSensorType.NetworkSpeed => "Network",
        ArgusSensorType.Battery => "Battery",
        _ => type.ToString()
    };

    private static string Caption(ArgusSensorType type) => type switch
    {
        ArgusSensorType.CpuFrequencyMax or ArgusSensorType.CpuMultiplierMax => "max",
        ArgusSensorType.CpuFrequencyMin or ArgusSensorType.CpuMultiplierMin => "min",
        ArgusSensorType.CpuFrequencyAvg or ArgusSensorType.CpuMultiplierAvg => "avg",
        _ => "cur"
    };

    private static bool IsTemperature(ArgusSensorType type) => type is
        ArgusSensorType.Temperature or ArgusSensorType.SyntheticTemperature or
        ArgusSensorType.CpuTemperature or ArgusSensorType.CpuTemperatureAdditional or
        ArgusSensorType.GpuTemperature or ArgusSensorType.DiskTemperature;
}
