namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;

/// <summary>Builds one bounded simple horizontal text run from prepared caller-owned font data.</summary>
internal static class SvgTextRunBuilder
{
    internal static SvgTextRun Create(SvgTextStyle style, string text, SvgFontSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(snapshot);
        SvgPreparedFont font = snapshot.GetFont(style.Font);
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, text, style.FontSize, style.X, style.Y, cancellationToken);
        return new SvgTextRun(style, font, glyphs);
    }

    /// <summary>Expands every positioned glyph in a simple text run into closed SVG-coordinate contours.</summary>
    internal static SvgGlyphQuadraticContour[][] GetGlyphContours(SvgTextRun run, CancellationToken cancellationToken)
    {
        var glyphContours = new SvgGlyphQuadraticContour[run.Glyphs.Length][];
        for (int index = 0; index < glyphContours.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            glyphContours[index] = SvgGlyphOutline.GetContours(run.Font, run.Glyphs[index], run.Style.FontSize, cancellationToken);
        }
        return glyphContours;
    }
}

/// <summary>Represents one simple single-font horizontal text run before glyph painting.</summary>
internal readonly record struct SvgTextRun(SvgTextStyle Style, SvgPreparedFont Font, SvgPositionedGlyph[] Glyphs);
