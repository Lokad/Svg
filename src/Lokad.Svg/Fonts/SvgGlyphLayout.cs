namespace Lokad.Svg.Fonts;

using System.Text;

/// <summary>Builds bounded horizontal glyph positions from one prepared standalone-TrueType face.</summary>
internal static class SvgGlyphLayout
{
    internal static SvgPositionedGlyph[] Layout(SvgPreparedFont font, string text, double fontSize, double originX, double originY, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(originX) || !double.IsFinite(originY)) throw new ArgumentOutOfRangeException(nameof(originX));
        const int maximumGlyphs = 32_768;
        var glyphs = new List<SvgPositionedGlyph>();
        double scale = fontSize / font.UnitsPerEm;
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        double x = originX;
        foreach (Rune scalar in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (glyphs.Count == maximumGlyphs) throw new SvgException(SvgErrorCode.LimitExceeded, "The text glyph count exceeds the admitted layout limit.");
            ushort glyphIndex = OpenTypeFont.GetGlyphIndex(font.Bytes, (uint)scalar.Value, cancellationToken);
            if (glyphIndex == 0) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font has no glyph for an admitted text scalar.", new SvgFontDiagnostic(SvgFontFailureStage.Glyph, font.Resolution.Resolution.Request.Family, font.Resolution.Resolution.Face.ResolvedFamily, font.Resolution.Resolution.Face.File.RelativePath + ":" + font.Resolution.Resolution.Face.FaceIndex, string.Empty));
            ushort advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyphIndex, cancellationToken);
            double scaledAdvance = advance * scale;
            if (!double.IsFinite(scaledAdvance) || !double.IsFinite(x + scaledAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
            glyphs.Add(new SvgPositionedGlyph(glyphIndex, x, originY, scaledAdvance));
            x += scaledAdvance;
        }
        return glyphs.ToArray();
    }
}

/// <summary>Represents one glyph index, origin and horizontal advance in SVG user units.</summary>
internal readonly record struct SvgPositionedGlyph(ushort GlyphIndex, double X, double Y, double Advance);
