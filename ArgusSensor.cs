namespace LoupixDeck.Plugin.Argus;

public sealed record ArgusSensor(
    ArgusSensorType Type,
    string Label,
    string Unit,
    double Value,
    uint DataIndex,
    uint SensorIndex);
