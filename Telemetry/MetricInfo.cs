namespace LoupixDeck.Plugin.Argus.Telemetry;

/// <summary>
/// How a metric is formatted, scaled and judged. <see cref="Min"/>/<see cref="Max"/> are the bar
/// and chart range in native units; a range with <c>Max &lt;= Min</c> means "no bar". With
/// <see cref="GrowToPeak"/> the range widens to the highest value seen in the history, so an
/// open-ended reading (power, RPM) never pegs the bar.
/// </summary>
internal sealed record MetricInfo(
    MetricFormat Format,
    double Min,
    double Max,
    ThresholdKind Threshold = ThresholdKind.None,
    bool Smooth = false,
    bool GrowToPeak = false,
    string? Unit = null)
{
    public bool HasRange => Max > Min;
}

/// <summary>Stable keys for tracked metrics.</summary>
internal static class MetricKeys
{
    /// <summary>The key of one Argus sensor: its type and its ordinal position within that type,
    /// in Argus report order — the same "Type:Index" grammar the Argus.Sensor parameter uses.</summary>
    public static string ForSensor(ArgusSensorType type, int ordinal) => $"{type}:{ordinal}";
}
