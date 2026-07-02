using LoupixDeck.Plugin.Argus.Tiles;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Display command that renders a single Argus Monitor reading as a compact monitoring tile onto
/// a touch button (90×90) via the SDK image-rendering API. The command name and the "Sensor"
/// parameter are unchanged from the former text command, so existing button assignments keep
/// working — a legacy parameter without a variant tag renders as a single-value tile.
/// </summary>
internal sealed class ArgusSensorCommand(ArgusMonitorService argus) : IDisplayImageCommand
{
    public CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = "Argus.Sensor",
        DisplayName = "Argus Sensor",
        Group = "Argus Monitor",
        ParameterTemplate = "({Sensor})",
        Parameters = [new CommandParameter("Sensor", typeof(string))],
        // Surfaced per sensor through the dynamic menu.
        HiddenFromMenu = true
    };

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public TimeSpan UpdateInterval => TimeSpan.FromSeconds(2);

    public bool RenderImage(CommandContext ctx, IRenderCanvas canvas)
    {
        string[] parameters = ctx.Parameters;
        string? sensorRef = parameters is { Length: >= 1 } ? parameters[0] : null;

        TileSpec spec = ArgusTileSpecBuilder.Build(sensorRef, argus.Sensors, argus.IsAvailable);
        TileRenderer.Render(canvas, spec, TileTheme.Default);
        return true;
    }

    public Task Execute(CommandContext ctx) => Task.CompletedTask;
}
