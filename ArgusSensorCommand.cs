using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus;

/// <summary>
/// Display command that renders a single Argus Monitor sensor reading onto a
/// touch button. The command name is kept identical to the former built-in.
/// </summary>
internal sealed class ArgusSensorCommand(ArgusMonitorService argus) : IDisplayCommand
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

    public string GetText(CommandContext ctx)
    {
        if (!argus.IsAvailable)
            return "N/A";

        var parameters = ctx.Parameters;
        if (parameters is not { Length: >= 1 } || string.IsNullOrWhiteSpace(parameters[0]))
            return "?";

        if (!TryParseSensorRef(parameters[0], out var type, out var sensorIndex))
            return "?";

        var sensor = argus.Sensors.FirstOrDefault(s => s.Type == type && s.SensorIndex == sensorIndex);
        if (sensor is null)
            return "?";

        var unit = string.IsNullOrEmpty(sensor.Unit) ? string.Empty : " " + sensor.Unit;
        return $"{sensor.Value:F1}{unit}";
    }

    public Task Execute(CommandContext ctx) => Task.CompletedTask;

    // Reference format: "SensorType:SensorIndex".
    private static bool TryParseSensorRef(string raw, out ArgusSensorType type, out uint sensorIndex)
    {
        type = ArgusSensorType.Invalid;
        sensorIndex = 0;

        var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
            return false;

        if (!Enum.TryParse(parts[0], ignoreCase: true, out type) || type == ArgusSensorType.Invalid)
            return false;

        if (parts.Length < 2 || string.IsNullOrEmpty(parts[1]))
            return true;

        return uint.TryParse(parts[1], out sensorIndex);
    }
}
