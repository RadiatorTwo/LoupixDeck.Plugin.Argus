using LoupixDeck.Plugin.Argus.Rendering;
using LoupixDeck.Plugin.Argus.Rendering.Tiles;
using LoupixDeck.Plugin.Argus.Telemetry;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Entry point of the Argus Monitor plugin (Windows only). Reads Argus Monitor's shared-memory data
/// API, samples it once a second into histories and alert states, and exposes two pixel-tile
/// display commands through a live menu: <c>Argus.Sensor</c> (one sensor per command; chain several
/// for a multi-row tile) and <c>Argus.Pages</c> (component pages, a key press shows the next one).
/// </summary>
public sealed class ArgusPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    /// <summary>Settings key: when true, buttons are drawn without an opaque background so the page
    /// wallpaper shows through. Read by the display command at render time.</summary>
    public const string TransparentBackgroundKey = "background.transparent";

    /// <summary>Settings key: the CPU's maximum junction temperature in °C. CPU warn/critical
    /// limits are TjMax − 15 / TjMax − 5, since Argus does not report TjMax itself.</summary>
    public const string CpuTjMaxKey = "thresholds.cpuTjMax";

    private const long DefaultTjMax = 100;

    private readonly ArgusMonitorService _service = new();
    private TelemetrySampler? _telemetry;
    private List<IPluginCommand> _commands = [];
    private IPluginHost? _host;

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "argus",
        Name = "Argus Monitor",
        Version = new Version(1, 0, 0),
        SdkVersion = new Version(1, 26, 0),
        Author = "RadiatorTwo",
        Description = "Display Argus Monitor sensor readings on touch buttons; chain several to compose a multi-sensor tile."
    };

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        _telemetry = new TelemetrySampler(_service, ReadTjMax);
        _commands = [new ArgusSensorCommand(_telemetry), new ArgusPagesCommand(_telemetry)];
        _service.Start();
        _telemetry.Start();
    }

    public override void Shutdown()
    {
        _telemetry?.Stop();
        _service.Stop();
    }

    private double ReadTjMax()
    {
        long tjMax = _host?.Settings.Get(CpuTjMaxKey, DefaultTjMax) ?? DefaultTjMax;
        return Math.Clamp(tjMax, 60, 125);
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = "Argus Monitor",
            Description = "System sensors and monitoring",
            Icon = "\U000F0379",
            Section = CommandGroupSection.Plugins
        }
    ];

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
            groupChildren.Add(new MenuNode { Name = "Pages", Children = PageNodes() });

            // Per-type single-sensor readings (one selectable command each). Combine several on a
            // button via its command sequence to get a multi-row tile.
            foreach (IGrouping<ArgusSensorType, ArgusSensor> typeGroup in sensors
                         .Where(s => s.Type != ArgusSensorType.Invalid)
                         .GroupBy(s => s.Type)
                         .OrderBy(g => ArgusReadingBuilder.HeaderFor(g.Key), StringComparer.OrdinalIgnoreCase))
            {
                // The parameter uses the sensor's ordinal position within its type (Argus report
                // order), because per-instance sensors (e.g. CPU cores) do not carry a distinct
                // SensorIndex — keep this order in lock-step with ArgusReadingBuilder's lookup.
                List<MenuNode> readings = [];
                int ordinal = 0;
                foreach (ArgusSensor sensor in typeGroup)
                {
                    string label = string.IsNullOrWhiteSpace(sensor.Label)
                        ? $"#{ordinal}"
                        : sensor.Label;

                    readings.Add(SensorNode(label, $"{sensor.Type}:{ordinal}"));
                    ordinal++;
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

    /// <summary>The paging tile (every page, press for the next) and one fixed tile per page.</summary>
    private static List<MenuNode> PageNodes() =>
    [
        PagesNode("All pages (press to cycle)", ComponentPages.DefaultSelection),
        PagesNode("CPU page", ComponentPages.Cpu.Id),
        PagesNode("GPU page", ComponentPages.Gpu.Id),
        PagesNode("RAM page", ComponentPages.Ram.Id),
        PagesNode("Network page", ComponentPages.Net.Id),
        PagesNode("Disk page", ComponentPages.Disk.Id),
        PagesNode("CPU summary", ComponentPages.Summary.Id)
    ];

    private static MenuNode PagesNode(string name, string pages) => new()
    {
        Name = name,
        CommandName = ArgusPagesCommand.CommandName,
        Parameters = new Dictionary<string, string> { { "Pages", pages } }
    };

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
                          "Text gets a 1-pixel shadow for legibility."
        },
        new PluginSettingDescriptor
        {
            Key = CpuTjMaxKey,
            Label = "CPU TjMax (°C)",
            Kind = PluginSettingKind.Number,
            DefaultValue = DefaultTjMax,
            Description = "Maximum junction temperature of your CPU, from the vendor's spec sheet " +
                          "(typically 95 for AMD Ryzen, 100–105 for Intel). CPU temperature turns amber " +
                          "at TjMax − 15 and red at TjMax − 5."
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
        // Tiles redraw several times a second and pick up the new settings on their own; this
        // only covers a host that drives them through the slower poll path.
        _host?.RequestButtonRefresh("Argus.Sensor");
        _host?.RequestButtonRefresh(ArgusPagesCommand.CommandName);
    }
}
