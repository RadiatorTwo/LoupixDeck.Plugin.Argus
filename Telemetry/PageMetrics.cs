namespace LoupixDeck.Plugin.Argus.Telemetry;

/// <summary>
/// The derived metrics the component pages show (CPU temperature, GPU clock, RAM free …). Each is
/// picked out of the Argus snapshot by position within a sensor type, not by label text: Argus
/// labels are localized ("Temperatur Hot Spot") and user-named (fans), while the order within a
/// type is stable. Labels only tell apart what the position cannot: the direction of a rate and
/// which network adapter a rate belongs to.
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
    public const string GpuPower = "gpu.power";
    public const string PowerTotal = "pwr.total";
    public const string VramLoad = "vram.load";
    public const string VramUsed = "vram.used";
    public const string VramFree = "vram.free";
    public const string BatteryLevel = "bat.level";

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
        // receive, so position alone would swap them. With several adapters the busiest one counts
        // (see Observe).
        new(NetDown,
            s => Directed(MainAdapterRates(s), "(down)", "(up)", 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(NetUp,
            s => Directed(MainAdapterRates(s), "(up)", "(down)", 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),

        new(DiskTemp,
            s => Max(s, ArgusSensorType.DiskTemperature),
            _ => new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature)),
        // Argus labels the direction ("Data transfer rate (read)"); #0 is read, #1 write. Every
        // rate in a direction is summed, so several drives add up.
        new(DiskRead,
            s => Summed(OfType(s, ArgusSensorType.DiskTransferRate), "(read)", "(write)", 0),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),
        new(DiskWrite,
            s => Summed(OfType(s, ArgusSensorType.DiskTransferRate), "(write)", "(read)", 1),
            _ => new MetricInfo(MetricFormat.BytesPerSecond, 0, 0)),

        new(GpuPower,
            s => At(s, ArgusSensorType.GpuPower, 0),
            _ => new MetricInfo(MetricFormat.Watt, 0, 300, GrowToPeak: true)),
        // CPU package (CpuPower #0) plus GPU board power; whichever Argus reports when one is missing.
        new(PowerTotal,
            s => At(s, ArgusSensorType.CpuPower, 0) is { } cpu
                ? cpu + (At(s, ArgusSensorType.GpuPower, 0) ?? 0)
                : At(s, ArgusSensorType.GpuPower, 0),
            _ => new MetricInfo(MetricFormat.Watt, 0, 400, GrowToPeak: true)),

        // Argus reports VRAM used in % and in MB, not the total; the total follows from the two.
        new(VramLoad,
            s => At(s, ArgusSensorType.GpuMemoryUsedPercent, 0),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100)),
        new(VramUsed,
            s => At(s, ArgusSensorType.GpuMemoryUsedMb, 0),
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),
        new(VramFree,
            s => At(s, ArgusSensorType.GpuMemoryUsedMb, 0) is { } used
                 && At(s, ArgusSensorType.GpuMemoryUsedPercent, 0) is > 0 and var percent
                ? Math.Max(0, (used * 100 / percent) - used)
                : null,
            _ => new MetricInfo(MetricFormat.Megabytes, 0, 0)),

        // The charge level: the battery reading in %, else the first one. Absent on desktops, so the
        // battery page is skipped there.
        new(BatteryLevel,
            s => FirstWithUnit(s, ArgusSensorType.Battery, percent: true) ?? At(s, ArgusSensorType.Battery, 0),
            _ => new MetricInfo(MetricFormat.Percent, 0, 100))
    ];

    // ── NET: the busiest adapter ─────────────────────────────────────────────────

    // Argus reports per-adapter rates only, no totals, so the NET page follows the adapter with the
    // most traffic averaged over the last few seconds (EMA, α 0.2 per one-second sample). Another
    // adapter takes over only once its average beats the current one by a clear margin, so the
    // page does not jump between two quiet adapters.
    private const double AdapterAlpha = 0.2;
    private const double AdapterSwitchMargin = 1.25;

    // Adapter name (the sensor label) → average traffic in bytes per second, up plus down.
    private static readonly Dictionary<string, double> AdapterTraffic = new(StringComparer.Ordinal);
    private static string? _mainAdapter;

    /// <summary>
    /// Feeds one snapshot into the adapter averages. The sampler calls it once per sample, before
    /// reading <see cref="All"/>, and under the same lock.
    /// </summary>
    public static void Observe(IReadOnlyList<ArgusSensor> sensors)
    {
        Dictionary<string, double> traffic = new(StringComparer.Ordinal);
        foreach (ArgusSensor sensor in sensors)
        {
            if (sensor.Type != ArgusSensorType.NetworkSpeed)
                continue;

            double rate = SensorMetrics.NativeValue(sensor);
            string adapter = Adapter(sensor);
            traffic[adapter] = traffic.GetValueOrDefault(adapter) + (double.IsFinite(rate) ? Math.Max(0, rate) : 0);
        }

        foreach (string gone in AdapterTraffic.Keys.Where(adapter => !traffic.ContainsKey(adapter)).ToList())
            AdapterTraffic.Remove(gone);

        foreach ((string adapter, double rate) in traffic)
        {
            AdapterTraffic[adapter] = AdapterTraffic.TryGetValue(adapter, out double average)
                ? average + (AdapterAlpha * (rate - average))
                : rate;
        }

        if (AdapterTraffic.Count == 0)
        {
            _mainAdapter = null;
            return;
        }

        KeyValuePair<string, double> busiest = AdapterTraffic.MaxBy(pair => pair.Value);
        if (_mainAdapter is null
            || !AdapterTraffic.TryGetValue(_mainAdapter, out double current)
            || busiest.Value > (current * AdapterSwitchMargin))
            _mainAdapter = busiest.Key;
    }

    /// <summary>The network rates of the busiest adapter; every network rate before the first
    /// <see cref="Observe"/>.</summary>
    private static List<ArgusSensor> MainAdapterRates(IReadOnlyList<ArgusSensor> sensors) =>
        sensors.Where(s => s.Type == ArgusSensorType.NetworkSpeed && (_mainAdapter is null || Adapter(s) == _mainAdapter))
            .ToList();

    // Argus labels each network rate with its adapter's name; the direction is in the unit.
    private static string Adapter(ArgusSensor sensor) => sensor.Label?.Trim() ?? string.Empty;

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

    private static List<ArgusSensor> OfType(IReadOnlyList<ArgusSensor> sensors, ArgusSensorType type) =>
        sensors.Where(s => s.Type == type).ToList();

    /// <summary>
    /// The first of <paramref name="rates"/> whose unit or label names <paramref name="direction"/>;
    /// the one at <paramref name="fallbackOrdinal"/> when no rate names either
    /// <paramref name="direction"/> or <paramref name="opposite"/>.
    /// </summary>
    private static double? Directed(List<ArgusSensor> rates, string direction, string opposite, int fallbackOrdinal)
    {
        ArgusSensor? named = rates.FirstOrDefault(s => Names(s, direction));
        if (named is not null)
            return SensorMetrics.NativeValue(named);

        return Fallback(rates, opposite, fallbackOrdinal);
    }

    /// <summary>
    /// Every rate in <paramref name="rates"/> that names <paramref name="direction"/>, summed; the
    /// one at <paramref name="fallbackOrdinal"/> when no rate names either direction.
    /// </summary>
    private static double? Summed(List<ArgusSensor> rates, string direction, string opposite, int fallbackOrdinal)
    {
        double? total = null;
        foreach (ArgusSensor sensor in rates)
        {
            if (Names(sensor, direction))
                total = (total ?? 0) + SensorMetrics.NativeValue(sensor);
        }

        return total ?? Fallback(rates, opposite, fallbackOrdinal);
    }

    private static double? Fallback(List<ArgusSensor> rates, string opposite, int fallbackOrdinal) =>
        rates.Any(s => Names(s, opposite)) || rates.ElementAtOrDefault(fallbackOrdinal) is not { } sensor
            ? null
            : SensorMetrics.NativeValue(sensor);

    private static bool Names(ArgusSensor sensor, string direction) =>
        (sensor.Unit?.Contains(direction, StringComparison.OrdinalIgnoreCase) ?? false)
        || (sensor.Label?.Contains(direction, StringComparison.OrdinalIgnoreCase) ?? false);

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
