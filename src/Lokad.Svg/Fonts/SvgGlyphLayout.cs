// SVG character fragment widths adapt Blink's spacing-before-rounding order.
// Copyright 2016, 2021 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
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

    /// <summary>Builds native paint positions while retaining semantic widths and bounded chunk/spacing rules.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, letterSpacingSpans, positionAdjustments, [], anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Retains effective glyph paints only for varying normalized intervals, sharing layout, cancellation and owned geometry storage with the ordinary run.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, letterSpacingSpans, positionAdjustments, paintSpans, [], anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Shares continuous positions and anchoring across bounded supplied-face and size intervals; material glyph-font storage borrows snapshot faces.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorage(font, text, fontSize, originX, originY, letterSpacing, 0, false, letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken);

    /// <summary>Applies word spacing to retained separator advances using the existing normalized spacing intervals and glyph owner.</summary>
    internal static SvgPositionedGlyphStorage CreateStorage(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, double wordSpacing, bool retainPaintFragments, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorageAndMetrics(font, text, fontSize, originX, originY, letterSpacing, wordSpacing, retainPaintFragments,
            letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, [], anchor, deviceMetrics, budget, sourceElementId, workCounter, cancellationToken).Storage;

    /// <summary>Returns caller-owned glyph storage and its resolved metric mode after shaping determines whether spacing has a gap.</summary>
    /// <remarks>Fonts and input intervals remain borrowed. The caller disposes storage; metrics are a value.
    /// The same budget/work/cancellation contract as ordinary layout applies, without a second shaping pass.</remarks>
    internal static (SvgPositionedGlyphStorage Storage, SvgGlyphDeviceMetrics? Metrics) CreateStorageAndMetrics(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, double wordSpacing, bool retainPaintFragments, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, ReadOnlySpan<SvgTextNodeBoundary> nodeBoundaries, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
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
        int previousNode = 0;
        foreach (SvgTextNodeBoundary boundary in nodeBoundaries)
        {
            if (workCounter is null) cancellationToken.ThrowIfCancellationRequested(); else workCounter.Consume(cancellationToken);
            if (boundary.Offset <= previousNode || boundary.Offset >= text.Length || char.IsSurrogatePair(text, boundary.Offset - 1))
                throw new ArgumentException("Text-node boundaries must be ordered normalized scalar boundaries.", nameof(nodeBoundaries));
            previousNode = boundary.Offset;
        }
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
        if (glyphCount == 0) return (new SvgPositionedGlyphStorage([], 0, null), deviceMetrics);
        RenderBudget.BudgetReservation reservation = budget.Reserve(checked(32L + glyphCount * 32L), SvgMemoryCategory.Geometry);
        RenderBudget.BudgetReservation? clusterReservation = null;
        RenderBudget.BudgetReservation? paintReservation = null;
        RenderBudget.BudgetReservation? fontReservation = null;
        RenderBudget.BudgetReservation? rotationReservation = null;
        RenderBudget.BudgetReservation? fragmentReservation = null;
        try
        {
            using BudgetedParserList<SvgTextPaintFragment>? fragments = retainPaintFragments ? new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken) : null;
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
            int mappedNodeIndex = 0;
            int mappedSourceOffset = 0;
            int mappedSpacingIndex = 0;
            int mappedPositionIndex = 0;
            int mappedPaintIndex = 0;
            int mappedFontIndex = 0;
            int scalarIndex = 0;
            ushort firstDesignAdvance = 0;
            double firstWordGap = 0;
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
                    if (glyphIndex == 0) throw new SvgException(SvgErrorCode.UnresolvedResource, $"The selected font has no glyph for U+{scalar.Value:X4} at scalar index {scalarIndex} in the normalized text run.", new SvgFontDiagnostic(SvgFontFailureStage.Glyph, font.Resolution.Resolution.Request.Family, font.Resolution.Resolution.Face.ResolvedFamily, font.Resolution.Resolution.Face.File.RelativePath + ":" + font.Resolution.Resolution.Face.FaceIndex, sourceElementId,
                        font.Resolution.Resolution.Request.Weight, font.Resolution.Resolution.Request.Italic, font.Resolution.Resolution.IsFallback, scalar.Value, scalarIndex));
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
                        scalarIndex++;
                    }
                    glyphIndex = ligatureGlyph;
                    sourceLength = ligatureLength;
                    OpenTypeFont.ValidateGlyphPositioning(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                    advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyphIndex, font.Metrics, workCounter, cancellationToken);
                }
                if (sourceLengths is not null) sourceLengths[glyphPosition] = sourceLength;
                double wordGap = GetWordGap(scalar, advance, wordSpacing, letterSpacingSpans, mappedSpacingIndex);
                double scaledAdvance = deviceMetrics is SvgGlyphDeviceMetrics metrics
                    ? metrics.IsTextPath ? metrics.GetAdvance(advance, 0, font.UnitsPerEm, fontSize, spacing, wordGap)
                        : metrics.GetAdvance(advance, font.UnitsPerEm, fontSize) + wordGap
                    : advance * scale + wordGap;
                if (glyphPosition == 0) { firstDesignAdvance = advance; firstWordGap = wordGap; }
                if (!double.IsFinite(scaledAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[glyphPosition++] = new SvgPositionedGlyph(glyphIndex, 0, originY, scaledAdvance);
                mappedSourceOffset += sourceLength;
                scalarIndex++;
                bool sourceNodeEnds = false;
                while (mappedNodeIndex < nodeBoundaries.Length && nodeBoundaries[mappedNodeIndex].Offset <= mappedSourceOffset)
                { sourceNodeEnds = true; mappedNodeIndex++; }
                if (fragments is not null && (sourceNodeEnds || mappedSourceOffset >= sourceEnd || deviceMetrics is { UsesTextLengthSpacing: true }))
                {
                    fragments.Add(new(fragmentStart, glyphPosition, 0));
                    fragmentStart = glyphPosition;
                }
            }
            int positionedGlyphCount = glyphPosition;
            if (positionedGlyphCount == 1 && deviceMetrics is { UsesTextLengthSpacing: true } spacingMetrics)
            {
                SvgGlyphDeviceMetrics singleMetrics = spacingMetrics with { UsesTextLengthSpacing = false };
                SvgGlyphFontStyle only = glyphFonts.Length == 0 ? new(font, fontSize) : glyphFonts[0];
                double naturalAdvance = singleMetrics.IsTextPath
                    ? singleMetrics.GetAdvance(firstDesignAdvance, 0, only.Font.UnitsPerEm, only.FontSize,
                        letterSpacingSpans.IsEmpty ? letterSpacing : letterSpacingSpans[0].LetterSpacing, firstWordGap)
                    : singleMetrics.GetAdvance(firstDesignAdvance, only.Font.UnitsPerEm, only.FontSize) + firstWordGap;
                if (!double.IsFinite(naturalAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[0] = glyphs[0] with { Advance = naturalAdvance };
                deviceMetrics = singleMetrics;
            }
            double x = originX;
            double fontFragmentOrigin = originX;
            int spacingSpanIndex = 0;
            int positionBoundaryIndex = 0;
            int sourceOffset = 0;
            int fragmentIndex = 0;
            int kerningRunEnd = 0, kerningBoundaryIndex = 0, nodeBoundaryIndex = 0;
            double decorationOrigin = originX;
            float sourceCssPosition = 0;
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
                int glyphSourceOffset = sourceOffset;
                sourceOffset += sourceLengths is null ? char.IsSurrogatePair(text, sourceOffset) ? 2 : 1 : sourceLengths[index];
                while (positionBoundaryIndex < positionAdjustments.Length && positionAdjustments[positionBoundaryIndex].Start < sourceOffset) positionBoundaryIndex++;
                bool chunkEnds = index + 1 == positionedGlyphCount || positionBoundaryIndex < positionAdjustments.Length && positionAdjustments[positionBoundaryIndex].Start == sourceOffset && positionAdjustments[positionBoundaryIndex].HasAbsolutePosition;
                bool nodeEnds = false;
                while (nodeBoundaryIndex < nodeBoundaries.Length && nodeBoundaries[nodeBoundaryIndex].Offset <= sourceOffset)
                { nodeEnds = true; nodeBoundaryIndex++; }
                bool sameFont = glyphFonts.Length == 0 || index + 1 < positionedGlyphCount && glyphFonts[index] == glyphFonts[index + 1];
                bool sameSpacing = letterSpacingSpans.IsEmpty || spacingSpanIndex + 1 == letterSpacingSpans.Length
                    || letterSpacingSpans[spacingSpanIndex].End != sourceOffset
                    || letterSpacingSpans[spacingSpanIndex].WordSpacing == letterSpacingSpans[spacingSpanIndex + 1].WordSpacing
                        && letterSpacingSpans[spacingSpanIndex].LetterSpacing == letterSpacingSpans[spacingSpanIndex + 1].LetterSpacing;
                bool sameDecoration = glyphPaints.Length == 0 || index + 1 < positionedGlyphCount
                    && ReferenceEquals(glyphPaints[index].Decoration, glyphPaints[index + 1].Decoration);
                bool samePaint = glyphPaints.Length == 0 || index + 1 < positionedGlyphCount && glyphPaints[index] == glyphPaints[index + 1];
                int pairAdjustment = 0;
                if (!chunkEnds && sameFont && sameSpacing)
                {
                    if (font.Metrics.Gpos is { HasFilteredPairs: true })
                    {
                        if (index >= kerningRunEnd)
                        {
                            // Find each contiguous shaped run once. A filtered
                            // partner must not cross a font/size or absolute reset.
                            while (kerningBoundaryIndex < positionAdjustments.Length
                                && (positionAdjustments[kerningBoundaryIndex].Start < sourceOffset || !positionAdjustments[kerningBoundaryIndex].HasAbsolutePosition))
                            {
                                cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                                kerningBoundaryIndex++;
                            }
                            int absoluteEnd = kerningBoundaryIndex < positionAdjustments.Length ? positionAdjustments[kerningBoundaryIndex].Start : text.Length;
                            kerningRunEnd = index + 1;
                            int runSourceEnd = sourceOffset;
                            int runSpacingIndex = spacingSpanIndex;
                            SvgTextLetterSpacingSpan runSpacing = letterSpacingSpans.IsEmpty ? default : letterSpacingSpans[runSpacingIndex];
                            while (kerningRunEnd < positionedGlyphCount && runSourceEnd < absoluteEnd
                                && (glyphFonts.Length == 0 || glyphFonts[kerningRunEnd] == glyphFonts[index]))
                            {
                                if (!letterSpacingSpans.IsEmpty)
                                {
                                    while (runSpacingIndex + 1 < letterSpacingSpans.Length && runSourceEnd >= letterSpacingSpans[runSpacingIndex].End)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                                        runSpacingIndex++;
                                    }
                                    SvgTextLetterSpacingSpan following = letterSpacingSpans[runSpacingIndex];
                                    if (following.LetterSpacing != runSpacing.LetterSpacing || following.WordSpacing != runSpacing.WordSpacing) break;
                                }
                                cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                                runSourceEnd += sourceLengths is null ? char.IsSurrogatePair(text, runSourceEnd) ? 2 : 1 : sourceLengths[kerningRunEnd];
                                kerningRunEnd++;
                            }
                        }
                        pairAdjustment = OpenTypeFont.GetPairAdjustment(font.Bytes, glyphs.AsSpan(index, kerningRunEnd - index), font.Metrics, workCounter, cancellationToken);
                    }
                    else pairAdjustment = OpenTypeFont.GetPairAdjustment(font.Bytes, glyph.GlyphIndex, glyphs[index + 1].GlyphIndex, font.Metrics, workCounter, cancellationToken);
                }
                double kerning = pairAdjustment * scale;
                if (pairAdjustment != 0 && deviceMetrics is { UsesCharacterAdvances: false } scalarMetrics)
                {
                    float deviceSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(fontSize * scalarMetrics.Scale);
                    long fontScale = (long)Math.Clamp((double)(deviceSize * 65536f), 0, int.MaxValue);
                    long multiplier = (fontScale << 16) / font.UnitsPerEm;
                    kerning = (double)(((Int128)pairAdjustment * multiplier + 32768) >> 16) / (65536 * scalarMetrics.Scale);
                }
                if (pairAdjustment != 0 && deviceMetrics is { UsesCharacterAdvances: true } pathMetrics)
                {
                    ushort advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyph.GlyphIndex, font.Metrics, workCounter, cancellationToken);
                    glyph = glyph with { Advance = pathMetrics.IsTextPath
                        ? pathMetrics.GetAdvance(advance, pairAdjustment, font.UnitsPerEm, fontSize, currentLetterSpacing,
                            GetWordGap(Rune.GetRuneAt(text, glyphSourceOffset), advance, wordSpacing, letterSpacingSpans, spacingSpanIndex))
                        : pathMetrics.GetAdvance(advance, pairAdjustment, font.UnitsPerEm, fontSize) };
                    kerning = 0;
                }
                double nextX = chunkEnds || deviceMetrics is { IsTextPath: true } ? x + glyph.Advance : x + glyph.Advance + kerning + currentLetterSpacing;
                if (fragments is not null && index + 1 == fragments[fragmentIndex].End)
                {
                    float sourceScale = (float)(deviceMetrics?.Scale ?? 1);
                    double sourceWidth = nextX - decorationOrigin + (chunkEnds && deviceMetrics is not { IsTextPath: true } ? currentLetterSpacing : 0);
                    fragments[fragmentIndex] = fragments[fragmentIndex] with
                    {
                        Width = nextX - decorationOrigin,
                        SourceWidth = MathF.Ceiling((float)(sourceWidth * sourceScale) * 64) / 64,
                        SourceOrigin = new(sourceCssPosition, 0)
                    };
                    sourceCssPosition += fragments[fragmentIndex].SourceWidth ?? 0;
                    fragmentIndex++;
                }
                if (!chunkEnds && (nodeEnds || !sameFont || !sameSpacing || !sameDecoration || !samePaint) && deviceMetrics is { } fragmentMetrics)
                {
                    // Native shaping fragments round their total inline width
                    // before the next face/size/spacing/decoration/paint fragment begins.
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
            int positionedFragment = 0;
            float sourceScaleFactor = (float)(deviceMetrics?.Scale ?? 1);
            float sourceDx = 0, sourceDy = 0;
            float sourceShiftX = (float)originX * sourceScaleFactor, sourceShiftY = (float)originY * sourceScaleFactor;
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
                int firstFragment = positionedFragment;
                double anchorOrigin = 0, left = double.PositiveInfinity, right = double.NegativeInfinity;
                for (int index = chunkStart; index < chunkEnd; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    workCounter?.Consume(cancellationToken);
                    double localX = glyphs[index].X - nominalOrigin;
                    double positionedX = deviceMetrics is SvgGlyphDeviceMetrics positionMetrics
                        ? nominalOrigin + chunkShift + positionMetrics.RoundOffset(localX) : glyphs[index].X;
                    int sourceAdjustment = positionAdjustmentIndex;
                    ApplyPositionAdjustment(ref relativeX, ref relativeY, positionedX, originY, sourceOffset, positionAdjustments, ref positionAdjustmentIndex);
                    sourceOffset += sourceLengths is null ? char.IsSurrogatePair(text, sourceOffset) ? 2 : 1 : sourceLengths[index];
                    double adjustedX = positionedX + relativeX;
                    double advanceEdge = adjustedX + glyphs[index].Advance;
                    if (index == chunkStart) anchorOrigin = adjustedX;
                    left = Math.Min(left, deviceMetrics is { IsTextPath: true } ? adjustedX : Math.Min(adjustedX, advanceEdge));
                    right = Math.Max(right, deviceMetrics is { IsTextPath: true } ? advanceEdge : Math.Max(adjustedX, advanceEdge));
                    double paintedX = adjustedX;
                    if (deviceMetrics is { UsesCharacterAdvances: false } originMetrics)
                        paintedX = RoundPaintOrigin(nominalOrigin + chunkShift + relativeX, originMetrics.Scale) + originMetrics.RoundOffset(localX);
                    double adjustedY = glyphs[index].Y + relativeY;
                    if (!double.IsFinite(paintedX) || !double.IsFinite(adjustedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
                    if (fragments is not null && positionedFragment < fragments.Count && fragments[positionedFragment].Start == index)
                    {
                        SvgDevicePoint css = fragments[positionedFragment].SourceOrigin ?? default;
                        if (sourceAdjustment != positionAdjustmentIndex)
                        {
                            SvgTextPositionAdjustment adjustment = positionAdjustments[sourceAdjustment];
                            if (adjustment.X is double sourceX)
                            {
                                sourceDx = 0;
                                sourceShiftX = (float)sourceX * sourceScaleFactor - (float)css.X;
                            }
                            if (adjustment.Y is double sourceY)
                            {
                                sourceDy = 0;
                                sourceShiftY = (float)sourceY * sourceScaleFactor;
                            }
                            sourceDx += (float)adjustment.Dx;
                            sourceDy += (float)adjustment.Dy;
                        }
                        fragments[positionedFragment] = fragments[positionedFragment] with
                        { SourceOrigin = new((float)css.X + sourceDx * sourceScaleFactor + sourceShiftX,
                            sourceDy * sourceScaleFactor + sourceShiftY) };
                        positionedFragment++;
                    }
                    glyphs[index] = glyphs[index] with { X = paintedX, Y = adjustedY };
                }
                double width = right - left;
                double chunkWidth = deviceMetrics is SvgGlyphDeviceMetrics widthMetrics ? widthMetrics.RoundWidth(width) : width;
                double anchorOffset = anchorOrigin - left + (anchor switch
                {
                    SvgTextAnchor.Start => 0,
                    SvgTextAnchor.Middle => -chunkWidth / 2,
                    SvgTextAnchor.End => -chunkWidth,
                    _ => throw new ArgumentOutOfRangeException(nameof(anchor))
                });
                float sourceAnchorOffset = 0;
                if (fragments is not null && firstFragment < positionedFragment)
                {
                    float sourceMinimum = float.PositiveInfinity, sourceMaximum = float.NegativeInfinity;
                    float sourceScale = (float)(deviceMetrics?.Scale ?? 1);
                    for (int index = firstFragment; index < positionedFragment; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                        SvgTextPaintFragment fragment = fragments[index];
                        if (fragment.SourceOrigin is not {} source) continue;
                        float sourceWidth = fragment.SourceWidth ?? MathF.Ceiling((float)(fragment.Width * sourceScale) * 64) / 64;
                        sourceMinimum = MathF.Min(sourceMinimum, (float)source.X);
                        sourceMaximum = MathF.Max(sourceMaximum, (float)source.X + sourceWidth);
                    }
                    if (fragments[firstFragment].SourceOrigin is {} sourceOrigin)
                        sourceAnchorOffset = (float)sourceOrigin.X - (anchor switch
                        {
                            SvgTextAnchor.Start => sourceMinimum,
                            SvgTextAnchor.Middle => (sourceMinimum + sourceMaximum) / 2,
                            SvgTextAnchor.End => sourceMaximum,
                            _ => throw new ArgumentOutOfRangeException(nameof(anchor))
                        });
                }
                if (deviceMetrics is { UsesCharacterAdvances: false } anchorMetrics) anchorOffset = RoundPaintOrigin(anchorOffset, anchorMetrics.Scale);
                if (!double.IsFinite(anchorOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
                if (anchorOffset != 0)
                    for (int index = chunkStart; index < chunkEnd; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                        double anchoredX = glyphs[index].X + anchorOffset;
                        if (!double.IsFinite(anchoredX)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text anchor exceeds the admitted numeric range.");
                        glyphs[index] = glyphs[index] with { X = anchoredX };
                    }
                if (fragments is not null && sourceAnchorOffset != 0)
                    for (int index = firstFragment; index < positionedFragment; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                        SvgTextPaintFragment fragment = fragments[index];
                        if (fragment.SourceOrigin is {} source)
                            fragments[index] = fragment with { SourceOrigin = source with { X = (float)source.X + sourceAnchorOffset } };
                    }
                chunkStart = chunkEnd;
                chunkShift += chunkWidth - width;
            }
            OwnedParserArray<SvgTextPaintFragment> retainedFragments = fragments is null ? new([], null) : fragments.Detach();
            fragmentReservation = retainedFragments.Reservation;
            SvgPositionedGlyphStorage result = new(glyphs, positionedGlyphCount, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, glyphRotations, rotationReservation, retainedFragments.Items, fragmentReservation)
            { NaturalWidth = deviceMetrics is { } naturalMetrics ? naturalMetrics.RoundWidth(x - originX) : x - originX };
            paintReservation = null;
            fontReservation = null;
            rotationReservation = null;
            fragmentReservation = null;
            return (result, deviceMetrics);
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

        // Fragment origins and chunk-anchor shifts truncate independently;
        // combining them changes half-grid middle anchoring. Ordinary offsets
        // within a shaped fragment retain scalar precision.
        static double RoundPaintOrigin(double origin, double scale)
            => MathF.Truncate((float)(origin * scale) * 64) / (scale * 64);

        static double GetLetterSpacing(int sourceOffset, double defaultLetterSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ref int spanIndex)
        {
            if (letterSpacingSpans.IsEmpty) return defaultLetterSpacing;
            while (sourceOffset >= letterSpacingSpans[spanIndex].End) spanIndex++;
            return letterSpacingSpans[spanIndex].LetterSpacing;
        }

        static double GetWordGap(Rune scalar, ushort advance, double wordSpacing, ReadOnlySpan<SvgTextLetterSpacingSpan> spans, int spanIndex)
            => advance != 0 && scalar.Value is 0x20 or 0xa0 or 0x1361 or 0x10100 or 0x10101 or 0x1039f or 0x1091f
                ? spans.IsEmpty ? wordSpacing : spans[spanIndex].WordSpacing : 0;

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
    private readonly SvgTextPaintFragment[] _paintFragments;
    private RenderBudget.BudgetReservation? _fragmentReservation;

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation)
        : this(glyphs, count, reservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation)
        : this(glyphs, count, reservation, glyphPaints, paintReservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation, SvgGlyphFontStyle[] glyphFonts, RenderBudget.BudgetReservation? fontReservation)
        : this(glyphs, count, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation, SvgGlyphFontStyle[] glyphFonts, RenderBudget.BudgetReservation? fontReservation, double[] glyphRotations, RenderBudget.BudgetReservation? rotationReservation)
        : this(glyphs, count, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, glyphRotations, rotationReservation, [], null) { }

    internal SvgPositionedGlyphStorage(SvgPositionedGlyph[] glyphs, int count, RenderBudget.BudgetReservation? reservation, SvgTextGlyphPaint[] glyphPaints, RenderBudget.BudgetReservation? paintReservation, SvgGlyphFontStyle[] glyphFonts, RenderBudget.BudgetReservation? fontReservation, double[] glyphRotations, RenderBudget.BudgetReservation? rotationReservation, SvgTextPaintFragment[] paintFragments, RenderBudget.BudgetReservation? fragmentReservation)
    {
        _glyphs = glyphs;
        _count = count;
        _reservation = reservation;
        _glyphPaints = glyphPaints;
        _paintReservation = paintReservation;
        _glyphFonts = glyphFonts; _fontReservation = fontReservation;
        _glyphRotations = glyphRotations; _rotationReservation = rotationReservation;
        _paintFragments = paintFragments; _fragmentReservation = fragmentReservation;
    }

    internal ReadOnlySpan<SvgPositionedGlyph> Glyphs => _glyphs.AsSpan(0, _count);
    internal double NaturalWidth { get; init; }
    internal ReadOnlySpan<double> GlyphRotations => _glyphRotations.Length == 0 ? [] : _glyphRotations.AsSpan(0, _count);
    internal ReadOnlySpan<SvgTextPaintFragment> PaintFragments => _paintFragments;
    internal Span<SvgTextPaintFragment> MutablePaintFragments => _paintFragments;
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
