using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus.Rendering;

/// <summary>
/// Draws a dynamic monitoring tile onto a host <see cref="IRenderCanvas"/> (a touch button is
/// 90×90). Pure drawing: it knows nothing about Argus and only lays out the <see cref="SensorReading"/>s
/// it is handed (1–4). A single reading fills the tile as a big value + header + gauge; two to four
/// readings stack as horizontal rows whose font grows as the count shrinks, so the space is always
/// used sensibly. All sizes derive from <see cref="IRenderCanvas.Width"/>/<see cref="IRenderCanvas.Height"/>
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

        if (n == 1)
            RenderSingle(canvas, readings[0], theme, cx, cy, cw, ch);
        else
            RenderRows(canvas, readings, n, theme, cx, cy, cw, ch);
    }

    /// <summary>One reading: centered header band, a large value+unit filling the middle and a
    /// full-width gauge at the bottom.</summary>
    private static void RenderSingle(IRenderCanvas canvas, SensorReading r, SensorTheme theme,
        int x, int y, int w, int h)
    {
        int headerH = 0;
        if (!string.IsNullOrWhiteSpace(r.Header))
        {
            headerH = (int)theme.HeaderFontSize + 4;
            string header = Fit(canvas, r.Header, theme.HeaderFontSize, w, bold: false);
            canvas.DrawText(header, x, y, w, headerH, theme.HeaderColor, theme.HeaderFontSize,
                TextHAlign.Center, TextVAlign.Middle, outlined: theme.OutlineText, outlineColor: theme.OutlineColor);
        }

        const int gap = 3;
        bool hasBar = theme.ShowBar && r.Fraction.HasValue;
        int barH = hasBar ? theme.BarHeight : 0;

        int bodyY = y + headerH;
        int bodyH = h - headerH - (hasBar ? barH + gap : 0);
        if (bodyH < 1)
            return;

        DrawValueUnit(canvas, x, bodyY, w, bodyH, r.Value, r.Unit,
            theme.ValueFontSize, theme.UnitFontSize, theme.ValueColor, theme.UnitColor, theme.OutlineText, theme.OutlineColor);

        if (hasBar)
            DrawBar(canvas, x, y + h - barH, w, barH, r.Fraction!.Value, theme, r.Accent ?? theme.BarFill);
    }

    /// <summary>Two to four readings as stacked rows: header label left, value+unit right, a thin
    /// gauge under each row when it is tall enough. The row font scales with the row height so fewer
    /// rows read bolder.</summary>
    private static void RenderRows(IRenderCanvas canvas, IReadOnlyList<SensorReading> readings, int n,
        SensorTheme theme, int x, int y, int w, int h)
    {
        int rowH = h / n;

        // Scale the font with the available row height so 2 rows are noticeably larger than 4.
        float rowFont = Math.Clamp(rowH * 0.5f, theme.CaptionFontSize, theme.ValueFontSize);

        bool anyFraction = false;
        for (int i = 0; i < n; i++)
        {
            if (readings[i].Fraction.HasValue)
            {
                anyFraction = true;
                break;
            }
        }

        bool showRowBar = theme.ShowBar && anyFraction && rowH >= 16;
        int barH = showRowBar ? Math.Max(3, theme.BarHeight - 3) : 0;
        const int rowGap = 1;
        int textH = rowH - barH - (showRowBar ? rowGap : 0);
        if (rowFont > textH - 2)
            rowFont = Math.Max(8f, textH - 2);

        // Left label column: widest header, capped so values keep room.
        float labelW = 0f;
        for (int i = 0; i < n; i++)
            labelW = Math.Max(labelW, canvas.MeasureText(readings[i].Header, rowFont));
        int labelColW = Math.Min((int)labelW + 6, (int)(w * 0.5f));

        for (int i = 0; i < n; i++)
        {
            SensorReading r = readings[i];
            int rowY = y + i * rowH;

            canvas.DrawText(Fit(canvas, r.Header, rowFont, labelColW, false),
                x, rowY, labelColW, textH, theme.CaptionColor, rowFont, TextHAlign.Left, TextVAlign.Middle,
                outlined: theme.OutlineText, outlineColor: theme.OutlineColor);

            string valueText = string.IsNullOrEmpty(r.Unit) ? r.Value : $"{r.Value} {r.Unit}";
            int valueX = x + labelColW;
            int valueW = w - labelColW;
            canvas.DrawText(Fit(canvas, valueText, rowFont, valueW, false),
                valueX, rowY, valueW, textH, theme.RowColor, rowFont, TextHAlign.Right, TextVAlign.Middle,
                outlined: theme.OutlineText, outlineColor: theme.OutlineColor);

            if (showRowBar && r.Fraction.HasValue)
                DrawBar(canvas, x, rowY + textH + rowGap, w, barH, r.Fraction.Value, theme, r.Accent ?? theme.BarFill);
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
