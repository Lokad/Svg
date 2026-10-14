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
        return storage.Glyphs.ToArray();
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
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, letterSpacingSpans, positionAdjustments, anchor, null, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Builds output-grid positions while retaining authored origins and bounded chunk/spacing semantics.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, letterSpacingSpans, positionAdjustments, [], anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Retains effective glyph paints only for varying normalized intervals, sharing layout, cancellation and owned geometry storage with the ordinary run.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, letterSpacingSpans, positionAdjustments, paintSpans, [], anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Shares continuous positions and anchoring across bounded supplied-face and size intervals; material glyph-font storage borrows snapshot faces.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, 0, false, letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Applies word spacing to retained separator advances using the existing normalized spacing intervals and glyph owner.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, double wordSpacing, bool retainDecorationFragments, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(sourceElementId);
        if (!double.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(originX) || !double.IsFinite(originY) || !double.IsFinite(letterSpacing)) throw new ArgumentOutOfRangeException(nameof(originX));
        if (!double.IsFinite(wordSpacing)) throw new ArgumentOutOfRangeException(nameof(wordSpacing));
        ValidateLetterSpacingSpans(text, letterSpacingSpans);
        ValidatePositionAdjustments(text, positionAdjustments);
        int paintEnd = 0;
        foreach (SvgTextPaintSpan paint in paintSpans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            workCounter?.Consume(cancellationToken);
            if (paint.Start != paintEnd || paint.End <= paint.Start || paint.End > text.Length) throw new ArgumentException("Text paint intervals must partition normalized text.", nameof(paintSpans));
            if (paint.End < text.Length && char.IsSurrogatePair(text, paint.End - 1)) throw new ArgumentException("Text paint intervals cannot divide a Unicode scalar.", nameof(paintSpans));
            paintEnd = paint.End;
        }
        if (!paintSpans.IsEmpty && paintEnd != text.Length) throw new ArgumentException("Text paint intervals must cover normalized text.", nameof(paintSpans));
        int fontEnd = 0;
        foreach (SvgTextResolvedFontSpan span in fontSpans)
        {
            cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
            if (span.Start != fontEnd || span.End <= span.Start || span.End > text.Length || span.Style.Font is null || !double.IsFinite(span.Style.FontSize) || span.Style.FontSize <= 0)
                throw new ArgumentException("Text font intervals must partition normalized text with valid supplied faces and sizes.", nameof(fontSpans));
            if (span.End < text.Length && char.IsSurrogatePair(text, span.End - 1)) throw new ArgumentException("Text font intervals cannot divide a Unicode scalar.", nameof(fontSpans));
            fontEnd = span.End;
        }
        if (!fontSpans.IsEmpty && fontEnd != text.Length) throw new ArgumentException("Text font intervals must cover normalized text.", nameof(fontSpans));
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
        if (glyphCount == 0) return new SvgPositionedGlyphStorage([], 0, null);
        RenderBudget.BudgetReservation reservation = budget.Reserve(checked(32L + glyphCount * 32L), SvgMemoryCategory.Geometry);
        RenderBudget.BudgetReservation? clusterReservation = null;
        RenderBudget.BudgetReservation? paintReservation = null;
        RenderBudget.BudgetReservation? fontReservation = null;
        RenderBudget.BudgetReservation? rotationReservation = null;
        RenderBudget.BudgetReservation? fragmentReservation = null;
        try
        {
            using BudgetedParserList<SvgTextDecorationFragment>? fragments = retainDecorationFragments ? new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken) : null;
            var glyphs = new SvgPositionedGlyph[glyphCount];
            SvgTextGlyphPaint[] glyphPaints = [];
            SvgGlyphFontStyle[] glyphFonts = [];
            double[] glyphRotations = [];
            foreach (SvgTextPositionAdjustment adjustment in positionAdjustments)
            {
                if (adjustment.Rotation is null) continue;
                rotationReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<double>(glyphCount), SvgMemoryCategory.Geometry);
                glyphRotations = new double[glyphCount];
                break;
            }
            if (!fontSpans.IsEmpty)
            {
                fontReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphFontStyle>(glyphCount), SvgMemoryCategory.Geometry);
                glyphFonts = new SvgGlyphFontStyle[glyphCount];
            }
            if (paintSpans.Length > 1)
            {
                paintReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgTextGlyphPaint>(glyphCount), SvgMemoryCategory.Geometry);
                glyphPaints = new SvgTextGlyphPaint[glyphCount];
            }
            int[]? sourceLengths = null;
            OpenTypeGsubLigatureTable? ligatures = OpenTypeFont.GetLatinLigatures(font.Bytes, font.Metrics, workCounter, cancellationToken);
            Span<GlyphLookup> cachedLookups = stackalloc GlyphLookup[GlyphLookupCacheCapacity];
            int cachedLookupCount = 0;
            int glyphPosition = 0;
            int fragmentStart = 0;
            int mappedSourceOffset = 0;
            int mappedSpacingIndex = 0;
            int mappedPositionIndex = 0;
            int mappedPaintIndex = 0;
            int mappedFontIndex = 0;
            var mappedScalars = text.EnumerateRunes();
            while (mappedScalars.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                workCounter?.Consume(cancellationToken);
                Rune scalar = mappedScalars.Current;
                if (!fontSpans.IsEmpty)
                {
                    while (fontSpans[mappedFontIndex].End <= mappedSourceOffset) mappedFontIndex++;
                    SvgGlyphFontStyle currentFont = fontSpans[mappedFontIndex].Style;
                    if (!ReferenceEquals(font, currentFont.Font))
                    {
                        font = currentFont.Font;
                        cachedLookupCount = 0;
                        ligatures = OpenTypeFont.GetLatinLigatures(font.Bytes, font.Metrics, workCounter, cancellationToken);
                    }
                    fontSize = currentFont.FontSize;
                    scale = fontSize / font.UnitsPerEm;
                    glyphFonts[glyphPosition] = currentFont;
                }
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
                    OpenTypeFont.ValidateGlyphPositioning(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                    advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                    if (cachedLookupCount < cachedLookups.Length) cachedLookups[cachedLookupCount++] = new GlyphLookup(scalarValue, glyphIndex, advance);
                }
                int sourceLength = scalar.Utf16SequenceLength;
                double spacing = GetLetterSpacing(mappedSourceOffset, letterSpacing, letterSpacingSpans, ref mappedSpacingIndex);
                while (mappedPositionIndex < positionAdjustments.Length && positionAdjustments[mappedPositionIndex].Start <= mappedSourceOffset)
                {
                    SvgTextPositionAdjustment adjustment = positionAdjustments[mappedPositionIndex++];
                    if (glyphRotations.Length != 0 && adjustment.Start == mappedSourceOffset) glyphRotations[glyphPosition] = adjustment.Rotation ?? 0;
                }
                int sourceEnd = letterSpacingSpans.IsEmpty ? text.Length : letterSpacingSpans[mappedSpacingIndex].End;
                if (!fontSpans.IsEmpty) sourceEnd = Math.Min(sourceEnd, fontSpans[mappedFontIndex].End);
                if (!paintSpans.IsEmpty)
                {
                    while (paintSpans[mappedPaintIndex].End <= mappedSourceOffset) mappedPaintIndex++;
                    sourceEnd = Math.Min(sourceEnd, paintSpans[mappedPaintIndex].End);
                    if (glyphPaints.Length != 0) glyphPaints[glyphPosition] = paintSpans[mappedPaintIndex].Paint;
                }
                if (mappedPositionIndex < positionAdjustments.Length) sourceEnd = Math.Min(sourceEnd, positionAdjustments[mappedPositionIndex].Start);
                if (spacing == 0 && ligatures is {} graph && OpenTypeFont.TryGetLatinLigature(font.Bytes, font.Metrics, graph, text, mappedSourceOffset, sourceEnd, glyphIndex, workCounter, cancellationToken, out ushort ligatureGlyph, out int ligatureLength))
                {
                    if (sourceLengths is null)
                    {
                        clusterReservation = budget.Reserve(checked(32L + glyphCount * 4L), SvgMemoryCategory.Geometry);
                        sourceLengths = new int[glyphCount];
                        int previousIndex = 0;
                        foreach (Rune previous in text.AsSpan(0, mappedSourceOffset).EnumerateRunes())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            workCounter?.Consume(cancellationToken);
                            sourceLengths[previousIndex++] = previous.Utf16SequenceLength;
                        }
                    }
                    for (int component = 1; component < ligatureLength; component++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        workCounter?.Consume(cancellationToken);
                        if (!mappedScalars.MoveNext()) throw new InvalidOperationException("The ligature source range is invalid.");
                    }
                    glyphIndex = ligatureGlyph;
                    sourceLength = ligatureLength;
                    OpenTypeFont.ValidateGlyphPositioning(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                    advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                }
                if (sourceLengths is not null) sourceLengths[glyphPosition] = sourceLength;
                double scaledAdvance = deviceMetrics is SvgGlyphDeviceMetrics metrics ? metrics.GetAdvance(advance, font.UnitsPerEm, fontSize) : advance * scale;
                if (advance != 0 && scalar.Value is 0x20 or 0xa0 or 0x1361 or 0x10100 or 0x10101 or 0x1039f or 0x1091f)
                    scaledAdvance += letterSpacingSpans.IsEmpty ? wordSpacing : letterSpacingSpans[mappedSpacingIndex].WordSpacing;
                if (!double.IsFinite(scaledAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[glyphPosition++] = new SvgPositionedGlyph(glyphIndex, 0, originY, scaledAdvance);
                mappedSourceOffset += sourceLength;
                if (fragments is not null && mappedSourceOffset >= sourceEnd)
                {
                    fragments.Add(new(fragmentStart, glyphPosition, 0));
                    fragmentStart = glyphPosition;
                }
            }
            int positionedGlyphCount = glyphPosition;
            double x = originX;
            double fontFragmentOrigin = originX;
            int spacingSpanIndex = 0;
            int positionBoundaryIndex = 0;
            int sourceOffset = 0;
            int fragmentIndex = 0;
            double decorationOrigin = originX;
            for (int index = 0; index < positionedGlyphCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgPositionedGlyph glyph = glyphs[index];
                if (fragments is not null && index == fragments[fragmentIndex].Start) decorationOrigin = x;
                if (glyphFonts.Length != 0)
                {
                    font = glyphFonts[index].Font; fontSize = glyphFonts[index].FontSize; scale = fontSize / font.UnitsPerEm;
                }
                double currentLetterSpacing = GetLetterSpacing(sourceOffset, letterSpacing, letterSpacingSpans, ref spacingSpanIndex);
                sourceOffset += sourceLengths is null ? char.IsSurrogatePair(text, sourceOffset) ? 2 : 1 : sourceLengths[index];
                while (positionBoundaryIndex < positionAdjustments.Length && positionAdjustments[positionBoundaryIndex].Start < sourceOffset) positionBoundaryIndex++;
                bool chunkEnds = index + 1 == positionedGlyphCount || positionBoundaryIndex < positionAdjustments.Length && positionAdjustments[positionBoundaryIndex].Start == sourceOffset && positionAdjustments[positionBoundaryIndex].HasAbsolutePosition;
                bool sameFont = glyphFonts.Length == 0 || index + 1 < positionedGlyphCount && glyphFonts[index] == glyphFonts[index + 1];
                bool sameWordSpacing = letterSpacingSpans.IsEmpty || spacingSpanIndex + 1 == letterSpacingSpans.Length
                    || letterSpacingSpans[spacingSpanIndex].End != sourceOffset
                    || letterSpacingSpans[spacingSpanIndex].WordSpacing == letterSpacingSpans[spacingSpanIndex + 1].WordSpacing;
                bool sameDecoration = glyphPaints.Length == 0 || index + 1 < positionedGlyphCount
                    && ReferenceEquals(glyphPaints[index].Decoration, glyphPaints[index + 1].Decoration);
                int pairAdjustment = chunkEnds || !sameFont ? 0 : OpenTypeFont.GetPairAdjustment(font.Bytes, glyph.GlyphIndex, glyphs[index + 1].GlyphIndex, font.Metrics, workCounter, cancellationToken);
                double kerning = pairAdjustment * scale;
                if (pairAdjustment != 0 && deviceMetrics is { IsTextPath: true } pathMetrics)
                {
                    ushort advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyph.GlyphIndex, font.Metrics, workCounter, cancellationToken);
                    glyph = glyph with { Advance = pathMetrics.GetAdvance(advance, pairAdjustment, font.UnitsPerEm, fontSize) };
                    kerning = 0;
                }
                double nextX = chunkEnds ? x + glyph.Advance : x + glyph.Advance + kerning + currentLetterSpacing;
                if (fragments is not null && index + 1 == fragments[fragmentIndex].End)
                {
                    fragments[fragmentIndex] = fragments[fragmentIndex] with { Width = nextX - decorationOrigin };
                    fragmentIndex++;
                }
                if (!chunkEnds && (!sameFont || !sameWordSpacing || !sameDecoration) && deviceMetrics is { } fragmentMetrics)
                {
                    // Native shaping fragments round their total inline width
                    // before the next face/size/spacing/decoration fragment begins.
                    nextX = fontFragmentOrigin + fragmentMetrics.RoundWidth(nextX - fontFragmentOrigin);
                    fontFragmentOrigin = nextX;
                }
                else if (chunkEnds) fontFragmentOrigin = nextX;
                if (!double.IsFinite(kerning) || !double.IsFinite(nextX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[index] = glyph with { X = x };
                x = nextX;
            }
            double relativeX = 0;
            double relativeY = 0;
            int positionAdjustmentIndex = 0;
            sourceOffset = 0;
            int chunkStart = 0;
            double chunkShift = 0;
            while (chunkStart < positionedGlyphCount)
            {
                int chunkEndSourceOffset = text.Length;
                for (int adjustmentIndex = positionAdjustmentIndex; adjustmentIndex < positionAdjustments.Length; adjustmentIndex++)
                {
                    SvgTextPositionAdjustment adjustment = positionAdjustments[adjustmentIndex];
                    if (adjustment.Start > sourceOffset && adjustment.HasAbsolutePosition)
                    {
                        chunkEndSourceOffset = adjustment.Start;
                        break;
                    }
                }
                int chunkEnd = positionedGlyphCount;
                if (chunkEndSourceOffset != text.Length)
                {
                    chunkEnd = chunkStart;
                    int boundarySourceOffset = sourceOffset;
                    while (boundarySourceOffset < chunkEndSourceOffset)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        workCounter?.Consume(cancellationToken);
                        boundarySourceOffset += sourceLengths is null ? char.IsSurrogatePair(text, boundarySourceOffset) ? 2 : 1 : sourceLengths[chunkEnd];
                        chunkEnd++;
                    }
                }
                double nominalOrigin = glyphs[chunkStart].X;
                double nominalEnd = chunkEnd == positionedGlyphCount ? x : glyphs[chunkEnd].X;
                double width = nominalEnd - nominalOrigin;
                double chunkWidth = deviceMetrics is SvgGlyphDeviceMetrics widthMetrics ? widthMetrics.RoundWidth(width) : width;
                double anchorOffset = anchor switch
                {
                    SvgTextAnchor.Start => 0,
                    SvgTextAnchor.Middle => -chunkWidth / 2,
                    SvgTextAnchor.End => -chunkWidth,
                    _ => throw new ArgumentOutOfRangeException(nameof(anchor))
                };
                if (!double.IsFinite(anchorOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
                for (int index = chunkStart; index < chunkEnd; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    workCounter?.Consume(cancellationToken);
                    double localX = glyphs[index].X - nominalOrigin;
                    double positionedX = deviceMetrics is SvgGlyphDeviceMetrics positionMetrics ? nominalOrigin + chunkShift + positionMetrics.RoundOffset(localX) : glyphs[index].X;
                    ApplyPositionAdjustment(ref relativeX, ref relativeY, positionedX, originY, sourceOffset, positionAdjustments, ref positionAdjustmentIndex);
                    sourceOffset += sourceLengths is null ? char.IsSurrogatePair(text, sourceOffset) ? 2 : 1 : sourceLengths[index];
                    double anchoredX = positionedX + anchorOffset + relativeX;
                    double adjustedY = glyphs[index].Y + relativeY;
                    if (!double.IsFinite(anchoredX) || !double.IsFinite(adjustedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
                    glyphs[index] = glyphs[index] with { X = anchoredX, Y = adjustedY };
                }
                chunkStart = chunkEnd;
                chunkShift += chunkWidth - width;
            }
            OwnedParserArray<SvgTextDecorationFragment> retainedFragments = fragments is null ? new([], null) : fragments.Detach();
            fragmentReservation = retainedFragments.Reservation;
            SvgPositionedGlyphStorage result = new(glyphs, positionedGlyphCount, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, glyphRotations, rotationReservation, retainedFragments.Items, fragmentReservation)
            { NaturalWidth = deviceMetrics is { } naturalMetrics ? naturalMetrics.RoundWidth(x - originX) : x - originX };
            paintReservation = null;
            fontReservation = null;
            rotationReservation = null;
            fragmentReservation = null;
            return result;
        }
        catch
        {
            reservation.Dispose();
            paintReservation?.Dispose();
            fontReservation?.Dispose();
            rotationReservation?.Dispose();
            fragmentReservation?.Dispose();
            throw;
        }
        finally { clusterReservation?.Dispose(); }

        static double GetLetterSpacing(int sourceOffset, double defaultLetterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ref int spanIndex)
        {
            if (letterSpacingSpans.IsEmpty) return defaultLetterSpacing;
            while (sourceOffset >= letterSpacingSpans[spanIndex].End) spanIndex++;
            return letterSpacingSpans[spanIndex].LetterSpacing;
        }

        static void ApplyPositionAdjustment(ref double x, ref double y, double nominalX, double originY, int sourceOffset, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ref int adjustmentIndex)
        {
            if (adjustmentIndex == positionAdjustments.Length || positionAdjustments[adjustmentIndex].Start != sourceOffset) return;
            SvgTextPositionAdjustment adjustment = positionAdjustments[adjustmentIndex++];
            if (adjustment.X is double absoluteX) x = absoluteX - nominalX;
            if (adjustment.Y is double absoluteY) y = absoluteY - originY;
            x += adjustment.Dx;
            y += adjustment.Dy;
        }

        static void ValidateLetterSpacingSpans(string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans)
        {
            int expectedStart = 0;
            for (int index = 0; index < letterSpacingSpans.Length; index++)
            {
                SvgTextLetterSpacingSpan span = letterSpacingSpans[index];
                if (!double.IsFinite(span.LetterSpacing) || !double.IsFinite(span.WordSpacing) || span.Start != expectedStart || span.End <= span.Start || span.End > text.Length) throw new ArgumentException("The text spacing spans are invalid.", nameof(letterSpacingSpans));
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
                if (adjustment.X is double absoluteX && !double.IsFinite(absoluteX) || adjustment.Y is double absoluteY && !double.IsFinite(absoluteY) || !double.IsFinite(adjustment.Dx) || !double.IsFinite(adjustment.Dy) || adjustment.Rotation is double angle && !double.IsFinite(angle) || adjustment.Start <= previousStart || adjustment.Start < 0 || adjustment.Start >= text.Length || char.IsLowSurrogate(text[adjustment.Start])) throw new ArgumentException("The text position adjustments are invalid.", nameof(positionAdjustments));
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
    private readonly SvgPositionedGlyph[] _glyphs;
    private readonly int _count;
    private readonly SvgTextGlyphPaint[] _glyphPaints;
    private RenderBudget.BudgetReservation? _paintReservation;
    private readonly SvgGlyphFontStyle[] _glyphFonts;
    private RenderBudget.BudgetReservation? _fontReservation;
    private readonly double[] _glyphRotations;
    private RenderBudget.BudgetReservation? _rotationReservation;
    private readonly SvgTextDecorationFragment[] _decorationFragments;
    private RenderBudget.BudgetReservation? _fragmentReservation;

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation)
        : this(glyphs, count, reservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation)
        : this(glyphs, count, reservation, glyphPaints, paintReservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation, SvgGlyphFontStyle[] glyphFonts, RenderBudget.BudgetReservation? fontReservation)
        : this(glyphs, count, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation, SvgGlyphFontStyle[] glyphFonts, RenderBudget.BudgetReservation? fontReservation, double[] glyphRotations, RenderBudget.BudgetReservation? rotationReservation)
        : this(glyphs, count, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, glyphRotations, rotationReservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation, SvgGlyphFontStyle[] glyphFonts, RenderBudget.BudgetReservation? fontReservation, double[] glyphRotations, RenderBudget.BudgetReservation? rotationReservation, SvgTextDecorationFragment[] decorationFragments, RenderBudget.BudgetReservation? fragmentReservation)
    {
        _glyphs = glyphs;
        _count = count;
        _reservation = reservation;
        _glyphPaints = glyphPaints;
        _paintReservation = paintReservation;
        _glyphFonts = glyphFonts; _fontReservation = fontReservation;
        _glyphRotations = glyphRotations; _rotationReservation = rotationReservation;
        _decorationFragments = decorationFragments; _fragmentReservation = fragmentReservation;
    }

    internal ReadOnlySpan<SvgPositionedGlyph> Glyphs => _glyphs.AsSpan(0, _count);
    internal double NaturalWidth { get; init; }
    internal ReadOnlySpan<double> GlyphRotations => _glyphRotations.Length == 0 ? [] : _glyphRotations.AsSpan(0, _count);
    internal ReadOnlySpan<SvgTextDecorationFragment> DecorationFragments => _decorationFragments;
    internal Span<SvgTextDecorationFragment> MutableDecorationFragments => _decorationFragments;
    /// <summary>Available only during owning run preparation, before scene borrowers exist.</summary>
    internal Span<SvgPositionedGlyph> MutableGlyphs => _glyphs.AsSpan(0, _count);
    internal ReadOnlySpan<SvgTextGlyphPaint> GlyphPaints => _glyphPaints.Length == 0 ? [] : _glyphPaints.AsSpan(0, _count);
    internal ReadOnlySpan<SvgGlyphFontStyle> GlyphFonts => _glyphFonts.Length == 0 ? [] : _glyphFonts.AsSpan(0, _count);

    public void Dispose()
    {
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
        Interlocked.Exchange(ref _paintReservation, null)?.Dispose();
        Array.Clear(_glyphFonts);
        Interlocked.Exchange(ref _fontReservation, null)?.Dispose();
        Interlocked.Exchange(ref _rotationReservation, null)?.Dispose();
        Interlocked.Exchange(ref _fragmentReservation, null)?.Dispose();
    }
}

/// <summary>Borrows a prepared face and its effective user-space size for one glyph.</summary>
internal readonly record struct SvgGlyphFontStyle(SvgPreparedFont Font, double FontSize);
internal readonly record struct SvgTextResolvedFontSpan(int Start, int End, SvgGlyphFontStyle Style);

/// <summary>Represents one glyph index, origin and horizontal advance in SVG user units.</summary>
internal readonly record struct SvgPositionedGlyph(ushort GlyphIndex, double X, double Y, double Advance)
{
    /// <summary>Applies composed translation after local spacing, anchoring and absolute span placement, retaining the native horizontal strike and advance.</summary>
    internal SvgPositionedGlyph Translate(SvgAffineTransform placement)
    {
        if (placement == SvgAffineTransform.Identity) return this;
        if (!placement.IsTranslation) throw new ArgumentException("Glyph translation requires unchanged axes.", nameof(placement));
        SvgDevicePoint origin = placement.Apply(X, Y);
        return this with { X = origin.X, Y = origin.Y };
    }
}
