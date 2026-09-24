using LoupixDeck.Plugin.Argus.Telemetry;

namespace LoupixDeck.Plugin.Argus.Rendering.Tiles;

/// <summary>One row of a component page: a metric and its label.</summary>
internal sealed record PageRow(string Metric, string Label);

/// <summary>
/// A component page (design Fig. 1, layout C): a header, one hero value with its unit and a bar,
/// then up to three rows. A page with fewer rows fills the rest with the history chart of
/// <see cref="Spark"/>. The summary page (<see cref="IsSummary"/>) is the all-at-once grid of
/// layout B instead.
/// </summary>
internal sealed record ComponentPage(
    string Id,
    string Title,
    string Hero,
    string HeroLabel,
    IReadOnlyList<PageRow> Rows,
    string? Spark = null,
    bool IsSummary = false);

/// <summary>The pages a paging tile can show, and the parameter grammar that selects them.</summary>
internal static class ComponentPages
{
    public static ComponentPage Cpu { get; } = new("cpu", "CPU", PageMetrics.CpuTemp, "TEMP",
        [new(PageMetrics.CpuClock, "CLK"), new(PageMetrics.CpuFan, "FAN"), new(PageMetrics.CpuLoad, "LOAD")]);

    public static ComponentPage Gpu { get; } = new("gpu", "GPU", PageMetrics.GpuTemp, "TEMP",
        [new(PageMetrics.GpuClock, "CLK"), new(PageMetrics.GpuFan, "FAN"), new(PageMetrics.GpuLoad, "LOAD")]);

    public static ComponentPage Ram { get; } = new("ram", "RAM", PageMetrics.RamLoad, "LOAD",
        [new(PageMetrics.RamUsed, "USED"), new(PageMetrics.RamFree, "FREE")], Spark: PageMetrics.RamLoad);

    public static ComponentPage Net { get; } = new("net", "NET", PageMetrics.NetDown, "DOWN",
        [new(PageMetrics.NetUp, "UP")], Spark: PageMetrics.NetDown);

    public static ComponentPage Disk { get; } = new("disk", "DISK", PageMetrics.DiskTemp, "TEMP",
        [new(PageMetrics.DiskRead, "READ"), new(PageMetrics.DiskWrite, "WRITE")], Spark: PageMetrics.DiskRead);

    /// <summary>All four CPU metrics at 2× (layout B). Argus has no core voltage, so load takes
    /// the design's VCORE row.</summary>
    public static ComponentPage Summary { get; } = new("sum", "CPU", PageMetrics.CpuTemp, "TEMP",
        [new(PageMetrics.CpuTemp, "TEMP"), new(PageMetrics.CpuClock, "CLK"), new(PageMetrics.CpuFan, "FAN"),
            new(PageMetrics.CpuLoad, "LOAD")], IsSummary: true);

    public static IReadOnlyList<ComponentPage> All { get; } = [Cpu, Gpu, Ram, Net, Disk, Summary];

    /// <summary>The cycle used when a tile names no pages: every component page, then the summary.</summary>
    public const string DefaultSelection = "cpu,gpu,ram,net,disk,sum";

    /// <summary>Parses a comma-separated page list ("cpu,gpu,sum"). Unknown ids are skipped; an
    /// empty or entirely unknown list falls back to <see cref="DefaultSelection"/>.</summary>
    public static IReadOnlyList<ComponentPage> Parse(string? selection)
    {
        List<ComponentPage> pages = [];
        foreach (string id in (selection ?? string.Empty).Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ComponentPage? page = All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (page is not null)
                pages.Add(page);
        }

        return pages.Count > 0 ? pages : Parse(DefaultSelection);
    }

    /// <summary>A page is shown only while its hero metric has data (e.g. NET disappears when
    /// Argus has network monitoring off).</summary>
    public static bool IsAvailable(ComponentPage page, TelemetryFrame frame) => frame.Get(page.Hero) is not null;
}
