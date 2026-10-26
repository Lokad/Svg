// SVG fragment widths and cross-element shaping adapt Blink's native operation order.
// Copyright 2016, 2017, 2021 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
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
        var result = CreateStorageAndMetrics(font, text, fontSize, originX, originY, letterSpacing, wordSpacing, retainPaintFragments,
            letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, nodeBoundaries, anchor, deviceMetrics, null, budget, sourceElementId, workCounter, cancellationToken);
        return (result.Storage, result.Metrics);
    }

    internal static (SvgPositionedGlyphStorage Storage, SvgGlyphDeviceMetrics? Metrics, SvgTextFittingResult Fitting) CreateStorageAndMetrics(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, double wordSpacing, bool retainPaintFragments, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, ReadOnlySpan<SvgTextNodeBoundary> nodeBoundaries, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, SvgTextFitting? fitting, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
        => CreateStorageAndMetrics(font, text, fontSize, originX, originY, letterSpacing, wordSpacing, retainPaintFragments,
            letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, nodeBoundaries, [], anchor, deviceMetrics, fitting, budget, sourceElementId, workCounter, cancellationToken);

    internal static (SvgPositionedGlyphStorage Storage, SvgGlyphDeviceMetrics? Metrics, SvgTextFittingResult Fitting) CreateStorageAndMetrics(SvgPreparedFont font, string text, double fontSize, double originX, double originY, double letterSpacing, double wordSpacing, bool retainPaintFragments, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextResolvedFontSpan> fontSpans, ReadOnlySpan<SvgTextNodeBoundary> nodeBoundaries, ReadOnlySpan<SvgTextPlacementRange> placementRanges, SvgTextAnchor anchor, SvgGlyphDeviceMetrics? deviceMetrics, SvgTextFitting? fitting, RenderBudget budget, string sourceElementId, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
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
        int placementEnd = 0;
        foreach (SvgTextPlacementRange range in placementRanges)
        {
            if (workCounter is null) cancellationToken.ThrowIfCancellationRequested(); else workCounter.Consume(cancellationToken);
            if (range.Start != placementEnd || range.End <= range.Start || range.End > text.Length
                || range.End < text.Length && char.IsSurrogatePair(text, range.End - 1)
                || range.Path is not null && range.AfterPathOrigin is not null
                || range.AfterPathOrigin is {} point && (!double.IsFinite(point.X) || !double.IsFinite(point.Y)))
                throw new ArgumentException("Text placement ranges must partition normalized scalar boundaries.", nameof(placementRanges));
            placementEnd = range.End;
        }
        if (!placementRanges.IsEmpty && placementEnd != text.Length)
            throw new ArgumentException("Text placement ranges must cover normalized text.", nameof(placementRanges));
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
        if (glyphCount == 0) return (new SvgPositionedGlyphStorage([], 0, null), deviceMetrics, new(false, 1));
        RenderBudget.BudgetReservation reservation = budget.Reserve(checked(32L + glyphCount * 32L), SvgMemoryCategory.Geometry);
        RenderBudget.BudgetReservation? clusterReservation = null;
        RenderBudget.BudgetReservation? paintReservation = null;
        RenderBudget.BudgetReservation? fontReservation = null;
        RenderBudget.BudgetReservation? rotationReservation = null;
        RenderBudget.BudgetReservation? fragmentReservation = null;
        try
        {
            bool needsSourceItems = retainPaintFragments || deviceMetrics is not null
                && (!positionAdjustments.IsEmpty || !nodeBoundaries.IsEmpty || fitting is not null);
            using BudgetedParserList<SvgTextPaintFragment>? fragments = needsSourceItems ? new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken) : null;
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
            int fragmentStart = 0, fragmentSourceStart = 0;
            int mappedNodeIndex = 0, shapingNodeIndex = 0;
            int mappedSourceOffset = 0;
            int mappedSpacingIndex = 0;
            int mappedPositionIndex = 0, ligaturePositionIndex = 0;
            int mappedPaintIndex = 0;
            int mappedFontIndex = 0, mappedPlacementIndex = 0;
            int scalarIndex = 0;
            ushort firstDesignAdvance = 0;
            double firstWordGap = 0;
            var mappedScalars = text.EnumerateRunes();
            while (mappedScalars.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                workCounter?.Consume(cancellationToken);
                Rune scalar = mappedScalars.Current;
                SvgGlyphDeviceMetrics? characterMetrics = deviceMetrics;
                if (!placementRanges.IsEmpty && deviceMetrics is {} sourceMetrics)
                {
                    while (placementRanges[mappedPlacementIndex].End <= mappedSourceOffset) mappedPlacementIndex++;
                    characterMetrics = sourceMetrics with { IsTextPath = placementRanges[mappedPlacementIndex].Path is not null };
                }

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
                while (ligaturePositionIndex < positionAdjustments.Length
                    && (positionAdjustments[ligaturePositionIndex].Start <= mappedSourceOffset || !positionAdjustments[ligaturePositionIndex].HasAbsolutePosition))
                {
                    cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                    ligaturePositionIndex++;
                }
                if (ligaturePositionIndex < positionAdjustments.Length)
                    sourceEnd = Math.Min(sourceEnd, positionAdjustments[ligaturePositionIndex].Start);
                // Blink compares the first SVG item's scaled font with a
                // following element's unscaled style font. Element changes
                // therefore stop shaping when the screen font size differs.
                bool scaledElementFontsDiffer = deviceMetrics is {} elementMetrics
                    && (float)fontSize * (float)elementMetrics.Scale != (float)fontSize;
                while (shapingNodeIndex < nodeBoundaries.Length
                    && (nodeBoundaries[shapingNodeIndex].Offset <= mappedSourceOffset || !nodeBoundaries[shapingNodeIndex].ChangesElement))
                {
                    cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                    shapingNodeIndex++;
                }
                if (scaledElementFontsDiffer && shapingNodeIndex < nodeBoundaries.Length)
                    sourceEnd = Math.Min(sourceEnd, nodeBoundaries[shapingNodeIndex].Offset);
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
                double scaledAdvance = characterMetrics is SvgGlyphDeviceMetrics metrics
                    ? metrics.IsTextPath ? metrics.GetAdvance(advance, 0, font.UnitsPerEm, fontSize, spacing, wordGap)
                        : metrics.GetAdvance(advance, font.UnitsPerEm, fontSize) + wordGap
                    : advance * scale + wordGap;
                if (glyphPosition == 0) { firstDesignAdvance = advance; firstWordGap = wordGap; }
                if (!double.IsFinite(scaledAdvance)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
                glyphs[glyphPosition++] = new SvgPositionedGlyph(glyphIndex, 0, originY, scaledAdvance);
                mappedSourceOffset += sourceLength;
                scalarIndex++;
                while (mappedNodeIndex < nodeBoundaries.Length && nodeBoundaries[mappedNodeIndex].Offset <= mappedSourceOffset)
                {
                    cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                    int end = nodeBoundaries[mappedNodeIndex++].Offset;
                    if (fragments is not null && end > fragmentSourceStart)
                    {
                        fragments.Add(new(fragmentStart, glyphPosition, 0) { SourceStart = fragmentSourceStart, SourceEnd = end });
                        fragmentStart = glyphPosition; fragmentSourceStart = end;
                    }
                }
                bool positionedFragmentEnds = mappedPositionIndex < positionAdjustments.Length
                    && positionAdjustments[mappedPositionIndex].Start <= mappedSourceOffset;
                if (fragments is not null && fragmentStart < glyphPosition
                    && (positionedFragmentEnds || mappedSourceOffset >= sourceEnd || characterMetrics is { UsesCharacterAdvances: true }))
                {
                    int end = mappedNodeIndex < nodeBoundaries.Length ? Math.Min(mappedSourceOffset, nodeBoundaries[mappedNodeIndex].Offset) : mappedSourceOffset;
                    fragments.Add(new(fragmentStart, glyphPosition, 0) { SourceStart = fragmentSourceStart, SourceEnd = end });
                    fragmentStart = glyphPosition; fragmentSourceStart = end;
                }
            }
            if (fragments is not null && fragmentSourceStart < text.Length)
                fragments.Add(new(fragmentStart, glyphPosition, 0) { SourceStart = fragmentSourceStart, SourceEnd = text.Length });
            int positionedGlyphCount = glyphPosition;
            if (positionedGlyphCount == 1 && fragments is not { Count: > 1 } && deviceMetrics is { UsesTextLengthSpacing: true } spacingMetrics)
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
            int kerningRunEnd = 0, kerningBoundaryIndex = 0, nodeBoundaryIndex = 0, kerningNodeIndex = 0;
            int glyphPlacementIndex = 0;
            double decorationOrigin = originX;
            float sourceCssPosition = 0;
            for (int index = 0; index < positionedGlyphCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgPositionedGlyph glyph = glyphs[index];
                SvgGlyphDeviceMetrics? glyphMetrics = deviceMetrics;
                if (!placementRanges.IsEmpty && deviceMetrics is {} sourceMetrics)
                {
                    while (placementRanges[glyphPlacementIndex].End <= sourceOffset) glyphPlacementIndex++;
                    glyphMetrics = sourceMetrics with { IsTextPath = placementRanges[glyphPlacementIndex].Path is not null };
                }

                if (fragments is not null && index == fragments[fragmentIndex].Start) decorationOrigin = x;
                if (glyphFonts.Length != 0)
                {
                    font = glyphFonts[index].Font; fontSize = glyphFonts[index].FontSize; scale = fontSize / font.UnitsPerEm;
                }
                double currentLetterSpacing = GetLetterSpacing(sourceOffset, letterSpacing, letterSpacingSpans, ref spacingSpanIndex);
                int glyphSourceOffset = sourceOffset;
                sourceOffset += sourceLengths is null ? char.IsSurrogatePair(text, sourceOffset) ? 2 : 1 : sourceLengths[index];
                bool positionedFragmentEnds = false;
                while (positionBoundaryIndex < positionAdjustments.Length && positionAdjustments[positionBoundaryIndex].Start < sourceOffset)
                {
                    cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                    positionedFragmentEnds |= positionAdjustments[positionBoundaryIndex].Start > glyphSourceOffset;
                    positionBoundaryIndex++;
                }
                positionedFragmentEnds |= positionBoundaryIndex < positionAdjustments.Length
                    && positionAdjustments[positionBoundaryIndex].Start == sourceOffset;
                bool chunkEnds = index + 1 == positionedGlyphCount || positionBoundaryIndex < positionAdjustments.Length && positionAdjustments[positionBoundaryIndex].Start == sourceOffset && positionAdjustments[positionBoundaryIndex].HasAbsolutePosition;
                bool nodeEnds = false, elementEnds = false;
                while (nodeBoundaryIndex < nodeBoundaries.Length && nodeBoundaries[nodeBoundaryIndex].Offset <= sourceOffset)
                {
                    nodeEnds = true;
                    elementEnds |= nodeBoundaries[nodeBoundaryIndex].ChangesElement;
                    nodeBoundaryIndex++;
                }
                bool scaledElementFontsDiffer = glyphMetrics is {} elementMetrics
                    && (float)fontSize * (float)elementMetrics.Scale != (float)fontSize;
                bool sameFont = glyphFonts.Length == 0 || index + 1 < positionedGlyphCount && glyphFonts[index] == glyphFonts[index + 1];
                bool sameSpacing = letterSpacingSpans.IsEmpty || spacingSpanIndex + 1 == letterSpacingSpans.Length
                    || letterSpacingSpans[spacingSpanIndex].End != sourceOffset
                    || letterSpacingSpans[spacingSpanIndex].WordSpacing == letterSpacingSpans[spacingSpanIndex + 1].WordSpacing
                        && letterSpacingSpans[spacingSpanIndex].LetterSpacing == letterSpacingSpans[spacingSpanIndex + 1].LetterSpacing;
                bool sameDecoration = glyphPaints.Length == 0 || index + 1 < positionedGlyphCount
                    && ReferenceEquals(glyphPaints[index].Decoration, glyphPaints[index + 1].Decoration);
                bool samePaint = glyphPaints.Length == 0 || index + 1 < positionedGlyphCount && glyphPaints[index] == glyphPaints[index + 1];
                int pairAdjustment = 0;
                if (!chunkEnds && !(elementEnds && scaledElementFontsDiffer) && sameFont && sameSpacing)
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
                            while (kerningNodeIndex < nodeBoundaries.Length
                                && (nodeBoundaries[kerningNodeIndex].Offset <= sourceOffset || !nodeBoundaries[kerningNodeIndex].ChangesElement))
                            {
                                cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                                kerningNodeIndex++;
                            }
                            if (scaledElementFontsDiffer && kerningNodeIndex < nodeBoundaries.Length)
                                absoluteEnd = Math.Min(absoluteEnd, nodeBoundaries[kerningNodeIndex].Offset);
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
                if (pairAdjustment != 0 && glyphMetrics is { UsesCharacterAdvances: false } scalarMetrics)
                {
                    float deviceSize = SvgGlyphDeviceMetrics.GetEffectiveFontSize(fontSize * scalarMetrics.Scale);
                    long fontScale = (long)Math.Clamp((double)(deviceSize * 65536f), 0, int.MaxValue);
                    long multiplier = (fontScale << 16) / font.UnitsPerEm;
                    kerning = (double)(((Int128)pairAdjustment * multiplier + 32768) >> 16) / (65536 * scalarMetrics.Scale);
                }
                if (pairAdjustment != 0 && glyphMetrics is { UsesCharacterAdvances: true } pathMetrics)
                {
                    ushort advance = OpenTypeFont.GetAdvanceWidth(font.Bytes, glyph.GlyphIndex, font.Metrics, workCounter, cancellationToken);
                    glyph = glyph with { Advance = pathMetrics.IsTextPath
                        ? pathMetrics.GetAdvance(advance, pairAdjustment, font.UnitsPerEm, fontSize, currentLetterSpacing,
                            GetWordGap(Rune.GetRuneAt(text, glyphSourceOffset), advance, wordSpacing, letterSpacingSpans, spacingSpanIndex))
                        : pathMetrics.GetAdvance(advance, pairAdjustment, font.UnitsPerEm, fontSize) };
                    kerning = 0;
                }
                double nextX = chunkEnds || glyphMetrics is { IsTextPath: true } ? x + glyph.Advance : x + glyph.Advance + kerning + currentLetterSpacing;
                while (fragments is not null && fragmentIndex < fragments.Count && index + 1 == fragments[fragmentIndex].End)
                {
                    float sourceScale = (float)(glyphMetrics?.Scale ?? 1);
                    bool empty = fragments[fragmentIndex].Start == fragments[fragmentIndex].End;
                    double sourceWidth = empty ? 0 : nextX - decorationOrigin + (chunkEnds && glyphMetrics is not { IsTextPath: true } ? currentLetterSpacing : 0);
                    fragments[fragmentIndex] = fragments[fragmentIndex] with
                    {
                        Width = empty ? 0 : nextX - decorationOrigin,
                        SourceWidth = MathF.Ceiling((float)(sourceWidth * sourceScale) * 64) / 64,
                        SourceOrigin = new(sourceCssPosition, 0)
                    };
                    sourceCssPosition += fragments[fragmentIndex].SourceWidth ?? 0;
                    fragmentIndex++;
                    decorationOrigin = nextX;
                }
                if (!chunkEnds && (nodeEnds || positionedFragmentEnds || !sameFont || !sameSpacing || !sameDecoration || !samePaint) && glyphMetrics is { } fragmentMetrics)
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
            bool positionedItems = fragments is not null && deviceMetrics is not null;
            float fittedScale = positionedItems && fragments is not null && deviceMetrics is {} itemMetrics
                ? PositionFragments(glyphs, fragments, text, positionAdjustments, glyphRotations, placementRanges, anchor, itemMetrics, fitting, originX, originY, workCounter, cancellationToken) : 1;
            if (!positionedItems)
            {
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
                    ApplyPositionAdjustment(ref relativeX, ref relativeY, positionedX, originY, sourceOffset, positionAdjustments, ref positionAdjustmentIndex, workCounter, cancellationToken);
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
                        for (int sourceIndex = sourceAdjustment; sourceIndex < positionAdjustmentIndex; sourceIndex++)
                        {
                            cancellationToken.ThrowIfCancellationRequested(); workCounter?.Consume(cancellationToken);
                            SvgTextPositionAdjustment adjustment = positionAdjustments[sourceIndex];
                            if (adjustment.X is double sourceX)
                            {
                                sourceDx = 0;
                                sourceShiftX = (float)sourceX * sourceScaleFactor - (float)css.X;
                            }
                            if (adjustment.AppliedY is double sourceY)
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
            }
            OwnedParserArray<SvgTextPaintFragment> retainedFragments = !retainPaintFragments || fragments is null ? new([], null) : fragments.Detach();
            fragmentReservation = retainedFragments.Reservation;
            SvgPositionedGlyphStorage result = new(glyphs, positionedGlyphCount, reservation, glyphPaints, paintReservation, glyphFonts, fontReservation, glyphRotations, rotationReservation, retainedFragments.Items, fragmentReservation)
            { NaturalWidth = deviceMetrics is { } naturalMetrics ? naturalMetrics.RoundWidth(x - originX) : x - originX };
            paintReservation = null;
            fontReservation = null;
            rotationReservation = null;
            fragmentReservation = null;
            return (result, deviceMetrics, new(positionedItems, fittedScale));
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

        // Paint quantizes the final anchored source rectangle. Ordinary offsets
        // within a shaped fragment retain scalar precision.
        // Resolve every addressable source character in native DX/DY, fitting,
        // X/Y and anchor order. Glyphless node items share the existing fragment owner.
        static float PositionFragments(SvgPositionedGlyph[] glyphs, BudgetedParserList<SvgTextPaintFragment> fragments,
            string text, ReadOnlySpan<SvgTextPositionAdjustment> adjustments, double[] rotations,
            ReadOnlySpan<SvgTextPlacementRange> placementRanges, SvgTextAnchor anchor, SvgGlyphDeviceMetrics metrics, SvgTextFitting? fitting, double originX, double originY,
            ParserWorkCounter? work, CancellationToken token)
        {
            float screen = (float)metrics.Scale;
            double baselineY = originY, relativeY = 0;
            float minimum = 0, natural = 0;
            if (fitting is not null)
            {
                minimum = float.PositiveInfinity;
                float maximum = float.NegativeInfinity, rangeDx = 0;
                int rangeAdjustmentIndex = 0;
                for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
                {
                    SvgTextPaintFragment fragment = fragments[fragmentIndex];
                    SvgDevicePoint css = fragment.SourceOrigin ?? default;
                    for (int source = fragment.SourceStart; source < fragment.SourceEnd; source += Rune.GetRuneAt(text, source).Utf16SequenceLength)
                    {
                        token.ThrowIfCancellationRequested(); work?.Consume(token);
                        if (rangeAdjustmentIndex < adjustments.Length && adjustments[rangeAdjustmentIndex].Start == source)
                        {
                            SvgTextPositionAdjustment adjustment = adjustments[rangeAdjustmentIndex++];
                            if (adjustment.X is not null) rangeDx = 0;
                            rangeDx += (float)adjustment.Dx;
                        }
                        float position = (float)css.X + rangeDx * screen;
                        float width = source == fragment.SourceStart ? fragment.SourceWidth ?? 0 : 0;
                        minimum = MathF.Min(minimum, position);
                        maximum = MathF.Max(maximum, position + width);
                    }
                }
                natural = maximum - minimum;
            }
            float stretch = fitting is { Mode: SvgTextLengthAdjust.SpacingAndGlyphs } fit && natural > 0
                ? (float)fit.Length * screen / natural : 1;
            float spacing = fitting is { Mode: SvgTextLengthAdjust.Spacing } spaced && fragments.Count > 1
                ? ((float)spaced.Length * screen - natural) / (fragments.Count - 1) : 0;
            if (!float.IsFinite(stretch) || !float.IsFinite(spacing))
                throw new SvgException(SvgErrorCode.LimitExceeded, "Fitted text coordinates exceed the numeric range.");
            float dx = 0, dy = 0;
            int adjustmentIndex = 0;
            float shiftX = (float)originX * screen, shiftY = (float)originY * screen;
            float spacingShift = 0, chunkMinimum = float.PositiveInfinity, chunkMaximum = float.NegativeInfinity, chunkOrigin = 0;
            int chunkStart = 0;
            int placementIndex = 0, activePlacement = -1;
            SvgTextAnchor chunkAnchor = anchor;
            SvgGlyphDeviceMetrics chunkMetrics = metrics;
            bool afterPath = false, transferReady = false;
            float transferX = 0, transferY = 0;

            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                SvgTextPaintFragment fragment = fragments[fragmentIndex];
                SvgDevicePoint css = fragment.SourceOrigin ?? default;
                if (!placementRanges.IsEmpty)
                    while (placementRanges[placementIndex].End <= fragment.SourceStart) placementIndex++;
                bool changesPlacement = !placementRanges.IsEmpty && placementIndex != activePlacement;

                bool startsChunk = adjustmentIndex < adjustments.Length && adjustments[adjustmentIndex].Start == fragment.SourceStart
                    && adjustments[adjustmentIndex].HasAbsolutePosition;
                if (fragmentIndex != chunkStart && (startsChunk || changesPlacement))
                {
                    ApplyChunk(glyphs, fragments, chunkStart, fragmentIndex, chunkMinimum, chunkMaximum, chunkOrigin,
                        chunkAnchor, chunkMetrics, fitting, stretch, work, token);
                    chunkStart = fragmentIndex; chunkMinimum = float.PositiveInfinity; chunkMaximum = float.NegativeInfinity;
                }
                if (changesPlacement)
                {
                    SvgTextPlacementRange range = placementRanges[placementIndex];
                    activePlacement = placementIndex;
                    chunkMetrics = metrics with { IsTextPath = range.Path is not null };
                    chunkAnchor = range.Path?.Anchor ?? (range.AfterPathOrigin is null ? anchor : SvgTextAnchor.Unanchored);
                    afterPath = range.AfterPathOrigin is not null;
                    transferReady = false;
                    if (range.Path is not null)
                    {
                        dx = 0; dy = 0; baselineY = 0; relativeY = 0; shiftY = 0;
                    }
                }
                if (startsChunk)
                {
                    chunkAnchor = placementRanges.IsEmpty ? anchor : placementRanges[placementIndex].Path?.Anchor ?? anchor;
                    afterPath = false;
                }

                for (int source = fragment.SourceStart; source < fragment.SourceEnd; source += Rune.GetRuneAt(text, source).Utf16SequenceLength)
                {
                    token.ThrowIfCancellationRequested(); work?.Consume(token);
                    SvgTextPositionAdjustment adjustment = default;
                    bool hasAdjustment = adjustmentIndex < adjustments.Length && adjustments[adjustmentIndex].Start == source;
                    if (hasAdjustment)
                    {
                        adjustment = adjustments[adjustmentIndex++];
                        if (adjustment.X is not null) dx = 0;
                        if (adjustment.AppliedY is double resetY) { dy = 0; baselineY = resetY; relativeY = 0; }
                        relativeY += adjustment.Dy;
                        dx += (float)adjustment.Dx; dy += (float)adjustment.Dy;
                    }
                    float originalX = (float)css.X + dx * screen;
                    float positionX = fitting is { Mode: SvgTextLengthAdjust.SpacingAndGlyphs } && natural > 0
                        ? minimum + (originalX - minimum) * stretch : originalX;
                    positionX += spacingShift;
                    float positionY = (float)css.Y + dy * screen;
                    if (adjustment.X is double absoluteX) shiftX = (float)absoluteX * screen - (float)css.X - (positionX - originalX);
                    else if (fragmentIndex == 0 && source == fragment.SourceStart)
                        shiftX = (float)originX * screen - (float)css.X - (positionX - originalX);
                    if (adjustment.AppliedY is double absoluteY) shiftY = (float)absoluteY * screen - (float)css.Y;
                    positionX += shiftX; positionY += shiftY;
                    if (adjustment.HasAbsolutePosition) afterPath = false;
                    if (afterPath)
                    {
                        if (!transferReady)
                        {
                            SvgDevicePoint endpoint = placementRanges[activePlacement].AfterPathOrigin
                                ?? throw new InvalidOperationException("A following source range requires its path endpoint.");
                            transferX = (float)endpoint.X * screen - positionX;
                            transferY = (float)endpoint.Y * screen - positionY;
                            transferReady = true;
                        }
                        positionX += transferX;
                        positionY += transferY;
                    }

                    float width = source == fragment.SourceStart ? (fragment.SourceWidth ?? 0) * stretch : 0;
                    if (source == fragment.SourceStart)
                    {
                        fragments[fragmentIndex] = fragment with { SourceOrigin = new(positionX, positionY), SourceWidth = width, Width = fragment.Width * stretch };
                        if (fragmentIndex == chunkStart) chunkOrigin = positionX;
                        for (int glyph = fragment.Start; glyph < fragment.End; glyph++)
                        {
                            token.ThrowIfCancellationRequested(); work?.Consume(token);
                            glyphs[glyph] = glyphs[glyph] with { Y = baselineY + relativeY + (afterPath ? transferY / (double)screen : 0) };
                            if (rotations.Length != 0) rotations[glyph] = adjustment.Rotation ?? 0;
                        }
                    }
                    chunkMinimum = MathF.Min(chunkMinimum, positionX);
                    chunkMaximum = MathF.Max(chunkMaximum, positionX + width);
                    if (source == fragment.SourceStart) spacingShift += spacing;
                }
            }
            ApplyChunk(glyphs, fragments, chunkStart, fragments.Count, chunkMinimum, chunkMaximum, chunkOrigin,
                chunkAnchor, chunkMetrics, fitting, stretch, work, token);
            return stretch;

            static void ApplyChunk(SvgPositionedGlyph[] glyphs, BudgetedParserList<SvgTextPaintFragment> fragments, int start, int end,
                float minimum, float maximum, float first, SvgTextAnchor anchor, SvgGlyphDeviceMetrics metrics,
                SvgTextFitting? fitting, float stretch, ParserWorkCounter? work, CancellationToken token)
            {
                float shift = anchor == SvgTextAnchor.Unanchored ? 0 : first - (anchor switch
                {
                    SvgTextAnchor.Start => minimum,
                    SvgTextAnchor.Middle => (minimum + maximum) / 2,
                    SvgTextAnchor.End => maximum,
                    _ => throw new ArgumentOutOfRangeException(nameof(anchor))
                });
                float screen = (float)metrics.Scale;
                for (int fragmentIndex = start; fragmentIndex < end; fragmentIndex++)
                {
                    token.ThrowIfCancellationRequested(); work?.Consume(token);
                    SvgTextPaintFragment fragment = fragments[fragmentIndex];
                    SvgDevicePoint origin = fragment.SourceOrigin ?? default;
                    float sourceX = (float)origin.X + shift;
                    fragments[fragmentIndex] = fragment with { SourceOrigin = new(sourceX, origin.Y) };
                    if (fragment.Start == fragment.End) continue;
                    double nominalStart = glyphs[fragment.Start].X;
                    double painted;
                    if (!metrics.IsTextPath && fitting is { Mode: SvgTextLengthAdjust.SpacingAndGlyphs })
                    {
                        float translation = sourceX - stretch * sourceX;
                        painted = (MathF.Truncate(sourceX * 64) / 64 * stretch + translation) / screen;
                    }
                    else if (!metrics.UsesCharacterAdvances)
                    {
                        // Quantize the final native rectangle. Keep the admitted
                        // inverse scale on each independent logical displacement.
                        float original = MathF.Truncate((float)origin.X * 64);
                        float anchored = MathF.Truncate(sourceX * 64);
                        painted = original / (metrics.Scale * 64) + (anchored - original) / (metrics.Scale * 64);
                    }
                    else painted = (double)sourceX / metrics.Scale;
                    for (int glyph = fragment.Start; glyph < fragment.End; glyph++)
                    {
                        token.ThrowIfCancellationRequested(); work?.Consume(token);
                        SvgPositionedGlyph old = glyphs[glyph];
                        double local = metrics.RoundOffset(old.X - nominalStart);
                        double x = painted + local * stretch, y = old.Y;
                        if (!double.IsFinite(x) || !double.IsFinite(y))
                            throw new SvgException(SvgErrorCode.LimitExceeded, "Text cursor coordinates exceed the numeric range.");
                        glyphs[glyph] = old with { X = x, Y = y, Advance = old.Advance * stretch };
                    }
                }
            }
        }

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

        static void ApplyPositionAdjustment(ref double x, ref double y, double nominalX, double originY, int sourceOffset, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ref int adjustmentIndex, ParserWorkCounter? work, CancellationToken token)
        {
            while (adjustmentIndex < positionAdjustments.Length && positionAdjustments[adjustmentIndex].Start <= sourceOffset)
            {
                token.ThrowIfCancellationRequested(); work?.Consume(token);
                SvgTextPositionAdjustment adjustment = positionAdjustments[adjustmentIndex++];
                if (adjustment.X is double absoluteX) x = absoluteX - nominalX;
                if (adjustment.AppliedY is double absoluteY) y = absoluteY - originY;
                x += adjustment.Dx;
                y += adjustment.Dy;
            }
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
