using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Argus.Tiles;

/// <summary>
/// Draws a compact hardware-monitoring tile onto a host <see cref="IRenderCanvas"/> (a touch
/// button is 90×90). Pure drawing: it knows nothing about Argus and switches only on
/// <see cref="TileSpec.Variant"/>. All sizes derive from <see cref="IRenderCanvas.Width"/>/
/// <see cref="IRenderCanvas.Height"/> and <see cref="TileTheme"/>, so it adapts if the region
/// size ever changes and can be fully themed. Modelled on the Audio plugin's AudioStripRenderer.
/// </summary>
public static class TileRenderer
{
    private const string Ellipsis = "…";

    public static void Render(IRenderCanvas canvas, TileSpec spec, TileTheme theme)
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

        // Header band (top).
        int headerH = (int)theme.HeaderFontSize + 4;
        if (!string.IsNullOrWhiteSpace(spec.Header))
        {
            string header = Fit(canvas, spec.Header, theme.HeaderFontSize, cw, bold: false);
            canvas.DrawText(header, cx, cy, cw, headerH, theme.HeaderColor, theme.HeaderFontSize,
                TextHAlign.Center, TextVAlign.Middle);
        }

        // Optional label band (bottom).
        bool hasLabel = !string.IsNullOrWhiteSpace(spec.Label);
        int labelH = hasLabel ? (int)theme.LabelFontSize + 4 : 0;
        if (hasLabel)
        {
            string label = Fit(canvas, spec.Label!, theme.LabelFontSize, cw, bold: false);
            canvas.DrawText(label, cx, cy + ch - labelH, cw, labelH, theme.LabelColor, theme.LabelFontSize,
                TextHAlign.Center, TextVAlign.Middle);
        }

        // Body (between header and label).
        const int gap = 2;
        int bx = cx;
        int by = cy + headerH + gap;
        int bw = cw;
        int bh = ch - headerH - gap - (hasLabel ? labelH + gap : 0);
        if (bh < 1)
            return;

