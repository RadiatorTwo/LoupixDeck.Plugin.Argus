using LoupixDeck.Plugin.Argus.Rendering;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Entry point of the Argus Monitor plugin (Windows only). Reads Argus Monitor's shared-memory data
/// API and exposes one image display command plus a live sensor menu. Each menu entry assigns a
/// single sensor; a multi-sensor button is composed by chaining several commands in the button's
/// sequence (the display command lays them out as rows, up to four).
/// </summary>
public sealed class ArgusPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    /// <summary>Settings key: when true, buttons are drawn without an opaque background so the page
    /// wallpaper shows through. Read by the display command at render time.</summary>
    public const string TransparentBackgroundKey = "background.transparent";

    private readonly ArgusMonitorService _service = new();
    private List<IPluginCommand> _commands = [];
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "argus",
        Name = "Argus Monitor",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 15, 0),
        Author = "RadiatorTwo",
        Description = "Display Argus Monitor sensor readings on touch buttons; chain several to compose a multi-sensor tile."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _commands = [new ArgusSensorCommand(_service)];
        _service.Start();
    }

    public override void Shutdown() => _service.Stop();

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    // ───────── IMenuContributor — dynamic sensor tree ─────────

    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        // Sensor readings are touch-button display content only.
        if (target != ButtonTargets.TouchButton)
            return Task.FromResult<IReadOnlyList<MenuNode>>([]);

        List<MenuNode> groupChildren = [];
        IReadOnlyList<ArgusSensor> sensors = _service.Sensors;

        if (!_service.IsAvailable || sensors.Count == 0)
        {
            groupChildren.Add(new MenuNode { Name = "Argus Monitor not available" });
        }
        else
        {
            // Per-type single-sensor readings (one selectable command each). Combine several on a
            // button via its command sequence to get a multi-row tile.
            foreach (IGrouping<ArgusSensorType, ArgusSensor> typeGroup in sensors
                         .Where(s => s.Type != ArgusSensorType.Invalid)
                         .GroupBy(s => s.Type)
                         .OrderBy(g => ArgusReadingBuilder.HeaderFor(g.Key), StringComparer.OrdinalIgnoreCase))
            {
                List<MenuNode> readings = [];
                foreach (ArgusSensor sensor in typeGroup.OrderBy(s => s.SensorIndex))
                {
                    string label = string.IsNullOrWhiteSpace(sensor.Label)
                        ? $"#{sensor.SensorIndex}"
                        : sensor.Label;

                    readings.Add(SensorNode(label, $"{sensor.Type}:{sensor.SensorIndex}"));
                }

                groupChildren.Add(new MenuNode
                {
                    Name = ArgusReadingBuilder.HeaderFor(typeGroup.Key),
                    Children = readings
                });
            }
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = "Argus Monitor", Children = groupChildren }];
        return Task.FromResult(result);
    }

    private static MenuNode SensorNode(string name, string sensorParameter) => new()
    {
        Name = name,
        CommandName = "Argus.Sensor",
        Parameters = new Dictionary<string, string> { { "Sensor", sensorParameter } }
    };

    // ───────── IPluginSettingsPage — status only ─────────

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new PluginSettingDescriptor
        {
            Key = TransparentBackgroundKey,
            Label = "Transparent background",
            Kind = PluginSettingKind.Toggle,
            DefaultValue = false,
            Description = "Draw buttons without an opaque background so the page wallpaper shows through. " +
                          "Text is outlined for legibility."
        }
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions => _settingsActions ??=
    [
        new PluginSettingAction
        {
            Label = "Show Status",
            Invoke = () => Task.FromResult(_service.IsAvailable
                ? $"Reading — {_service.Sensors.Count} sensor(s)."
                : "Not running — is Argus Monitor open?")
        }
    ];

    private IReadOnlyList<PluginSettingAction>? _settingsActions;

    public void OnSettingsSaved()
    {
        // Repaint bound touch buttons immediately so a transparency toggle is visible at once
        // (otherwise it would only apply on the command's next 2s poll).
        _host?.RequestButtonRefresh("Argus.Sensor");
    }
}
