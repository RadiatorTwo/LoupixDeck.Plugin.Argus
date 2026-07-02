using System.Globalization;

namespace LoupixDeck.Plugin.Argus.Tiles;

/// <summary>
/// Turns the persisted <c>Argus.Sensor</c> command parameter and a live sensor snapshot into a
/// <see cref="TileSpec"/>. Owns the plugin-private parameter grammar and the sensor→tile mapping
/// (short headers, unit scaling, value formatting), keeping <see cref="TileRenderer"/> pure.
///
/// <para>Parameter grammar (a single opaque string):</para>
/// <list type="bullet">
/// <item><c>single:&lt;Type&gt;:&lt;Index&gt;</c></item>
/// <item><c>double:&lt;Type&gt;:&lt;Index&gt;|&lt;Type&gt;:&lt;Index&gt;</c></item>
/// <item><c>memory:&lt;AbsType&gt;:&lt;Index&gt;[|&lt;PctType&gt;:&lt;Index&gt;]</c></item>
/// <item><c>multi:&lt;Type&gt;:&lt;Index&gt;,&lt;Index&gt;,…</c> or <c>multi:&lt;Type&gt;:*</c></item>
/// </list>
/// A parameter with no recognised prefix (e.g. the legacy <c>CpuLoad:0</c>) is read as
/// <c>single</c>, so buttons saved before this change keep working and render as a single-value tile.
/// </summary>
public static class ArgusTileSpecBuilder
{
    private static readonly char[] TagSeparators = [':'];