        switch (spec.Variant)
        {
            case TileVariant.DoubleValue:
                RenderDouble(canvas, spec, theme, bx, by, bw, bh);
                break;
            case TileVariant.Memory:
                RenderMemory(canvas, spec, theme, bx, by, bw, bh);
                break;
            case TileVariant.MultiSensor:
                RenderMulti(canvas, spec, theme, bx, by, bw, bh);
                break;
            default:
                RenderSingle(canvas, spec, theme, bx, by, bw, bh);
                break;
        }
    }

    private static void RenderSingle(IRenderCanvas canvas, TileSpec spec, TileTheme theme,
        int x, int y, int w, int h)
    {
        // Subtle background guide line behind the value.
        if (theme.ShowGuideLine)
        {
            int lineY = y + h / 2;
            int lineInset = w / 6;
            canvas.DrawLine(x + lineInset, lineY, x + w - lineInset, lineY, 1, theme.GuideLine);
        }

        DrawValueUnit(canvas, x, y, w, h, spec.PrimaryValue, spec.PrimaryUnit,
            theme.ValueFontSize, theme.UnitFontSize, theme.ValueColor, theme.UnitColor);
    }

    private static void RenderDouble(IRenderCanvas canvas, TileSpec spec, TileTheme theme,
        int x, int y, int w, int h)
    {
        // Two stacked rows (caption left, value + unit centered in the rest). Stacking rather than
        // side-by-side keeps wide units (e.g. "MHz") readable on a narrow 90px tile.
        int rowH = h / 2;
        DrawDoubleRow(canvas, x, y, w, rowH, spec.PrimaryCaption, spec.PrimaryValue, spec.PrimaryUnit, theme);
        DrawDoubleRow(canvas, x, y + rowH, w, h - rowH, spec.SecondaryCaption,
            spec.SecondaryValue ?? string.Empty, spec.SecondaryUnit ?? string.Empty, theme);

        // Thin divider between the two rows.
        if (theme.ShowGuideLine)
            canvas.DrawLine(x + w / 6, y + rowH, x + w - w / 6, y + rowH, 1, theme.GuideLine);
    }

    private static void DrawDoubleRow(IRenderCanvas canvas, int x, int y, int w, int h,
        string? caption, string value, string unit, TileTheme theme)
    {
        int capW = 0;
        if (!string.IsNullOrWhiteSpace(caption))
        {
            capW = Math.Min((int)canvas.MeasureText(caption!, theme.CaptionFontSize) + 4, (int)(w * 0.4f));
            canvas.DrawText(caption!, x, y, capW, h, theme.CaptionColor, theme.CaptionFontSize,
                TextHAlign.Left, TextVAlign.Middle);
        }

        float valueFont = Math.Min(theme.SecondaryFontSize * 1.25f, h - 2);
        DrawValueUnit(canvas, x + capW, y, w - capW, h, value, unit,
            valueFont, theme.UnitFontSize * 0.8f, theme.ValueColor, theme.UnitColor);
    }

    private static void RenderMemory(IRenderCanvas canvas, TileSpec spec, TileTheme theme,
        int x, int y, int w, int h)
    {
        int topH = (int)(h * 0.58f);
        DrawValueUnit(canvas, x, y, w, topH, spec.PrimaryValue, spec.PrimaryUnit,
            theme.ValueFontSize, theme.UnitFontSize, theme.ValueColor, theme.UnitColor);

        if (!string.IsNullOrWhiteSpace(spec.SecondaryValue))
        {
            DrawValueUnit(canvas, x, y + topH, w, h - topH, spec.SecondaryValue!, spec.SecondaryUnit ?? string.Empty,
                theme.SecondaryFontSize, theme.UnitFontSize * 0.85f, theme.SecondaryColor, theme.UnitColor);
        }
    }

    private static void RenderMulti(IRenderCanvas canvas, TileSpec spec, TileTheme theme,
        int x, int y, int w, int h)
    {
        IReadOnlyList<TileRow> rows = spec.Rows ?? [];
        int n = Math.Clamp(rows.Count, 1, 4);
        if (rows.Count == 0)
            return;

        int rowH = h / n;
        float rowFont = Math.Min(theme.RowFontSize, rowH - 2);
        if (rowFont < 8f)
            rowFont = 8f;

        // Left label column width: widest label, capped, so values still have room.
        float labelW = 0f;
        for (int i = 0; i < n; i++)
            labelW = Math.Max(labelW, canvas.MeasureText(rows[i].Label, rowFont));
        int labelColW = Math.Min((int)labelW + 6, (int)(w * 0.5f));

        for (int i = 0; i < n; i++)
        {
            TileRow row = rows[i];
            int rowY = y + i * rowH;

            canvas.DrawText(Fit(canvas, row.Label, rowFont, labelColW, false),
                x, rowY, labelColW, rowH, theme.CaptionColor, rowFont, TextHAlign.Left, TextVAlign.Middle);

            string valueText = string.IsNullOrEmpty(row.Unit) ? row.Value : $"{row.Value} {row.Unit}";
            int valueX = x + labelColW;
            int valueW = w - labelColW;
            canvas.DrawText(Fit(canvas, valueText, rowFont, valueW, false),
                valueX, rowY, valueW, rowH, theme.RowColor, rowFont, TextHAlign.Right, TextVAlign.Middle);
        }
    }

    /// <summary>
    /// Draws a large value with a smaller unit beside it, the pair centered horizontally within
    /// the box and vertically middled. The value font is shrunk to fit the available width; the
    /// unit sits slightly above the value's vertical center per the design.
    /// </summary>
    private static void DrawValueUnit(IRenderCanvas canvas, int x, int y, int w, int h,
        string value, string unit, float valueFont, float unitFont, PluginColor valueColor, PluginColor unitColor)
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
            TextHAlign.Left, TextVAlign.Middle, bold: true);

        if (hasUnit)
        {
            // Nudge the unit up so it reads as a superscript-ish unit next to the number.
            int unitY = y - (int)(fitFont * 0.12f);
            canvas.DrawText(unit, startX + (int)Math.Ceiling(numW) + gap, unitY, (int)Math.Ceiling(unitW) + 1, h,
                unitColor, unitFont, TextHAlign.Left, TextVAlign.Middle);
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
