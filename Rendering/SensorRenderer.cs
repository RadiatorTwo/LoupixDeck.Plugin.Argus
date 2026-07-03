using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus.Rendering;

/// <summary>
/// Draws a dynamic monitoring tile onto a host <see cref="IRenderCanvas"/> (a touch button is
/// 90×90). Pure drawing: it knows nothing about Argus and only lays out the <see cref="SensorReading"/>s
/// it is handed (1–4). The content is split into one vertical cell per reading; each cell shows its
/// header on top, a large value+unit below and a gauge at the bottom, with every font scaled to the
/// cell height — so a single reading fills the tile while four stack compactly and nothing is clipped
/// horizontally. All sizes derive from <see cref="IRenderCanvas.Width"/>/<see cref="IRenderCanvas.Height"/>
/// and <see cref="SensorTheme"/>.
/// </summary>
public static class SensorRenderer
{
    private const string Ellipsis = "…";

    /// <summary>Hard cap on rows — four gauges fit legibly on a 90×90 tile.</summary>
    public const int MaxReadings = 4;

    public static void Render(IRenderCanvas canvas, IReadOnlyList<SensorReading> readings, SensorTheme theme)
    {
        canvas.Clear(theme.Background);

        int px = theme.Inset;
        int py = theme.Inset;
        int pw = canvas.Width - 2 * theme.Inset;
        int ph = canvas.Height - 2 * theme.Inset;

        if (theme.ShowPanel)
        {
            canvas.FillRoundedRectangle(px, py, pw, ph, theme.CornerRadius, theme.Panel);
            if (theme.BorderWidth > 0)
                canvas.DrawRoundedRectangle(px, py, pw, ph, theme.CornerRadius, theme.BorderWidth, theme.Border);
        }

        int cx = px + theme.Padding;
        int cy = py + theme.Padding;
        int cw = pw - 2 * theme.Padding;
        int ch = ph - 2 * theme.Padding;
        if (cw < 1 || ch < 1)
            return;

        int n = Math.Min(readings.Count, MaxReadings);
        if (n == 0)
            return;

        // Reserve bottom clearance so the lowest gauge is not clipped by the device bezel; split the
        // rest into one equal cell per reading (the leftover stays as that bottom clearance).
        int gridH = Math.Max(1, ch - theme.BarBottomGap);
        int cellH = Math.Max(1, gridH / n);

        for (int i = 0; i < n; i++)
            RenderCell(canvas, readings[i], theme, cx, cy + i * cellH, cw, cellH);
    }

    /// <summary>One reading in its cell: header on top, a large value+unit centered below and a gauge
    /// at the bottom. Fonts scale with the cell height; the gauge is dropped when the cell is too
    /// short to keep the value legible.</summary>
    private static void RenderCell(IRenderCanvas canvas, SensorReading r, SensorTheme theme,
        int x, int y, int w, int h)
    {
        bool hasHeader = !string.IsNullOrWhiteSpace(r.Header);
        float headerFont = Math.Clamp(h * 0.30f, 8f, theme.HeaderFontSize);
        int headerH = hasHeader ? (int)Math.Ceiling(headerFont) + 2 : 0;
        if (headerH > h)
            headerH = h;

        int remaining = h - headerH;

        // Keep the gauge only when the cell still has room for a legible value above it.
        const int barGap = 2;
        bool hasBar = theme.ShowBar && r.Fraction.HasValue && remaining >= theme.BarHeight + barGap + 12;
        int barH = hasBar ? theme.BarHeight : 0;
        int valueH = remaining - (hasBar ? barH + barGap : 0);

        if (hasHeader)
        {
            string header = Fit(canvas, r.Header, headerFont, w, bold: false);
            canvas.DrawText(header, x, y, w, headerH, theme.HeaderColor, headerFont,
                TextHAlign.Center, TextVAlign.Middle, outlined: theme.OutlineText, outlineColor: theme.OutlineColor);
        }

        if (valueH > 0)
        {
            float valueCap = Math.Clamp(valueH - 1f, 10f, theme.ValueFontSize);
            DrawValueUnit(canvas, x, y + headerH, w, valueH, r.Value, r.Unit,
                valueCap, theme.UnitFontSize, theme.ValueColor, theme.UnitColor, theme.OutlineText, theme.OutlineColor);
        }

        if (hasBar)
        {
            int barX = x + theme.BarInsetX;
            int barW = Math.Max(1, w - 2 * theme.BarInsetX);
            DrawBar(canvas, barX, y + h - barH, barW, barH, r.Fraction!.Value, theme, r.Accent ?? theme.BarFill);
        }
    }

