using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus.Rendering;

/// <summary>
/// Turns a persisted <c>Argus.Sensor</c> command parameter and a live sensor snapshot into one or
/// more <see cref="SensorReading"/>s. Owns the sensor→display mapping (short headers, unit scaling,
/// value formatting, gauge scaling) so <see cref="SensorRenderer"/> stays a pure drawing component.
///
/// <para>The current model is <b>one sensor per command</b>: a parameter is a plain
/// <c>&lt;Type&gt;:&lt;Index&gt;</c> reference and yields a single reading; the user composes a
/// multi-sensor button by chaining several commands (the renderer lays them out as rows).</para>
///
/// <para>For backward compatibility the former packed grammar is still parsed, so buttons saved
/// before the rework keep rendering (now as one row per contained sensor):</para>
/// <list type="bullet">
/// <item><c>single:&lt;Type&gt;:&lt;Index&gt;</c> → one reading</item>
/// <item><c>double:&lt;Type&gt;:&lt;Index&gt;|&lt;Type&gt;:&lt;Index&gt;</c> → two readings</item>
/// <item><c>memory:&lt;AbsType&gt;:&lt;Index&gt;[|&lt;PctType&gt;:&lt;Index&gt;]</c> → abs (+ percent) reading</item>
/// <item><c>multi:&lt;Type&gt;:&lt;Index&gt;,…</c> or <c>multi:&lt;Type&gt;:*</c> → one reading per match</item>
/// </list>
/// A parameter with no recognised prefix (e.g. <c>CpuLoad:0</c>) is read as a single reference.
/// </summary>
public static class ArgusReadingBuilder
{
    private static readonly char[] TagSeparators = [':'];