    public static TileSpec Build(string? parameter, IReadOnlyList<ArgusSensor> sensors, bool isAvailable)
    {
        if (!isAvailable)
            return Placeholder("Argus", "N/A");

        if (string.IsNullOrWhiteSpace(parameter))
            return Placeholder("Argus", "?");

        (string tag, string rest) = SplitTag(parameter.Trim());

        return tag switch
        {
            "double" => BuildDouble(rest, sensors),
            "memory" => BuildMemory(rest, sensors),
            "multi" => BuildMulti(rest, sensors),
            _ => BuildSingle(rest, sensors)
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

    private static TileSpec BuildSingle(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        if (!TryFindSensor(rest, sensors, out ArgusSensor? sensor))
            return Placeholder(HeaderFromRef(rest), "?");

        (string value, string unit) = Format(sensor!);
        return new TileSpec
        {
            Variant = TileVariant.SingleValue,
            Header = Header(sensor!),
            PrimaryValue = value,
            PrimaryUnit = unit
        };
    }

    private static TileSpec BuildDouble(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        string[] refs = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ArgusSensor? a = refs.Length > 0 && TryFindSensor(refs[0], sensors, out ArgusSensor? s0) ? s0 : null;
        ArgusSensor? b = refs.Length > 1 && TryFindSensor(refs[1], sensors, out ArgusSensor? s1) ? s1 : null;

        if (a is null && b is null)
            return Placeholder(refs.Length > 0 ? HeaderFromRef(refs[0]) : "Argus", "?");

        (string av, string au) = a is not null ? Format(a) : ("?", string.Empty);
        (string bv, string bu) = b is not null ? Format(b) : ("?", string.Empty);

        return new TileSpec
        {
            Variant = TileVariant.DoubleValue,
            Header = Header(a ?? b!),
            PrimaryValue = av,
            PrimaryUnit = au,
            SecondaryValue = bv,
            SecondaryUnit = bu,
            PrimaryCaption = a is not null ? Caption(a.Type) : null,
            SecondaryCaption = b is not null ? Caption(b.Type) : null
        };
    }

    private static TileSpec BuildMemory(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        string[] refs = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ArgusSensor? abs = refs.Length > 0 && TryFindSensor(refs[0], sensors, out ArgusSensor? s0) ? s0 : null;
        ArgusSensor? pct = refs.Length > 1 && TryFindSensor(refs[1], sensors, out ArgusSensor? s1) ? s1 : null;

        if (abs is null)
            return Placeholder(refs.Length > 0 ? HeaderFromRef(refs[0]) : "Memory", "?");

        (string value, string unit) = Format(abs);
        (string secValue, string secUnit) = pct is not null ? Format(pct) : (null!, null!);

        return new TileSpec
        {
            Variant = TileVariant.Memory,
            Header = Header(abs),
            PrimaryValue = value,
            PrimaryUnit = unit,
            SecondaryValue = pct is not null ? secValue : null,
            SecondaryUnit = pct is not null ? secUnit : null
        };
    }

    private static TileSpec BuildMulti(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        // "<Type>:<idx>,<idx>,..." or "<Type>:*"
        int colon = rest.IndexOf(':');
        string typeToken = colon > 0 ? rest[..colon] : rest;
        string indexToken = colon > 0 ? rest[(colon + 1)..] : "*";

        if (!Enum.TryParse(typeToken, ignoreCase: true, out ArgusSensorType type) || type == ArgusSensorType.Invalid)
            return Placeholder("Argus", "?");

        List<ArgusSensor> matches;
        if (indexToken.Trim() == "*")
        {
            matches = sensors.Where(s => s.Type == type).OrderBy(s => s.SensorIndex).ToList();
        }
        else
        {
            HashSet<uint> wanted = indexToken
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => uint.TryParse(t, out uint i) ? i : uint.MaxValue)
                .Where(i => i != uint.MaxValue)
                .ToHashSet();
            matches = sensors.Where(s => s.Type == type && wanted.Contains(s.SensorIndex))
                .OrderBy(s => s.SensorIndex).ToList();
        }

        if (matches.Count == 0)
            return Placeholder(Header(type), "?");

        bool isTemp = IsTemperature(type);
        List<TileRow> rows = new(matches.Count);
        foreach (ArgusSensor sensor in matches)
        {
            (string value, string unit) = Format(sensor);
            string rowLabel = isTemp ? $"T{sensor.SensorIndex + 1}" : $"#{sensor.SensorIndex}";
            rows.Add(new TileRow(rowLabel, value, unit));
        }

        return new TileSpec
        {
            Variant = TileVariant.MultiSensor,
            Header = Header(type),
            Rows = rows
        };
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    /// <summary>Formats a sensor's value + unit, scaling MB→G for memory-style readings.</summary>
    private static (string value, string unit) Format(ArgusSensor sensor)
    {
        double value = sensor.Value;
        string unit = sensor.Unit ?? string.Empty;

        if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
        {
            value /= 1024.0;
            unit = "G";
        }

        return (FormatNumber(value), unit);
    }

    /// <summary>One decimal place, but a trailing ".0" is dropped so whole numbers read as
    /// integers (36 °C) while fractional readings keep their decimal (6.6 %). Matches the design's
    /// example values without a per-type rule.</summary>
    private static string FormatNumber(double value)
    {
        string text = value.ToString("F1", CultureInfo.InvariantCulture);
        if (text.EndsWith(".0", StringComparison.Ordinal))
            text = text[..^2];
        return text;
    }

    // ── Sensor lookup ───────────────────────────────────────────────────────────

    private static bool TryFindSensor(string reference, IReadOnlyList<ArgusSensor> sensors, out ArgusSensor? sensor)
    {
        sensor = null;
        if (!TryParseRef(reference, out ArgusSensorType type, out uint index))
            return false;

        sensor = sensors.FirstOrDefault(s => s.Type == type && s.SensorIndex == index);
        return sensor is not null;
    }

    private static bool TryParseRef(string raw, out ArgusSensorType type, out uint index)
    {
        type = ArgusSensorType.Invalid;
        index = 0;

        string[] parts = raw.Split(TagSeparators, 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
            return false;

        if (!Enum.TryParse(parts[0], ignoreCase: true, out type) || type == ArgusSensorType.Invalid)
            return false;

        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
            return true;

        return uint.TryParse(parts[1], out index);
    }

    // ── Headers / captions ────────────────────────────────────────────────────

    private static TileSpec Placeholder(string header, string value) => new()
    {
        Variant = TileVariant.SingleValue,
        Header = header,
        PrimaryValue = value
    };

    private static string HeaderFromRef(string reference)
    {
        int colon = reference.IndexOf(':');
        string typeToken = colon > 0 ? reference[..colon] : reference;
        return Enum.TryParse(typeToken, ignoreCase: true, out ArgusSensorType type)
            ? Header(type)
            : "Argus";
    }

    /// <summary>Short, user-facing header for a sensor type. Public so the menu can label the
    /// tile entries it builds with the same names the rendered tiles use.</summary>
    public static string HeaderFor(ArgusSensorType type) => Header(type);

    private static string Header(ArgusSensor sensor)
    {
        string mapped = Header(sensor.Type);
        return string.IsNullOrEmpty(mapped) && !string.IsNullOrWhiteSpace(sensor.Label)
            ? sensor.Label
            : mapped;
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

    private static string? Caption(ArgusSensorType type) => type switch
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
