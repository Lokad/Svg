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
}

/// <summary>Represents one simple single-font horizontal text run before glyph painting.</summary>
internal readonly record struct SvgTextRun(SvgTextStyle Style, SvgPreparedFont Font, SvgPositionedGlyph[] Glyphs);
