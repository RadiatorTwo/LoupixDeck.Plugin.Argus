using System.Collections.Concurrent;
using LoupixDeck.Plugin.Argus.Rendering;
using LoupixDeck.Plugin.Argus.Rendering.Pixel;
using LoupixDeck.Plugin.Argus.Rendering.Tiles;
using LoupixDeck.Plugin.Argus.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// A paging hardware tile (design Fig. 1): one component per page — CPU, GPU, RAM, NET, DISK and
/// the all-at-once summary — and a key press moves that button to the next page. The "Pages"
/// parameter lists the cycle ("cpu,gpu,sum"); a single id makes a fixed tile. Pages without data
/// (e.g. NET while Argus' network monitoring is off) are skipped, and the header's n/N counts only
/// the pages shown. The current page is kept per button and resets on a restart.
/// </summary>
internal sealed class ArgusPagesCommand(TelemetrySampler telemetry) : IAnimatedDisplayCommand, IDisplayImageCommand
{
    public const string CommandName = "Argus.Pages";

    private readonly ConcurrentDictionary<string, int> _positions = new(StringComparer.Ordinal);

    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = CommandName,
        DisplayName = "Argus Pages",
        Group = "Argus Monitor",
        Icon = "\U000F0379",
        Description = "Show hardware pages on a touch button; press it for the next page",
        ParameterTemplate = "({Pages})",
        Parameters = [new CommandParameter("Pages", typeof(string))],
        // Surfaced per page selection through the dynamic menu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public int TargetFps => PixelTile.TargetFps;

    public TimeSpan UpdateInterval => TimeSpan.FromMilliseconds(500);

    public Task Execute(CommandContext ctx)
    {
        _positions.AddOrUpdate(PositionKey(ctx), 1, (_, position) => position + 1);
        return Task.CompletedTask;
    }

    public AnimationFrameInfo RenderAnimatedFrame(CommandContext ctx, IRenderCanvas canvas, AnimationFrameContext frame) =>
        PixelTile.Render(ctx, canvas, surface => Draw(ctx, surface, TileDrawing.BlinkOn(frame.Elapsed)));

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        PixelTile.Render(ctx, canvas, surface => Draw(ctx, surface, PixelTile.WallClockBlink()));
        return true;
    }

    private void Draw(CommandContext ctx, PixelSurface surface, bool blinkOn)
    {
        TelemetryFrame frame = telemetry.Frame;
        if (!frame.IsAvailable)
        {
            TileDrawing.Unavailable(surface, "ARGUS");
            return;
        }

        IReadOnlyList<ComponentPage> selected = ComponentPages.Parse(Selection(ctx));
        List<ComponentPage> pages = selected.Where(page => ComponentPages.IsAvailable(page, frame)).ToList();
        if (pages.Count == 0)
        {
            TileDrawing.NoData(surface, selected[0].Title);
            return;
        }

        int index = _positions.GetValueOrDefault(PositionKey(ctx)) % pages.Count;
        PageLayout.Draw(surface, pages[index], index + 1, pages.Count, frame, blinkOn);
    }

    /// <summary>The pressed/rendered button; on a host without button keys, every button with the
    /// same page list shares one position.</summary>
    private static string PositionKey(CommandContext ctx) => ButtonKeys.For(ctx, "pages:" + Selection(ctx));

    private static string Selection(CommandContext ctx) =>
        ctx.Parameters is { Length: >= 1 } ? ctx.Parameters[0] : ComponentPages.DefaultSelection;
}
