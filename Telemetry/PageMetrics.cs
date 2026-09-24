namespace LoupixDeck.Plugin.Argus.Telemetry;

/// <summary>
/// The derived metrics the component pages show (CPU temperature, GPU clock, RAM free …). Each is
/// picked out of the Argus snapshot by position within a sensor type, never by label: Argus
/// labels are localized ("Temperatur Hot Spot") and user-named (fans), while the order within a
/// type is stable.
/// </summary>
internal static class PageMetrics
{
    public const string CpuTemp = "cpu.temp";
    public const string CpuClock = "cpu.clock";
    public const string CpuFan = "cpu.fan";
    public const string CpuLoad = "cpu.load";
    public const string CpuPower = "cpu.power";
    public const string GpuTemp = "gpu.temp";
    public const string GpuClock = "gpu.clock";
    public const string GpuFan = "gpu.fan";
    public const string GpuLoad = "gpu.load";
    public const string RamLoad = "ram.load";
    public const string RamUsed = "ram.used";
    public const string RamFree = "ram.free";
    public const string NetDown = "net.down";
    public const string NetUp = "net.up";
    public const string DiskTemp = "disk.temp";
    public const string DiskRead = "disk.read";
    public const string DiskWrite = "disk.write";

    /// <summary>One derived metric: how to read it from a snapshot and how to describe it.</summary>
    public sealed record Definition(
        string Id,
        Func<IReadOnlyList<ArgusSensor>, double?> Read,
        Func<double, MetricInfo> Describe);

    public static IReadOnlyList<Definition> All { get; } =
    [
        // The hottest per-core reading is Tctl/Tdie on AMD and the package's hottest core on Intel.
        new(CpuTemp,
            s => Max(s, ArgusSensorType.CpuTemperature) ?? Max(s, ArgusSensorType.CpuTemperatureAdditional),
            tj => new MetricInfo(MetricFormat.Temperature, 30, tj, ThresholdKind.CpuTemperature)),
        new(CpuClock,
            s => At(s, ArgusSensorType.CpuFrequencyMax, 0) ?? Max(s, ArgusSensorType.CpuFrequency),
            _ => new MetricInfo(MetricFormat.ClockMhz, 800, 5800, Smooth: true, GrowToPeak: true)),
        new(CpuFan,
            s => At(s, ArgusSensorType.FanSpeedRpm, 0),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 2500, ThresholdKind.CpuFanStall, GrowToPeak: true)),
        // CpuLoad #0 is the total; #1/#2 are the user and system shares.
        new(CpuLoad,
            s => At(s, ArgusSensorType.CpuLoad, 0),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),
        new(CpuPower,
            s => At(s, ArgusSensorType.CpuPower, 0),
            _ => new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true)),

        new(GpuTemp,
            s => At(s, ArgusSensorType.GpuTemperature, 0),
            _ => new MetricInfo(MetricFormat.Temperature, 30, 95, ThresholdKind.GpuTemperature)),
        new(GpuClock,
            s => At(s, ArgusSensorType.GpuCoreClk, 0),
            _ => new MetricInfo(MetricFormat.ClockMhz, 200, 3200, Smooth: true, GrowToPeak: true)),
        new(GpuFan,
            s => At(s, ArgusSensorType.GpuFanSpeedRpm, 0),
            _ => new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true)),
        new(GpuLoad,
            s => At(s, ArgusSensorType.GpuLoad, 0),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true)),

        // RamUsage carries the load in % plus total and used in MB; total is the larger MB value.
        new(RamLoad,
            s => FirstWithUnit(s, ArgusSensorType.RamUsage, percent: true),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100, ThresholdKind.RamLoad)),
        new(RamUsed,
            s => RamMegabytes(s)?.Used,
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),
        new(RamFree,
            s => RamMegabytes(s) is { } ram ? ram.Total - ram.Used : null,
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),

        // Argus names the direction in the unit ("Bytes/sec (up)"); it reports send before
        // receive, so position alone would swap them. With several adapters the first one counts.
        new(NetDown,
            s => Directed(s, ArgusSensorType.NetworkSpeed, "(down)", "(up)", 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(NetUp,
            s => Directed(s, ArgusSensorType.NetworkSpeed, "(up)", "(down)", 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),

        new(DiskTemp,
            s => Max(s, ArgusSensorType.DiskTemperature),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature)),
        // Argus labels the direction ("Data transfer rate (read)"); #0 is read, #1 write.
        new(DiskRead,
            s => Directed(s, ArgusSensorType.DiskTransferRate, "(read)", "(write)", 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(DiskWrite,
            s => Directed(s, ArgusSensorType.DiskTransferRate, "(write)", "(read)", 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0))
    ];

    private static double? At(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type, int ordinal)
    {
        int seen = 0;
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type != type)
                continue;
            if (seen++ == ordinal)
                return sensor.Value;
        }

        return null;
    }

    private static double? Max(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type)
    {
        double? max = null;
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type == type && (max is null || sensor.Value > max))
                max = sensor.Value;
        }

        return max;
    }

    private static double? Rate(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type, int ordinal)
    {
        int seen = 0;
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type != type)
                continue;
            if (seen++ == ordinal)
                return SensorMetrics.NativeValue(sensor);
        }

        return null;
    }

    /// <summary>
    /// The first rate of <paramref name="type"/> whose unit or label names
    /// <paramref name="direction"/>; the one at <paramref name="fallbackOrdinal"/> when no sensor
    /// of the type names either <paramref name="direction"/> or <paramref name="opposite"/>.
    /// </summary>
    private static double? Directed(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type, string direction,
        string opposite, int fallbackOrdinal)
    {
        bool anyDirection = false;
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type != type)
                continue;

            if (Names(sensor, direction))
                return SensorMetrics.NativeValue(sensor);

            anyDirection |= Names(sensor, opposite);
        }

        return anyDirection ? null : Rate(sensors, type, fallbackOrdinal);

        static bool Names(ArgusSensor sensor, string direction) =>
            (sensor.Unit?.Contains(direction, StringComparison.OrdinalIgnoreCase) ?? false)
            || (sensor.Label?.Contains(direction, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static double? FirstWithUnit(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type, bool percent)
    {
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type == type && (((sensor.Unit ?? string.Empty).Trim() == "%") == percent))
                return sensor.Value;
        }

        return null;
    }

    private static (double Total, double Used)? RamMegabytes(IReadOnlyList<ArgusSensor> sensors)
    {
        List<double> megabytes = [];
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type == ArgusSensorType.RamUsage
                && (sensor.Unit ?? string.Empty).Trim().Equals("MB", StringComparison.OrdinalIgnoreCase))
                megabytes.Add(sensor.Value);
        }

        return megabytes.Count >= 2 ? (megabytes.Max(), megabytes.Min()) : null;
    }
}
