namespace Lokad.Svg.Fonts;

using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Builds bounded horizontal glyph positions from one prepared standalone-TrueType face.</summary>
internal static class SvgGlyphLayout
{
    private const int GlyphLookupCacheCapacity = 16;

    internal static SvgPositionedGlyph[] Layout(SvgPreparedFont font, string text, double fontSize, double originX, double originY, CancellationToken cancellationToken)
        => Layout(font, text, fontSize, originX, originY, SvgTextAnchor.Start, cancellationToken);

    internal static SvgPositionedGlyph[] Layout(SvgPreparedFont font, string text, double fontSize, double originX, double originY, SvgTextAnchor anchor, CancellationToken cancellationToken)
    {
        using SvgPositionedGlyphStorage storage = CreateStorage(font, text, fontSize, originX, originY, anchor, new RenderBudget(long.MaxValue), cancellationToken);
        return storage.Glyphs;
    }

    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, RenderBudget budget, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, SvgTextAnchor.Start, budget, cancellationToken);

    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, SvgTextAnchor anchor, RenderBudget budget, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, anchor, budget, string.Empty, null, cancellationToken);

    /// <summary>Creates positioned glyph storage while charging font table lookups to the conversion work counter.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, SvgTextAnchor anchor, RenderBudget budget, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, anchor, budget, string.Empty, workCounter, cancellationToken);

    /// <summary>Creates positioned glyph storage with bounded source-element context for glyph diagnostics.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, SvgTextAnchor anchor, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, 0, anchor, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Creates positioned glyph storage with scalar inter-glyph spacing and bounded source-element context for diagnostics.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, SvgTextAnchor anchor, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, [], anchor, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Creates positioned glyph storage with normalized UTF-16 letter-spacing intervals from nested direct-text spans.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, SvgTextAnchor anchor, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, letterSpacingSpans, [], anchor, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Creates positioned glyph storage with normalized letter-spacing intervals and scalar tspan offsets from direct-text extraction.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgTextAnchor anchor, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(sourceElementId);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(originX) || !double.IsFinite(originY) || !double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(originX));
        ValidateLetterSpacingSpans(text, letterSpacingSpans);
        ValidatePositionAdjustments(text, positionAdjustments);
        double scale = fontSize / font.UnitsPerEm;
        if (!double.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        int glyphCount = 0;
        foreach (Rune scalar in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            workCounter?.Consume(cancellationToken);
            if (glyphCount == SvgTextLimits.MaximumCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The text glyph count exceeds the admitted layout limit.");
            glyphCount++;
        }
        if (glyphCount == 0) return new SvgPositionedGlyphStorage([], null);
        RenderBudget.BudgetReservation reservation = budget.Reserve(checked(32L + glyphCount * 32L), SvgMemoryCategory.Geometry);
        try
        {
            var glyphs = new SvgPositionedGlyph[glyphCount];
            Span<GlyphLookup> cachedLookups = stackalloc GlyphLookup[GlyphLookupCacheCapacity];
            int cachedLookupCount = 0;
            int glyphPosition = 0;
            foreach (Rune scalar in text.EnumerateRunes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                workCounter?.Consume(cancellationToken);
                uint scalarValue = (uint)scalar.Value;
                ushort glyphIndex = 0;
                ushort advance = 0;
                int lookupIndex = 0;
                for (; lookupIndex < cachedLookupCount; lookupIndex++)
                {
                    GlyphLookup cached = cachedLookups[lookupIndex];
                    if (cached.Scalar != scalarValue) continue;
                    glyphIndex = cached.GlyphIndex;
                    advance = cached.Advance;
                    break;
                }
                if (lookupIndex == cachedLookupCount)
                {
                    glyphIndex = OpenTypeFont.GetGlyphIndex(font.Bytes, scalarValue, font.Metrics, workCounter, cancellationToken);
                    if (glyphIndex == 0) throw new SvgException(SvgErrorCode.UnresolvedResource, "The selected font has no glyph for an admitted text scalar.", new SvgFontDiagnostic(SvgFontFailureStage.Glyph, font.Resolution.Resolution.Request.Family, font.Resolution.Resolution.Face.ResolvedFamily, font.Resolution.Resolution.Face.File.RelativePath + ":" + font.Resolution.Resolution.Face.FaceIndex, sourceElementId));
                    advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                    if (cachedLookupCount < cachedLookups.Length) cachedLookups[cachedLookupCount++] = new GlyphLookup(scalarValue, glyphIndex, advance);
                }
                double scaledAdvance = advance * scale;
                if (!double.IsFinite(scaledAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[glyphPosition++] = new SvgPositionedGlyph(glyphIndex, 0, originY, scaledAdvance);
            }
            double x = originX;
            int spacingSpanIndex = 0;
            int sourceOffset = 0;
            var positionedScalars = text.EnumerateRunes();
            for (int index = 0; index < glyphs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgPositionedGlyph glyph = glyphs[index];
                double kerning = index + 1 == glyphs.Length ? 0 : OpenTypeFont.GetPairAdjustment(font.Bytes, glyph.GlyphIndex, glyphs[index + 1].GlyphIndex, font.Metrics, workCounter, cancellationToken) * scale;
                if (!positionedScalars.MoveNext()) throw new InvalidOperationException("The normalized text glyph count is invalid.");
                Rune scalar = positionedScalars.Current;
                double currentLetterSpacing = GetLetterSpacing(sourceOffset, letterSpacing, letterSpacingSpans, ref spacingSpanIndex);
                sourceOffset += scalar.Utf16SequenceLength;
                double nextX = index + 1 == glyphs.Length ? x + glyph.Advance : x + glyph.Advance + kerning + currentLetterSpacing;
                if (!double.IsFinite(kerning) || !double.IsFinite(nextX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[index] = glyph with { X = x };
                x = nextX;
            }
            double anchorOffset = anchor switch
            {
                SvgTextAnchor.Start => 0,
                SvgTextAnchor.Middle => (originX - x) / 2,
                SvgTextAnchor.End => originX - x,
                _ => throw new ArgumentOutOfRangeException(nameof(anchor))
            };
            if (!double.IsFinite(anchorOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
            double relativeX = 0;
            double relativeY = 0;
            int positionAdjustmentIndex = 0;
            sourceOffset = 0;
            var adjustedScalars = text.EnumerateRunes();
            for (int index = 0; index < glyphs.Length; index++)
            {
                workCounter?.Consume(cancellationToken);
                if (!adjustedScalars.MoveNext()) throw new InvalidOperationException("The normalized text glyph count is invalid.");
                Rune scalar = adjustedScalars.Current;
                ApplyPositionAdjustment(ref relativeX, ref relativeY, sourceOffset, positionAdjustments, ref positionAdjustmentIndex);
                sourceOffset += scalar.Utf16SequenceLength;
                double anchoredX = glyphs[index].X + anchorOffset + relativeX;
                double adjustedY = glyphs[index].Y + relativeY;
                if (!double.IsFinite(anchoredX) || !double.IsFinite(adjustedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
                glyphs[index] = glyphs[index] with { X = anchoredX, Y = adjustedY };
            }
            return new SvgPositionedGlyphStorage(glyphs, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }

        static double GetLetterSpacing(int sourceOffset, double defaultLetterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ref int spanIndex)
        {
            if (letterSpacingSpans.IsEmpty) return defaultLetterSpacing;
            while (sourceOffset >= letterSpacingSpans[spanIndex].End) spanIndex++;
            return letterSpacingSpans[spanIndex].LetterSpacing;
        }

        static void ApplyPositionAdjustment(ref double x, ref double y, int sourceOffset, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ref int adjustmentIndex)
        {
            if (adjustmentIndex == positionAdjustments.Length || positionAdjustments[adjustmentIndex].Start != sourceOffset) return;
            SvgTextPositionAdjustment adjustment = positionAdjustments[adjustmentIndex++];
            x += adjustment.Dx;
            y += adjustment.Dy;
        }

        static void ValidateLetterSpacingSpans(string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans)
        {
            int expectedStart = 0;
            for (int index = 0; index < letterSpacingSpans.Length; index++)
            {
                SvgTextLetterSpacingSpan span = letterSpacingSpans[index];
                if (!double.IsFinite(span.LetterSpacing) || span.Start != expectedStart || span.End <= span.Start || span.End > text.Length) throw new ArgumentException("The text letter-spacing spans are invalid.", nameof(letterSpacingSpans));
                if (span.Start != 0 && char.IsLowSurrogate(text[span.Start]) || span.End != text.Length && char.IsLowSurrogate(text[span.End])) throw new ArgumentException("The text letter-spacing spans split a Unicode scalar.", nameof(letterSpacingSpans));
                expectedStart = span.End;
            }
            if (!letterSpacingSpans.IsEmpty && expectedStart != text.Length) throw new ArgumentException("The text letter-spacing spans do not cover the normalized text.", nameof(letterSpacingSpans));
        }

        static void ValidatePositionAdjustments(string text, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments)
        {
            int previousStart = -1;
            for (int index = 0; index < positionAdjustments.Length; index++)
            {
                SvgTextPositionAdjustment adjustment = positionAdjustments[index];
                if (!double.IsFinite(adjustment.Dx) || !double.IsFinite(adjustment.Dy) || adjustment.Start <= previousStart || adjustment.Start < 0 || adjustment.Start >= text.Length || char.IsLowSurrogate(text[adjustment.Start])) throw new ArgumentException("The text position adjustments are invalid.", nameof(positionAdjustments));
                previousStart = adjustment.Start;
            }
        }
    }

    /// <summary>Represents one stack-resident per-run cmap and advance lookup.</summary>
    private readonly record struct GlyphLookup(uint Scalar, ushort GlyphIndex, ushort Advance);
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
