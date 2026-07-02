namespace LoupixDeck.Plugin.Argus.Tiles;

/// <summary>
/// One aligned row of a <see cref="TileVariant.MultiSensor"/> tile: a short label (e.g. "T1"),
/// the formatted value and its unit.
/// </summary>
public sealed record TileRow(string Label, string Value, string Unit);

/// <summary>
/// Data for a single monitoring tile, decoupled from Argus so <see cref="TileRenderer"/> stays a
/// pure, reusable drawing component. All numbers arrive pre-formatted as strings — unit scaling
/// (e.g. MB→G) and decimal formatting happen in the caller (see <c>ArgusTileSpecBuilder</c>).
/// </summary>
public sealed class TileSpec
{
    /// <summary>Which layout to draw.</summary>
    public required TileVariant Variant { get; init; }

    /// <summary>Small title shown at the top of the tile (e.g. "CPU Load").</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>Main value string (e.g. "36", "11.3"). The most prominent element.</summary>
    public string PrimaryValue { get; init; } = string.Empty;

    /// <summary>Unit drawn small next to <see cref="PrimaryValue"/> (e.g. "°C", "%", "G").</summary>
    public string PrimaryUnit { get; init; } = string.Empty;

    /// <summary>Second value (DoubleValue / Memory). Null when unused.</summary>
    public string? SecondaryValue { get; init; }

    /// <summary>Unit for <see cref="SecondaryValue"/>. Null when unused.</summary>
    public string? SecondaryUnit { get; init; }

    /// <summary>Short caption under/over the primary value (DoubleValue, e.g. "T1"). Optional.</summary>
    public string? PrimaryCaption { get; init; }

    /// <summary>Short caption for the secondary value (DoubleValue, e.g. "T2"). Optional.</summary>
    public string? SecondaryCaption { get; init; }

    /// <summary>Rows for <see cref="TileVariant.MultiSensor"/>. Null/empty otherwise.</summary>
    public IReadOnlyList<TileRow>? Rows { get; init; }

    /// <summary>Optional descriptive label drawn as a band at the bottom of the tile. Ellipsized
    /// when too wide.</summary>
    public string? Label { get; init; }
}
