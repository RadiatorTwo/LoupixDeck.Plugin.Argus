namespace LoupixDeck.Plugin.Argus.Tiles;

/// <summary>
/// Layout variant a <see cref="TileSpec"/> is rendered with. The variant is chosen when a
/// menu entry is built (encoded into the command parameter) and switched on by
/// <see cref="TileRenderer"/> at draw time.
/// </summary>
public enum TileVariant
{
    /// <summary>Header + one large centered value with its unit.</summary>
    SingleValue,

    /// <summary>Header + two values side by side, each with an optional short caption.</summary>
    DoubleValue,

    /// <summary>Header + an absolute value (large) with a percentage underneath.</summary>
    Memory,

    /// <summary>Header + several aligned rows (short label, value, unit).</summary>
    MultiSensor
}
