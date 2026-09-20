namespace Lokad.Svg.Fonts;

/// <summary>Converts one decoded TrueType glyph outline into SVG user coordinates.</summary>
internal static class SvgGlyphOutline
{
    internal static SvgGlyphQuadraticSegment[] GetContours(SvgPreparedFont font, SvgPositionedGlyph glyph, double fontSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (glyph.GlyphIndex == 0 || glyph.GlyphIndex >= font.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph));
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(glyph.X) || !double.IsFinite(glyph.Y)) throw new ArgumentOutOfRangeException(nameof(glyph));
        double scale = fontSize / font.UnitsPerEm;
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(fontSize));

        OpenTypeQuadraticSegment[] source = OpenTypeFont.GetGlyphContours(font.Bytes, glyph.GlyphIndex, cancellationToken);
        var contours = new SvgGlyphQuadraticSegment[source.Length];
        for (int index = 0; index < contours.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenTypeQuadraticSegment segment = source[index];
            contours[index] = new SvgGlyphQuadraticSegment(Transform(segment.Start), Transform(segment.Control), Transform(segment.End));
        }
        return contours;

        SvgGlyphOutlinePoint Transform(OpenTypeOutlinePoint point) => new(glyph.X + point.X * scale, glyph.Y - point.Y * scale);
    }
}

/// <summary>Represents one font-outline point in SVG user coordinates.</summary>
internal readonly record struct SvgGlyphOutlinePoint(double X, double Y);

/// <summary>Represents one SVG-coordinate quadratic glyph segment; a line has control equal to end.</summary>
internal readonly record struct SvgGlyphQuadraticSegment(SvgGlyphOutlinePoint Start, SvgGlyphOutlinePoint Control, SvgGlyphOutlinePoint End);