    /// <summary>Draws a horizontal gauge: a full-width track with an accent fill proportional to
    /// <paramref name="fraction"/> (clamped 0..1).</summary>
    private static void DrawBar(IRenderCanvas canvas, int x, int y, int w, int h, double fraction,
        SensorTheme theme, PluginColor fill)
    {
        int radius = Math.Min(theme.BarRadius, h / 2);
        canvas.FillRoundedRectangle(x, y, w, h, radius, theme.BarTrack);

        int fillW = (int)Math.Round(w * Math.Clamp(fraction, 0.0, 1.0));
        if (fillW > 0)
            canvas.FillRoundedRectangle(x, y, fillW, h, Math.Min(radius, fillW / 2), fill);
    }

    /// <summary>
    /// Draws a large value with a smaller unit beside it, the pair centered horizontally within the
    /// box and vertically middled. The value font is shrunk to fit the available width; the unit sits
    /// slightly above the value's vertical center per the design.
    /// </summary>
    private static void DrawValueUnit(IRenderCanvas canvas, int x, int y, int w, int h,
        string value, string unit, float valueFont, float unitFont, PluginColor valueColor, PluginColor unitColor,
        bool outline, PluginColor outlineColor)
    {
        if (string.IsNullOrEmpty(value))
            return;

        bool hasUnit = !string.IsNullOrEmpty(unit);
        const int gap = 3;
        float unitW = hasUnit ? canvas.MeasureText(unit, unitFont) : 0f;

        // Shrink the value font until value + gap + unit fits the width.
        float fitFont = valueFont;
        float numW = canvas.MeasureText(value, fitFont, bold: true);
        while (fitFont > 10f && numW + (hasUnit ? gap + unitW : 0f) > w)
        {
            fitFont -= 1f;
            numW = canvas.MeasureText(value, fitFont, bold: true);
        }

        float total = numW + (hasUnit ? gap + unitW : 0f);
        int startX = x + (int)Math.Max(0f, (w - total) / 2f);

        canvas.DrawText(value, startX, y, (int)Math.Ceiling(numW) + 1, h, valueColor, fitFont,
            TextHAlign.Left, TextVAlign.Middle, bold: true, outlined: outline, outlineColor: outlineColor);

        if (hasUnit)
        {
            // Nudge the unit up so it reads as a superscript-ish unit next to the number.
            int unitY = y - (int)(fitFont * 0.12f);
            canvas.DrawText(unit, startX + (int)Math.Ceiling(numW) + gap, unitY, (int)Math.Ceiling(unitW) + 1, h,
                unitColor, unitFont, TextHAlign.Left, TextVAlign.Middle, outlined: outline, outlineColor: outlineColor);
        }
    }

    /// <summary>Truncates text with a trailing ellipsis so it fits <paramref name="maxWidth"/>.</summary>
    private static string Fit(IRenderCanvas canvas, string text, float fontSize, float maxWidth, bool bold)
    {
        if (string.IsNullOrEmpty(text) || canvas.MeasureText(text, fontSize, bold) <= maxWidth)
            return text;

        string trimmed = text;
        while (trimmed.Length > 1 && canvas.MeasureText(trimmed + Ellipsis, fontSize, bold) > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + Ellipsis;
    }
}