    public static IReadOnlyList<SensorReading> Build(string? parameter, IReadOnlyList<ArgusSensor> sensors, bool isAvailable)
    {
        if (!isAvailable)
            return [Placeholder("Argus", "N/A")];

        if (string.IsNullOrWhiteSpace(parameter))
            return [Placeholder("Argus", "?")];

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

    private static SensorReading BuildSingle(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        if (!TryFindSensor(rest, sensors, out ArgusSensor? sensor))
            return Placeholder(HeaderFromRef(rest), "?");

        (string value, string unit) = Format(sensor!);
        return new SensorReading(SingleHeader(sensor!, sensors), value, unit, Fraction(sensor!, sensors), Accent(sensor!.Type));
    }

    private static IReadOnlyList<SensorReading> BuildDouble(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        string[] refs = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<SensorReading> readings = [];
        foreach (string reference in refs)
        {
            if (!TryFindSensor(reference, sensors, out ArgusSensor? sensor))
                continue;

            (string value, string unit) = Format(sensor!);
            // Distinguish the two same-type readings by their role (cur/avg/max/min).
            readings.Add(new SensorReading(Caption(sensor!.Type), value, unit, Fraction(sensor!, sensors), Accent(sensor!.Type)));
        }

        if (readings.Count == 0)
            return [Placeholder(refs.Length > 0 ? HeaderFromRef(refs[0]) : "Argus", "?")];

        return readings;
    }

    private static IReadOnlyList<SensorReading> BuildMemory(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        string[] refs = rest.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ArgusSensor? abs = refs.Length > 0 && TryFindSensor(refs[0], sensors, out ArgusSensor? s0) ? s0 : null;
        ArgusSensor? pct = refs.Length > 1 && TryFindSensor(refs[1], sensors, out ArgusSensor? s1) ? s1 : null;

        if (abs is null)
            return [Placeholder(refs.Length > 0 ? HeaderFromRef(refs[0]) : "Memory", "?")];

        double? fill = pct is not null ? Math.Clamp(pct.Value / 100.0, 0.0, 1.0) : null;
        (string absValue, string absUnit) = Format(abs);

        List<SensorReading> readings = [new SensorReading(Header(abs), absValue, absUnit, fill, Accent(abs.Type))];
        if (pct is not null)
        {
            (string pctValue, string pctUnit) = Format(pct);
            readings.Add(new SensorReading("Used", pctValue, pctUnit, fill, Accent(abs.Type)));
        }

        return readings;
    }

    private static IReadOnlyList<SensorReading> BuildMulti(string rest, IReadOnlyList<ArgusSensor> sensors)
    {
        // "<Type>:<idx>,<idx>,..." or "<Type>:*"
        int colon = rest.IndexOf(':');
        string typeToken = colon > 0 ? rest[..colon] : rest;
        string indexToken = colon > 0 ? rest[(colon + 1)..] : "*";

        if (!Enum.TryParse(typeToken, ignoreCase: true, out ArgusSensorType type) || type == ArgusSensorType.Invalid)
            return [Placeholder("Argus", "?")];

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
            return [Placeholder(Header(type), "?")];

        bool isTemp = IsTemperature(type);
        List<SensorReading> readings = new(matches.Count);
        foreach (ArgusSensor sensor in matches)
        {
            (string value, string unit) = Format(sensor);
            string label = isTemp ? $"T{sensor.SensorIndex + 1}" : $"#{sensor.SensorIndex}";
            readings.Add(new SensorReading(label, value, unit, Fraction(sensor, sensors), Accent(type)));
        }

        return readings;
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
    /// integers (36 °C) while fractional readings keep their decimal (6.6 %).</summary>
    private static string FormatNumber(double value)
    {
        string text = value.ToString("F1", CultureInfo.InvariantCulture);
        if (text.EndsWith(".0", StringComparison.Ordinal))
            text = text[..^2];
        return text;
    }

    // ── Gauge fill ──────────────────────────────────────────────────────────────

    // Nominal full-scale values for readings that have no natural 0..100 range. Centralised here
    // so the bar scaling can be tuned in one place. Percentages ignore these (they are already 0..100).
    private const double TempMaxC = 100.0;
    private const double PowerMaxW = 100.0;
    private const double FanRpmMax = 3000.0;
    private const double FreqMaxDefaultMhz = 6000.0;
    private const double MultiplierMaxDefault = 60.0;

    /// <summary>Returns the 0..1 gauge fill for a sensor, or null when the reading has no meaningful
    /// scale (so no bar is drawn). Percent readings use their value directly; others divide by a
    /// nominal per-type maximum.</summary>
    private static double? Fraction(ArgusSensor sensor, IReadOnlyList<ArgusSensor> sensors)
    {
        double? max = MaxFor(sensor, sensors);
        if (max is null or <= 0)
            return null;

        return Math.Clamp(sensor.Value / max.Value, 0.0, 1.0);
    }

    private static double? MaxFor(ArgusSensor sensor, IReadOnlyList<ArgusSensor> sensors)
    {
        // Anything already expressed in percent is 0..100.
        if ((sensor.Unit ?? string.Empty).Equals("%", StringComparison.OrdinalIgnoreCase))
            return 100.0;

        switch (sensor.Type)
        {
            case ArgusSensorType.Temperature:
            case ArgusSensorType.SyntheticTemperature:
            case ArgusSensorType.CpuTemperature:
            case ArgusSensorType.CpuTemperatureAdditional:
            case ArgusSensorType.GpuTemperature:
            case ArgusSensorType.DiskTemperature:
                return TempMaxC;

            case ArgusSensorType.CpuLoad:
            case ArgusSensorType.GpuLoad:
            case ArgusSensorType.GpuFanSpeedPercent:
            case ArgusSensorType.FanControlValue:
            case ArgusSensorType.GpuMemoryUsedPercent:
            case ArgusSensorType.Battery:
                return 100.0;

            case ArgusSensorType.CpuPower:
            case ArgusSensorType.GpuPower:
                return PowerMaxW;

            case ArgusSensorType.FanSpeedRpm:
            case ArgusSensorType.GpuFanSpeedRpm:
                return FanRpmMax;

            case ArgusSensorType.CpuFrequency:
            case ArgusSensorType.CpuFrequencyAvg:
            case ArgusSensorType.CpuFrequencyMin:
            case ArgusSensorType.CpuFrequencyMax:
            case ArgusSensorType.CpuFrequencyFsb:
                double freqMax = sensors.FirstOrDefault(s => s.Type == ArgusSensorType.CpuFrequencyMax)?.Value ?? 0;
                return freqMax > 0 ? freqMax : FreqMaxDefaultMhz;

            case ArgusSensorType.CpuMultiplier:
            case ArgusSensorType.CpuMultiplierMax:
            case ArgusSensorType.CpuMultiplierMin:
            case ArgusSensorType.CpuMultiplierAvg:
                double multMax = sensors.FirstOrDefault(s => s.Type == ArgusSensorType.CpuMultiplierMax)?.Value ?? 0;
                return multMax > 0 ? multMax : MultiplierMaxDefault;

            default:
                // Clocks (non-CPU), transfer rates, network speed, GPU name, absolute memory (MB/G):
                // no natural scale → no bar.
                return null;
        }
    }

    // ── Accent (bar tint per subsystem) ───────────────────────────────────────────

    private static readonly PluginColor AccentCpu = new(0x53, 0x6D, 0x9E);      // muted steel blue
    private static readonly PluginColor AccentGpu = new(0x57, 0x9E, 0x63);      // muted green
    private static readonly PluginColor AccentMemory = new(0xA8, 0x5C, 0x5C);   // muted red
    private static readonly PluginColor AccentStorage = new(0xB0, 0x92, 0x42);  // muted amber
    private static readonly PluginColor AccentTemp = new(0xC0, 0x76, 0x40);     // muted orange

    /// <summary>Muted accent tint for a subsystem, used only for the gauge fill. Null → the theme's
    /// neutral default bar color.</summary>
    private static PluginColor? Accent(ArgusSensorType type) => type switch
    {
        ArgusSensorType.CpuLoad or ArgusSensorType.CpuPower or ArgusSensorType.CpuTemperature
            or ArgusSensorType.CpuTemperatureAdditional or ArgusSensorType.CpuMultiplier
            or ArgusSensorType.CpuMultiplierMax or ArgusSensorType.CpuMultiplierMin or ArgusSensorType.CpuMultiplierAvg
            or ArgusSensorType.CpuFrequency or ArgusSensorType.CpuFrequencyMax or ArgusSensorType.CpuFrequencyMin
            or ArgusSensorType.CpuFrequencyAvg or ArgusSensorType.CpuFrequencyFsb => AccentCpu,

        ArgusSensorType.GpuLoad or ArgusSensorType.GpuPower or ArgusSensorType.GpuTemperature
            or ArgusSensorType.GpuCoreClk or ArgusSensorType.GpuMemoryClk or ArgusSensorType.GpuShaderClk
            or ArgusSensorType.GpuMemoryUsedMb or ArgusSensorType.GpuMemoryUsedPercent
            or ArgusSensorType.GpuFanSpeedPercent or ArgusSensorType.GpuFanSpeedRpm => AccentGpu,

        ArgusSensorType.RamUsage => AccentMemory,
        ArgusSensorType.DiskTemperature or ArgusSensorType.DiskTransferRate => AccentStorage,
        ArgusSensorType.Temperature or ArgusSensorType.SyntheticTemperature => AccentTemp,
        _ => null
    };

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

    private static SensorReading Placeholder(string header, string value) =>
        new(header, value, string.Empty);

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
    /// with the raw <see cref="ArgusSensor.SensorIndex"/> as a fallback.
    /// </summary>
    private static string SingleHeader(ArgusSensor sensor, IReadOnlyList<ArgusSensor> sensors)
    {
        string baseHeader = Header(sensor);

        int sameType = 0;
        foreach (ArgusSensor s in sensors)
        {
            if (s.Type == sensor.Type && ++sameType > 1)
                break;
        }

        if (sameType <= 1)
            return baseHeader;

        int number = TrailingNumber(sensor.Label) ?? (int)sensor.SensorIndex;
        return $"{baseHeader} {number}";
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
