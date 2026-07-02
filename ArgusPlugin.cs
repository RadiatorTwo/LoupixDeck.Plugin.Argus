using LoupixDeck.Plugin.Argus.Tiles;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Entry point of the Argus Monitor plugin (Windows only). Reads Argus Monitor's
/// shared-memory data API and exposes one image display command plus a live sensor menu that
/// builds ready-made monitoring tiles (single value, multi-sensor, memory, double value).
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
        Description = "Display Argus Monitor sensor readings as compact monitoring tiles on touch buttons."
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

        List<MenuNode> groupChildren = [];
        IReadOnlyList<ArgusSensor> sensors = _service.Sensors;

        if (!_service.IsAvailable || sensors.Count == 0)
        {
            groupChildren.Add(new MenuNode { Name = "Argus Monitor not available" });
        }
        else
        {
            // Combined / multi-sensor tiles first, where the data supports them.
            List<MenuNode> tileNodes = BuildTileNodes(sensors);
            if (tileNodes.Count > 0)
                groupChildren.Add(new MenuNode { Name = "Tiles", Children = tileNodes });

            // Per-type single-value readings (one selectable tile each).
            foreach (IGrouping<ArgusSensorType, ArgusSensor> typeGroup in sensors
                         .Where(s => s.Type != ArgusSensorType.Invalid)
                         .GroupBy(s => s.Type)
                         .OrderBy(g => ArgusTileSpecBuilder.HeaderFor(g.Key), StringComparer.OrdinalIgnoreCase))
            {
                List<MenuNode> readings = [];
                foreach (ArgusSensor sensor in typeGroup.OrderBy(s => s.SensorIndex))
                {
                    string label = string.IsNullOrWhiteSpace(sensor.Label)
                        ? $"#{sensor.SensorIndex}"
                        : sensor.Label;

                    readings.Add(SensorNode(label, $"single:{sensor.Type}:{sensor.SensorIndex}"));
                }

                groupChildren.Add(new MenuNode
                {
                    Name = ArgusTileSpecBuilder.HeaderFor(typeGroup.Key),
                    Children = readings
                });
            }
        }

        IReadOnlyList<MenuNode> result = [new MenuNode { Name = "Argus Monitor", Children = groupChildren }];
        return Task.FromResult(result);
    }

    /// <summary>Builds the combined tiles (multi-sensor / memory / double) that the current sensor
    /// snapshot supports. Each entry pre-bakes the variant + sensor references into the command
    /// parameter so the renderer knows which layout to draw.</summary>
    private static List<MenuNode> BuildTileNodes(IReadOnlyList<ArgusSensor> sensors)
    {
        List<MenuNode> nodes = [];

        // Multi-sensor tiles: temperature groups with more than one reading (disk temps, cores).
        foreach (IGrouping<ArgusSensorType, ArgusSensor> group in sensors
                     .Where(s => IsTemperature(s.Type))
                     .GroupBy(s => s.Type)
                     .OrderBy(g => ArgusTileSpecBuilder.HeaderFor(g.Key), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() < 2)
                continue;

            string header = ArgusTileSpecBuilder.HeaderFor(group.Key);
            nodes.Add(SensorNode($"{header} (all)", $"multi:{group.Key}:*"));
        }

        // GPU memory: pair absolute MB with percent (same subsystem).
        ArgusSensor? gpuMemMb = sensors.FirstOrDefault(s => s.Type == ArgusSensorType.GpuMemoryUsedMb);
        ArgusSensor? gpuMemPct = sensors.FirstOrDefault(s => s.Type == ArgusSensorType.GpuMemoryUsedPercent);
        if (gpuMemMb is not null && gpuMemPct is not null)
        {
            nodes.Add(SensorNode("GPU Memory (used + %)",
                $"memory:GpuMemoryUsedMb:{gpuMemMb.SensorIndex}|GpuMemoryUsedPercent:{gpuMemPct.SensorIndex}"));
        }

        // CPU frequency average vs. maximum as a double-value tile.
        bool hasFreqAvg = sensors.Any(s => s.Type == ArgusSensorType.CpuFrequencyAvg);
        bool hasFreqMax = sensors.Any(s => s.Type == ArgusSensorType.CpuFrequencyMax);
        if (hasFreqAvg && hasFreqMax)
        {
            nodes.Add(SensorNode("CPU Freq (avg / max)",
                "double:CpuFrequencyAvg:0|CpuFrequencyMax:0"));
        }

        return nodes;
    }

    private static MenuNode SensorNode(string name, string sensorParameter) => new()
    {
        Name = name,
        CommandName = "Argus.Sensor",
        Parameters = new Dictionary<string, string> { { "Sensor", sensorParameter } }
    };

    private static bool IsTemperature(ArgusSensorType type) => type is
        ArgusSensorType.Temperature or ArgusSensorType.SyntheticTemperature or
        ArgusSensorType.CpuTemperature or ArgusSensorType.CpuTemperatureAdditional or
        ArgusSensorType.GpuTemperature or ArgusSensorType.DiskTemperature;

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
