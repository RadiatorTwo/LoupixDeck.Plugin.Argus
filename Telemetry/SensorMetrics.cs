namespace LoupixDeck.Plugin.Argus.Telemetry;

/// <summary>
/// Describes a single Argus sensor as a metric: which native value to track, how to format it and
/// which alert rule applies. Used for every sensor, so any sensor a user puts on a tile gets a
/// history and a state.
/// </summary>
internal static class SensorMetrics
{
    /// <summary>The value to track, in the unit <see cref="Describe"/> formats: transfer rates are
    /// normalized to bytes per second, everything else is Argus' own value.</summary>
    public static double NativeValue(ArgusSensor sensor) => sensor.Type switch
    {
        ArgusSensorType.DiskTransferRate or ArgusSensorType.NetworkSpeed =>
            MetricFormatter.ToBytesPerSecond(sensor.Value, sensor.Unit) ?? sensor.Value,
        _ => sensor.Value
    };

    public static MetricInfo Describe(ArgusSensor sensor, int ordinal, double tjMax)
    {
        bool percent = (sensor.Unit ?? string.Empty).Trim() == "%";

        return sensor.Type switch
        {
            ArgusSensorType.CpuTemperature or ArgusSensorType.CpuTemperatureAdditional =>
                new MetricInfo(MetricFormat.Temperature, 30, tjMax, ThresholdKind.CpuTemperature),
            // Only the GPU core has the design's 80/88 limits; hot-spot and memory run hotter by design.
            ArgusSensorType.GpuTemperature =>
                new MetricInfo(MetricFormat.Temperature, 30, 95, ordinal == 0 ? ThresholdKind.GpuTemperature : ThresholdKind.None),
            ArgusSensorType.DiskTemperature =>
                new MetricInfo(MetricFormat.Temperature, 20, 80, ThresholdKind.StorageTemperature),
            ArgusSensorType.Temperature or ArgusSensorType.SyntheticTemperature =>
                new MetricInfo(MetricFormat.Temperature, 20, 100),

            ArgusSensorType.RamUsage when percent =>
                new MetricInfo(MetricFormat.Percent, 0, 100, ThresholdKind.RamLoad),
            ArgusSensorType.RamUsage or ArgusSensorType.GpuMemoryUsedMb =>
                new MetricInfo(MetricFormat.Megabytes, 0, 0),

            ArgusSensorType.CpuLoad or ArgusSensorType.GpuLoad =>
                new MetricInfo(MetricFormat.Percent, 0, 100, Smooth: true),
            ArgusSensorType.GpuFanSpeedPercent or ArgusSensorType.FanControlValue
                or ArgusSensorType.GpuMemoryUsedPercent or ArgusSensorType.Battery =>
                new MetricInfo(MetricFormat.Percent, 0, 100),

            ArgusSensorType.FanSpeedRpm =>
                new MetricInfo(MetricFormat.Rpm, 0, 3000, ThresholdKind.CpuFanStall, GrowToPeak: true),
            ArgusSensorType.GpuFanSpeedRpm =>
                new MetricInfo(MetricFormat.Rpm, 0, 3300, ThresholdKind.GpuFanStall, GrowToPeak: true),

            ArgusSensorType.CpuPower or ArgusSensorType.GpuPower =>
                new MetricInfo(MetricFormat.Watt, 0, 100, GrowToPeak: true),

            ArgusSensorType.CpuFrequency or ArgusSensorType.CpuFrequencyMax or ArgusSensorType.CpuFrequencyMin
                or ArgusSensorType.CpuFrequencyAvg =>
                new MetricInfo(MetricFormat.ClockMhz, 0, 6000, Smooth: true, GrowToPeak: true),
            ArgusSensorType.GpuCoreClk or ArgusSensorType.GpuMemoryClk or ArgusSensorType.GpuShaderClk =>
                new MetricInfo(MetricFormat.ClockMhz, 0, 3200, Smooth: true, GrowToPeak: true),
            ArgusSensorType.CpuMultiplier or ArgusSensorType.CpuMultiplierMax or ArgusSensorType.CpuMultiplierMin
                or ArgusSensorType.CpuMultiplierAvg =>
                new MetricInfo(MetricFormat.Multiplier, 0, 60, Smooth: true, GrowToPeak: true),

            ArgusSensorType.DiskTransferRate or ArgusSensorType.NetworkSpeed =>
                new MetricInfo(MetricFormat.BytesPerSecond, 0, 0),

            _ when percent => new MetricInfo(MetricFormat.Percent, 0, 100),
            _ => new MetricInfo(MetricFormat.Generic, 0, 0, Unit: sensor.Unit)
        };
    }
}
