using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus.Tiles;

/// <summary>
/// All colors, font sizes, spacings and border settings of a monitoring tile, gathered so the
/// look can be themed later without touching <see cref="TileRenderer"/>. Consumers should start
/// from <see cref="Default"/> and override only what they need — the parameterless struct default
/// is intentionally not usable (all-zero colors). Defaults are deliberately neutral dark greys;
/// no bright status colors.
/// </summary>
public readonly record struct TileTheme
{
    // ── Surface ───────────────────────────────────────────────────────────────
    /// <summary>Fill behind everything (the whole canvas is cleared with this).</summary>
    public PluginColor Background { get; init; }

    /// <summary>Fill of the inner tile panel. Draw the panel only when <see cref="ShowPanel"/>.</summary>
    public PluginColor Panel { get; init; }

    /// <summary>Panel border color.</summary>
    public PluginColor Border { get; init; }

    /// <summary>Subtle horizontal guide line color (SingleValue only, when <see cref="ShowGuideLine"/>).</summary>
    public PluginColor GuideLine { get; init; }

    public bool ShowPanel { get; init; }
    public bool ShowGuideLine { get; init; }

    /// <summary>Outer margin from the canvas edge to the panel (device bezel clearance).</summary>
    public int Inset { get; init; }

    /// <summary>Inner padding from the panel edge to content.</summary>
    public int Padding { get; init; }

    /// <summary>Panel corner radius / border stroke width.</summary>
    public int CornerRadius { get; init; }
    public int BorderWidth { get; init; }

    // ── Text ──────────────────────────────────────────────────────────────────
    public PluginColor HeaderColor { get; init; }
    public float HeaderFontSize { get; init; }

    public PluginColor ValueColor { get; init; }
    /// <summary>Upper bound for the main value size; the renderer shrinks it to fit the width.</summary>
    public float ValueFontSize { get; init; }

    public PluginColor UnitColor { get; init; }
    public float UnitFontSize { get; init; }

    public PluginColor SecondaryColor { get; init; }
    public float SecondaryFontSize { get; init; }

    public PluginColor CaptionColor { get; init; }
    public float CaptionFontSize { get; init; }

    public PluginColor RowColor { get; init; }
    public float RowFontSize { get; init; }

    public PluginColor LabelColor { get; init; }
    public float LabelFontSize { get; init; }

    /// <summary>Neutral dark default theme.</summary>
    public static TileTheme Default { get; } = new()
    {
        Background = new PluginColor(0x0E, 0x0E, 0x0E),
        Panel = new PluginColor(0x1A, 0x1A, 0x1A),
        Border = new PluginColor(0x30, 0x30, 0x30),
        GuideLine = new PluginColor(0x2A, 0x2A, 0x2A),
        ShowPanel = true,
        ShowGuideLine = true,
        Inset = 2,
        Padding = 5,
        CornerRadius = 8,
        BorderWidth = 1,

        HeaderColor = new PluginColor(0x8C, 0x8C, 0x8C),
        HeaderFontSize = 11f,

        ValueColor = new PluginColor(0xEC, 0xEC, 0xEC),
        ValueFontSize = 30f,

        UnitColor = new PluginColor(0x9C, 0x9C, 0x9C),
        UnitFontSize = 13f,

        SecondaryColor = new PluginColor(0xBC, 0xBC, 0xBC),
        SecondaryFontSize = 16f,

        CaptionColor = new PluginColor(0x7C, 0x7C, 0x7C),
        CaptionFontSize = 9f,

        RowColor = new PluginColor(0xD0, 0xD0, 0xD0),
        RowFontSize = 13f,

        LabelColor = new PluginColor(0xC8, 0xC8, 0xC8),
        LabelFontSize = 12f
    };
}
