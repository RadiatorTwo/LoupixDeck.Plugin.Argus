using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Entry point of the Argus Monitor plugin (Windows only). Reads Argus Monitor's
/// shared-memory data API and exposes one display command plus a live sensor menu.
/// </summary>
public sealed class ArgusPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    private readonly ArgusMonitorService _service = new();
    private List<IPluginCommand> _commands = [];

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "argus",
        Name = "Argus Monitor",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 14, 0),
        Author = "RadiatorTwo",
        Description = "Display Argus Monitor sensor readings on touch buttons via its shared-memory data API."
    };

    public override void Initialize(IPluginHost host)
    {
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

        var groupChildren = new List<MenuNode>();
        var sensors = _service.Sensors;

        if (!_service.IsAvailable || sensors.Count == 0)
        {
            groupChildren.Add(new MenuNode { Name = "Argus Monitor not available" });
        }
        else
        {
            foreach (var typeGroup in sensors
                         .Where(s => s.Type != ArgusSensorType.Invalid)
                         .GroupBy(s => s.Type)
                         .OrderBy(g => g.Key.ToString()))
            {
                var readings = new List<MenuNode>();
                foreach (var sensor in typeGroup.OrderBy(s => s.SensorIndex))
                {
                    var label = string.IsNullOrWhiteSpace(sensor.Label)
                        ? $"#{sensor.SensorIndex}"
                        : sensor.Label;

                    readings.Add(new MenuNode
                    {
                        Name = label,
                        CommandName = "Argus.Sensor",
                        Parameters = new Dictionary<string, string>
                        {
                            { "Sensor", $"{sensor.Type}:{sensor.SensorIndex}" }
                        }
                    });
                }

                groupChildren.Add(new MenuNode { Name = typeGroup.Key.ToString(), Children = readings });
            }
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = "Argus Monitor", Children = groupChildren }];
        return Task.FromResult(result);
    }

    // ───────── IPluginSettingsPage — status only ─────────

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } = [];

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
    }
}
