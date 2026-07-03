using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus.Rendering;

/// <summary>
/// One sensor reading to draw as a row (or, when it is the only one, as a full tile). Decoupled
/// from Argus so <see cref="SensorRenderer"/> stays a pure, reusable drawing component. All numbers
/// arrive pre-formatted as strings — unit scaling (e.g. MB→G) and decimal formatting happen in the
/// caller (see <see cref="ArgusReadingBuilder"/>).
/// </summary>
/// <param name="Header">Short row label / tile title (e.g. "CPU Load", "T1").</param>
/// <param name="Value">Main value string (e.g. "36", "11.3").</param>
/// <param name="Unit">Unit drawn small next to the value (e.g. "°C", "%", "G"). May be empty.</param>
/// <param name="Fraction">Gauge fill 0..1 for the value, or null when the reading has no meaningful
/// scale (no bar is drawn).</param>
/// <param name="Accent">Accent color for the gauge fill, or null for the theme's neutral default.</param>
public sealed record SensorReading(
    string Header,
    string Value,
    string Unit,
    double? Fraction = null,
    PluginColor? Accent = null);
