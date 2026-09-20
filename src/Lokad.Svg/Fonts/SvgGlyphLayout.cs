namespace Lokad.Svg.Fonts;

using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Builds bounded horizontal glyph positions from one prepared standalone-TrueType face.</summary>
internal static class SvgGlyphLayout
{
    internal static SvgPositionedGlyph[] Layout(SvgPreparedFont font, string text, double fontSize, double originX, double originY, CancellationToken cancellationToken)
    {
        using SvgPositionedGlyphStorage storage = CreateStorage(font, text, fontSize, originX, originY, new RenderBudget(long.MaxValue), cancellationToken);
        return storage.Glyphs;
    }

    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, RenderBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(budget);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(originX) || !double.IsFinite(originY)) throw new ArgumentOutOfRangeException(nameof(originX));
        double scale = fontSize / font.UnitsPerEm;
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        int glyphCount = 0;
        foreach (Rune scalar in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (glyphCount == SvgTextLimits.MaximumCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The text glyph count exceeds the admitted layout limit.");
            glyphCount++;
        }
        if (glyphCount == 0) return new SvgPositionedGlyphStorage([], null);
        RenderBudget.BudgetReservation reservation = budget.Reserve(checked(32L + glyphCount * 32L), SvgMemoryCategory.Geometry);
        try
        {
            var glyphs = new SvgPositionedGlyph[glyphCount];
            int glyphPosition = 0;
            double x = originX;
            foreach (Rune scalar in text.EnumerateRunes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort glyphIndex = OpenTypeFont.GetGlyphIndex(font.Bytes, (uint)scalar.Value, font.Metrics, cancellationToken);
                if (glyphIndex == 0) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font has no glyph for an admitted text scalar.", new SvgFontDiagnostic(SvgFontFailureStage.Glyph, font.Resolution.Resolution.Request.Family, font.Resolution.Resolution.Face.ResolvedFamily, font.Resolution.Resolution.Face.File.RelativePath + ":" + font.Resolution.Resolution.Face.FaceIndex, string.Empty));
                ushort advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyphIndex, font.Metrics, cancellationToken);
                double scaledAdvance = advance * scale;
                if (!double.IsFinite(scaledAdvance) || !double.IsFinite(x + scaledAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[glyphPosition++] = new SvgPositionedGlyph(glyphIndex, x, originY, scaledAdvance);
                x += scaledAdvance;
            }
            return new SvgPositionedGlyphStorage(glyphs, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }
}

/// <summary>Owns a budget reservation for one positioned-glyph array until direct text lowering completes.</summary>
internal sealed class SvgPositionedGlyphStorage : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, RenderBudget.BudgetReservation? reservation)
    {
        Glyphs = glyphs;
        _reservation = reservation;
    }

    internal SvgPositionedGlyph[] Glyphs { get; }

    public void Dispose() => Interlocked.Exchange(ref _reservation, null)?.Dispose();
}

/// <summary>Represents one glyph index, origin and horizontal advance in SVG user units.</summary>
internal readonly record struct SvgPositionedGlyph(ushort GlyphIndex, double X, double Y, double Advance);
