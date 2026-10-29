// Fitted fragment paint origins adapt Blink's SVG text painter.
// Copyright 2017, 2019 The Chromium Authors. See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using System.Text;

/// <summary>Builds one bounded simple horizontal text run from prepared caller-owned font data.</summary>
internal static class SvgTextRunBuilder
{
    internal static SvgTextRun Create(SvgTextStyle style, string text, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, [], snapshot, budget, cancellationToken);

    /// <summary>Builds one text run with normalized UTF-16 letter-spacing intervals retained by direct-text extraction.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, [], snapshot, budget, cancellationToken);

    /// <summary>Builds one text run with normalized letter-spacing intervals and scalar span offsets retained by direct-text extraction.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, null, snapshot, budget, cancellationToken);

    /// <summary>Builds a run using the already resolved output scale for platform glyph advances and SVG fragments.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, [], null, deviceMetrics, snapshot, budget, cancellationToken);

    /// <summary>Resolves paint during the existing character-to-glyph mapping while preserving one continuous layout and owning any varying glyph-paint array.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, paintSpans, [], defaultPaint, deviceMetrics, snapshot, budget, cancellationToken);

    /// <summary>Resolves normalized font intervals from the caller snapshot once, then shares their faces with continuous layout and glyph replay.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextFontSpan> fontSpans, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
        => Create(style, text, letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, [], defaultPaint, deviceMetrics, snapshot, budget, cancellationToken);

    /// <summary>Borrows normalized node boundaries with font/paint intervals for native fragment metrics; caller retains all input ownership.</summary>
    internal static SvgTextRun Create(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextFontSpan> fontSpans, ReadOnlySpan<SvgTextNodeBoundary> nodeBoundaries, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
    {
        try { return CreateCore(style, text, letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, nodeBoundaries, [], defaultPaint, deviceMetrics, snapshot, budget, cancellationToken); }
        catch (SvgException exception)
        {
            if (style.SourceContext is {} source) exception.AttachSource(source.Describe(string.Empty, string.Empty));
            throw;
        }
    }

    /// <summary>Borrows complete normalized source ranges for continuous shaping and placement; the caller retains input owners and the result owns its glyph storage.</summary>
    internal static SvgTextRun CreateShared(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextFontSpan> fontSpans, ReadOnlySpan<SvgTextNodeBoundary> nodeBoundaries, ReadOnlySpan<SvgTextPlacementRange> placementRanges, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
    {
        try { return CreateCore(style, text, letterSpacingSpans, positionAdjustments, paintSpans, fontSpans, nodeBoundaries, placementRanges, defaultPaint, deviceMetrics, snapshot, budget, cancellationToken); }
        catch (SvgException exception)
        {
            if (style.SourceContext is {} source) exception.AttachSource(source.Describe(string.Empty, string.Empty));
            throw;
        }
    }

    private static SvgTextRun CreateCore(SvgTextStyle style, string text, ReadOnlySpan<SvgTextLetterSpacingSpan> letterSpacingSpans, ReadOnlySpan<SvgTextPositionAdjustment> positionAdjustments, ReadOnlySpan<SvgTextPaintSpan> paintSpans, ReadOnlySpan<SvgTextFontSpan> fontSpans, ReadOnlySpan<SvgTextNodeBoundary> nodeBoundaries, ReadOnlySpan<SvgTextPlacementRange> placementRanges, SvgTextGlyphPaint? defaultPaint, SvgGlyphDeviceMetrics? deviceMetrics, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken cancellationToken)
    {
        if (style.TextLength is > 0 && style.LengthAdjust == SvgTextLengthAdjust.Spacing && deviceMetrics is {} metrics)
            deviceMetrics = metrics with { UsesTextLengthSpacing = true };
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(snapshot);
        using BudgetedParserList<SvgTextResolvedFontSpan> resolvedFonts = new(budget, SvgMemoryCategory.Parser, cancellationToken);
        SvgPreparedFont? firstRequestedFont = null;
        foreach (SvgTextFontSpan span in fontSpans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgFontFaceRequest request = SvgFontFaceRequest.FromCss(span.Font.Family ?? "serif", span.Font.Weight, span.Font.Italic, null, cancellationToken);
            SvgPreparedFont face = snapshot.GetFont(request, style.SourceElementId, cancellationToken);
            firstRequestedFont ??= face;
            if (snapshot.HasCoverageAlternatives(request, cancellationToken))
                AddCoverageSpans(request, face, span.Font.FontSize, span.Start, span.End);
            else resolvedFonts.Add(new(span.Start, span.End, new(face, span.Font.FontSize)));
        }
        SvgPreparedFont font = firstRequestedFont is not null ? firstRequestedFont
            : snapshot.GetFont(style.Font, style.SourceElementId, cancellationToken);
        if (fontSpans.IsEmpty && snapshot.HasCoverageAlternatives(style.Font, cancellationToken))
            AddCoverageSpans(style.Font, font, style.FontSize, 0, text.Length);
        // Source rectangles also define a solid text drawing's bounds when an
        // enclosing opacity, mask, filter or instance uses the run later.
        double metricScale = deviceMetrics?.Scale ?? 1;
        bool nativeDrawing = resolvedFonts.Count != 0
            || SvgTextLayoutBounds.UsesFittedMetrics(font, SvgGlyphDeviceMetrics.GetEffectiveFontSize(style.FontSize * metricScale));
        foreach (SvgTextResolvedFontSpan span in resolvedFonts.AsSpan())
        {
            cancellationToken.ThrowIfCancellationRequested();
            nativeDrawing &= SvgTextLayoutBounds.UsesFittedMetrics(span.Style.Font, SvgGlyphDeviceMetrics.GetEffectiveFontSize(span.Style.FontSize * metricScale));
        }
        bool needsFragments = !placementRanges.IsEmpty || nativeDrawing && (defaultPaint is not null || !paintSpans.IsEmpty)
            || defaultPaint?.Decoration is not null || defaultPaint?.HasStroke == true || defaultPaint?.FillServer is not null;
        foreach (SvgTextPaintSpan span in paintSpans) needsFragments |= span.Paint.Decoration is not null || span.Paint.HasStroke || span.Paint.FillServer is not null;
        SvgTextRecordingStroke elementStroke = defaultPaint?.Stroke is {} elementPaint
            ? new(true, elementPaint.Style.Width) : SvgTextRecordingStroke.None;
        double originX = style.X + style.Dx;
        double originY = style.Y + style.Dy;
        if (!double.IsFinite(originX) || !double.IsFinite(originY))
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text coordinates exceed the admitted numeric range.");
        }

        if (style.PositionLists.IsEmpty)
        {
            var (ordinary, resolvedMetrics, fittingResult) = SvgGlyphLayout.CreateStorageAndMetrics(font, text, style.FontSize, originX, originY, style.LetterSpacing, style.WordSpacing, needsFragments, letterSpacingSpans, positionAdjustments, paintSpans, resolvedFonts.AsSpan(), nodeBoundaries, placementRanges, style.Anchor, deviceMetrics, style.TextLength is > 0 ? new SvgTextFitting(style.TextLength.Value, style.LengthAdjust) : null, budget, style.SourceElementId, cancellationToken);
            try { return new SvgTextRun(style, font, ordinary, paintSpans.IsEmpty ? defaultPaint : paintSpans[0].Paint, resolvedMetrics, elementStroke)
                { GlyphStretch = AdjustGlyphs(ordinary, font, style, resolvedMetrics, fittingResult, cancellationToken) }; }
            catch { ordinary.Dispose(); throw; }
        }
        using OwnedParserArray<SvgTextPositionAdjustment> positions = style.PositionLists.Build(text, positionAdjustments, budget, cancellationToken);
        SvgPositionedGlyphStorage? glyphs = null;
        try
        {
            var layout = SvgGlyphLayout.CreateStorageAndMetrics(font, text, style.FontSize, 0, 0, style.LetterSpacing, style.WordSpacing, needsFragments, letterSpacingSpans, positions.Items, paintSpans, resolvedFonts.AsSpan(), nodeBoundaries, placementRanges, style.Anchor, deviceMetrics, style.TextLength is > 0 ? new SvgTextFitting(style.TextLength.Value, style.LengthAdjust) : null, budget, style.SourceElementId, cancellationToken);
            glyphs = layout.Storage;
            return new SvgTextRun(style, font, glyphs, paintSpans.IsEmpty ? defaultPaint : paintSpans[0].Paint, layout.Metrics, elementStroke)
            { GlyphStretch = AdjustGlyphs(glyphs, font, style, layout.Metrics, layout.Fitting, cancellationToken) };
        }
        catch { glyphs?.Dispose(); throw; }

        void AddCoverageSpans(SvgFontFaceRequest request, SvgPreparedFont primary, double fontSize, int start, int end)
        {
            int offset = start, groupStart = start;
            SvgPreparedFont? current = null;
            foreach (Rune scalar in text.AsSpan(start, end - start).EnumerateRunes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgPreparedFont selected = snapshot.GetFontForScalar(request, primary, (uint)scalar.Value, cancellationToken);
                if (current is not null && !ReferenceEquals(current, selected))
                {
                    resolvedFonts.Add(new(groupStart, offset, new(current, fontSize)));
                    groupStart = offset;
                }
                current = selected; offset += scalar.Utf16SequenceLength;
            }
            if (current is not null) resolvedFonts.Add(new(groupStart, end, new(current, fontSize)));
        }

        static double AdjustGlyphs(SvgPositionedGlyphStorage storage, SvgPreparedFont font, SvgTextStyle style, SvgGlyphDeviceMetrics? metrics, SvgTextFittingResult fittingResult,
CancellationToken token)
        {
            if (style.Baseline != SvgTextBaseline.Alphabetic)
            {
                SvgGlyphFontStyle previous = default;
                double shift = 0;
                Span<SvgPositionedGlyph> baselineGlyphs = storage.MutableGlyphs;
                for (int index = 0; index < baselineGlyphs.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgGlyphFontStyle current = storage.GlyphFonts.IsEmpty ? new(font, style.FontSize) : storage.GlyphFonts[index];
                    if (index == 0 || current != previous)
                    {
                        shift = GetBaselineShift(current, style.Baseline, metrics, token);
                        previous = current;
                    }
                    double y = baselineGlyphs[index].Y + shift;
                    if (!double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The positioned text baseline exceeds the numeric range.");
                    baselineGlyphs[index] = baselineGlyphs[index] with { Y = y };
                }
                Span<SvgTextPaintFragment> baselineFragments = storage.MutablePaintFragments;
                for (int index = 0; index < baselineFragments.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgTextPaintFragment fragment = baselineFragments[index];
                    if (fragment.SourceOrigin is not {} origin) continue;
                    SvgGlyphFontStyle face = storage.GlyphFonts.IsEmpty ? new(font, style.FontSize) : storage.GlyphFonts[Math.Min(fragment.Start, storage.Glyphs.Length - 1)];
                    float baselineScale = (float)(metrics?.Scale ?? 1);
                    baselineFragments[index] = fragment with { SourceOrigin = origin with
                    { Y = (float)origin.Y + (float)GetBaselineShift(face, style.Baseline, metrics, token) * baselineScale } };
                }
            }
            if (fittingResult.IsResolved) return fittingResult.Scale;
            if (style.TextLength is not double desired || desired == 0 || storage.Glyphs.Length == 0
                || style.LengthAdjust == SvgTextLengthAdjust.Spacing && storage.Glyphs.Length == 1) return 1;
            Span<SvgPositionedGlyph> glyphs = storage.MutableGlyphs;
            double first = glyphs[0].X;
            double natural = storage.NaturalWidth;
            if (natural <= 0) return 1;
            double scale = style.LengthAdjust == SvgTextLengthAdjust.SpacingAndGlyphs ? (float)desired / (float)natural : 1;
            double spacing = style.LengthAdjust == SvgTextLengthAdjust.Spacing && glyphs.Length > 1 ? (desired - natural) / (glyphs.Length - 1) : 0;
            double anchorShift = style.Anchor switch { SvgTextAnchor.Middle => (natural - desired) / 2, SvgTextAnchor.End => natural - desired, _ => 0 };
            Span<SvgTextPaintFragment> fragments = storage.MutablePaintFragments;
            float sourceScale = (float)(metrics?.Scale ?? 1);
            float sourceSpacing = style.LengthAdjust == SvgTextLengthAdjust.Spacing && glyphs.Length > 1
                ? ((float)desired * sourceScale - (float)natural * sourceScale) / (glyphs.Length - 1) : 0;
            float sourceFirst = fragments.IsEmpty ? (float)first * sourceScale : (float)(fragments[0].SourceOrigin?.X ?? first * sourceScale);
            for (int index = 0; index < fragments.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgTextPaintFragment fragment = fragments[index];
                fragments[index] = fragment with
                {
                    Width = fragment.Width * scale + spacing * (fragment.End - fragment.Start - 1),
                    SourceWidth = fragment.SourceWidth is float sourceWidth
                        ? sourceWidth * (float)scale + sourceSpacing * (fragment.End - fragment.Start - 1) : null,
                    SourceOrigin = fragment.SourceOrigin is {} origin
                        ? origin with { X = sourceFirst + ((float)origin.X - sourceFirst) * (float)scale
                            + fragment.Start * sourceSpacing + (float)anchorShift * sourceScale } : null
                };
            }
            int fittedFragment = 0;
            double fittedPaintShift = 0;
            for (int index = 0; index < glyphs.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgPositionedGlyph glyph = glyphs[index];
                double x = first + (glyph.X - first) * scale + index * spacing + anchorShift;
                if (style.LengthAdjust == SvgTextLengthAdjust.SpacingAndGlyphs && metrics is { IsTextPath: false }
                    && fittedFragment < fragments.Length && fragments[fittedFragment].Start == index)
                {
                    if (fragments[fittedFragment].SourceOrigin is {} sourceOrigin)
                    {
                        // Float composition after physical 26.6 truncation preserves
                        // native quarter-pixel strike selection at rounding ties.
                        float sourceX = (float)sourceOrigin.X;
                        float translation = sourceX - (float)scale * sourceX;
                        float painted = MathF.Truncate(sourceX * 64f) / 64 * (float)scale + translation;
                        fittedPaintShift = painted / sourceScale - x;
                    }
                    fittedFragment++;
                }
                x += fittedPaintShift;
                if (!double.IsFinite(x) || !double.IsFinite(scale)) throw new SvgException(SvgErrorCode.LimitExceeded, "Adjusted text length exceeds the numeric range.");
                glyphs[index] = glyph with { X = x, Advance = glyph.Advance * scale };
            }
            return scale;

            static double GetBaselineShift(SvgGlyphFontStyle fontStyle, SvgTextBaseline baseline, SvgGlyphDeviceMetrics? metrics, CancellationToken token)
            {
                SvgPreparedFont font = fontStyle.Font;
                double outputScale = metrics?.Scale ?? 1;
                float size = (float)(fontStyle.FontSize * outputScale);
                float ascent = (metrics?.Target ?? (OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux)) == SvgGlyphMetricTarget.Windows
                    ? size * font.Metrics.LineMetrics.WindowsAscender / font.UnitsPerEm : font.Metrics.LineMetrics.Ascender * (size / font.UnitsPerEm);
                float roundedAscent = MathF.Floor(ascent + .5f);
                float baselineAscent;
                if (baseline == SvgTextBaseline.Middle)
                {
                    int xHeight = font.GetXHeight(token);
                    baselineAscent = roundedAscent - (xHeight > 0 ? size * xHeight / font.UnitsPerEm / 2 : size / 4);
                }
                else baselineAscent = roundedAscent * .2f;
                // Blink retains fractional baseline metrics in LayoutUnit before
                // the platform's glyph strike snaps its painted origin.
                double shift = (roundedAscent - MathF.Round(baselineAscent * 64, MidpointRounding.AwayFromZero) / 64) / outputScale;
                if (!double.IsFinite(shift)) throw new SvgException(SvgErrorCode.LimitExceeded, "The text baseline exceeds the device range.");
                return shift;
            }
        }
    }

    /// <summary>Yields each positioned glyph's closed SVG-coordinate contours while retaining at most one decoded consecutive glyph outline.</summary>
    internal static IEnumerable<SvgGlyphContourStorage> GetGlyphContours(SvgTextRun run, RenderBudget budget, CancellationToken cancellationToken)
        => GetGlyphContours(run, SvgAffineTransform.Identity, budget, cancellationToken);

    /// <summary>Applies the admitted parent translation after local layout while reusing the ordinary decoded outline and native strike.</summary>
    internal static IEnumerable<SvgGlyphContourStorage> GetGlyphContours(SvgTextRun run, SvgAffineTransform placement, RenderBudget budget, CancellationToken cancellationToken)
    {
        OpenTypeGlyphContourStorage? decoded = null;
        ushort decodedGlyphIndex = 0;
        SvgPreparedFont? decodedFont = null;
        try
        {
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (SvgPositionedGlyph glyph, SvgAffineTransform glyphMap) = run.GetGlyphPlacement(index, placement);
                SvgGlyphFontStyle glyphFont = run.GetGlyphFont(index);
                if (decoded is null || decodedGlyphIndex != glyph.GlyphIndex || !ReferenceEquals(decodedFont, glyphFont.Font))
                {
                    decoded?.Dispose();
                    decoded = OpenTypeFont.GetGlyphContours(glyphFont.Font.Bytes, glyph.GlyphIndex, glyphFont.Font.Metrics, budget, cancellationToken);
                    decodedGlyphIndex = glyph.GlyphIndex;
                    decodedFont = glyphFont.Font;
                }
                yield return SvgGlyphOutline.GetContourStorage(glyphFont.Font, glyph, glyphFont.FontSize, glyphMap, decoded, budget, cancellationToken);
            }
        }
        finally { decoded?.Dispose(); }
    }
}

/// <summary>Owns positioned glyph storage or borrows one scoped interval of a shared text run.</summary>
/// <remarks>The caller retains the font snapshot. An owning run releases glyph arrays; a view releases
/// only its rebased fragment array. The parent outlives every view. Metadata is reserved by the caller,
/// and input views remain parser-thread confined through synchronous painting.</remarks>
internal sealed class SvgTextRun : IDisposable
{
    private readonly SvgPositionedGlyphStorage _glyphs;
    private readonly SvgTextGlyphPaint? _defaultPaint;
    private readonly SvgGlyphDeviceMetrics? _deviceMetrics;
    private readonly int _glyphStart;
    private readonly int _glyphCount;
    private readonly bool _ownsGlyphs;
    private readonly OwnedParserArray<SvgTextPaintFragment> _viewFragments;
    private OwnedParserArray<SvgTextRecordingScope> _recordingScopes = new([], null);
    private ReadOnlyMemory<SvgTextRecordingScope> _recordingScopeView;
    private SvgTextRecordingStroke _elementStroke;
    internal bool HasRootRecording { get; private set; }
    internal ReadOnlySpan<SvgTextRecordingScope> RecordingScopes => _recordingScopeView.Span;

    /// <summary>Copies normalized non-root recording ranges before publication; the run owns and charges the copied array.</summary>
    /// <remarks>Parser-thread confined. The caller retains input storage; work and cancellation are shared.
    /// Ranges follow subtree closing order, so End is monotonic. Root ownership is retained by value;
    /// views borrow only their own placement range. Failure and disposal release all reservations.</remarks>
    internal void RetainRecordingScopes(ReadOnlySpan<SvgTextRecordingScope> scopes, SvgTextRecordingStroke elementStroke, RenderBudget budget, CancellationToken token)
    {
        if (!_ownsGlyphs || HasRootRecording || _recordingScopes.Items.Length != 0)
            throw new InvalidOperationException("Recording scopes must be retained once by an owning run.");
        int count = 0, previousEnd = -1;
        bool root = false;
        foreach (SvgTextRecordingScope scope in scopes)
        {
            token.ThrowIfCancellationRequested();
            if (scope.Start < 0 || scope.End <= scope.Start || scope.FirstText < scope.Start || scope.FirstText >= scope.End
                || !double.IsFinite(scope.StrokeWidth) || scope.StrokeWidth < 0 || !scope.IsRoot && scope.End < previousEnd)
                throw new ArgumentException("Recording scopes must contain directly painted text, a non-negative finite stroke width and ordered subtree ends.", nameof(scopes));
            if (scope.IsRoot) root = true;
            else { count++; previousEnd = scope.End; }
        }
        if (count == 0) { HasRootRecording = root; _elementStroke = elementStroke; return; }
        RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgTextRecordingScope>(count), SvgMemoryCategory.Parser);
        try
        {
            SvgTextRecordingScope[] retained = new SvgTextRecordingScope[count];
            int index = 0;
            foreach (SvgTextRecordingScope scope in scopes)
            { token.ThrowIfCancellationRequested(); if (!scope.IsRoot) retained[index++] = scope; }
            _recordingScopes = new(retained, owner);
            _recordingScopeView = retained;
            HasRootRecording = root; _elementStroke = elementStroke;
        }
        catch { owner.Dispose(); throw; }
    }

    internal SvgTextRun(SvgTextStyle style, SvgPreparedFont font, SvgPositionedGlyphStorage glyphs, SvgTextGlyphPaint? defaultPaint,
        SvgGlyphDeviceMetrics? deviceMetrics, SvgTextRecordingStroke elementStroke)
    {
        _elementStroke = elementStroke;
        Style = style;
        Font = font;
        _glyphs = glyphs;
        _defaultPaint = defaultPaint;
        _deviceMetrics = deviceMetrics;
        _glyphCount = glyphs.Glyphs.Length;
        _ownsGlyphs = true;
        _viewFragments = new([], null);
    }

    /// <summary>Borrows one parent glyph interval and adopts only its rebased fragment array.</summary>
    /// <remarks>The parser keeps the parent alive until every view is disposed. Views borrow glyph/font/recording storage
    /// and release only their adopted fragments; the caller reserves view metadata separately. Selecting the source range
    /// observes the caller's cancellation token. Construction and painting remain parser-thread confined.</remarks>
    internal SvgTextRun(SvgTextRun source, int first, int count, OwnedParserArray<SvgTextPaintFragment> fragments,
        SvgTextStyle style, SvgGlyphDeviceMetrics? metrics, CancellationToken token)
    {
        _glyphs = source._glyphs;
        _glyphStart = checked(source._glyphStart + first);
        _glyphCount = count;
        _ownsGlyphs = false;
        _viewFragments = fragments;
        _defaultPaint = source._defaultPaint;
        _deviceMetrics = metrics;
        Style = style;
        Font = source.Font;
        GlyphStretch = source.GlyphStretch;
        _elementStroke = source._elementStroke;
        HasRootRecording = source.HasRootRecording;
        // Non-root scopes cannot cross placement ranges: textPath is admitted
        // only as a direct text child. Root records retain the shared element box.
        if (fragments.Items.Length != 0 && !source._recordingScopeView.IsEmpty)
        {
            int firstScope = UpperBound(source.RecordingScopes, fragments.Items[0].SourceStart, token);
            int lastScope = UpperBound(source.RecordingScopes, fragments.Items[^1].SourceEnd, token);
            _recordingScopeView = source._recordingScopeView.Slice(firstScope, lastScope - firstScope);
        }

        static int UpperBound(ReadOnlySpan<SvgTextRecordingScope> scopes, int offset, CancellationToken token)
        {
            int low = 0, high = scopes.Length;
            while (low < high)
            {
                token.ThrowIfCancellationRequested();
                int middle = low + (high - low) / 2;
                if (scopes[middle].End <= offset) low = middle + 1;
                else high = middle;
            }
            return low;
        }
    }

    /// <summary>Retains the element stroke width for SVG visual bounds independently of character paint overrides.</summary>
    internal double ElementStrokeWidth => _elementStroke.Resolve();
    internal SvgTextStyle Style { get; }

    internal SvgPreparedFont Font { get; }
    internal double GlyphStretch { get; init; } = 1;

    internal ReadOnlySpan<SvgPositionedGlyph> Glyphs => _glyphs.Glyphs.Slice(_glyphStart, _glyphCount);
    internal ReadOnlySpan<SvgTextPaintFragment> PaintFragments => _ownsGlyphs ? _glyphs.PaintFragments : _viewFragments.Items;
    internal SvgGlyphDeviceMetrics? DeviceMetrics => _deviceMetrics;
    internal SvgGlyphFontStyle GetGlyphFont(int index) => _glyphs.GlyphFonts.IsEmpty ? new(Font, Style.FontSize) : _glyphs.GlyphFonts[_glyphStart + index];
    internal SvgTextGlyphPaint GetGlyphPaint(int index, SvgColor fallbackColor) => _glyphs.GlyphPaints.IsEmpty ? _defaultPaint ?? new(new(fallbackColor, fallbackColor.Alpha / 255f), true) : _glyphs.GlyphPaints[_glyphStart + index];
    internal double GetGlyphRotation(int index) => _glyphs.GlyphRotations.IsEmpty ? 0 : _glyphs.GlyphRotations[_glyphStart + index];

    /// <summary>Applies source-grid paint precision and composes character-local rotation with glyph and element placement.</summary>
    internal (SvgPositionedGlyph Position, SvgAffineTransform Map) GetGlyphPlacement(int index, SvgAffineTransform placement)
        => GetPlacement(index, placement, true);

    internal (SvgPositionedGlyph Position, SvgAffineTransform Map) GetDecorationPlacement(int index, SvgAffineTransform placement)
        => GetPlacement(index, placement, false);

    private (SvgPositionedGlyph Position, SvgAffineTransform Map) GetPlacement(int index, SvgAffineTransform placement, bool stretch)
    {
        SvgPositionedGlyph glyph = Glyphs[index];
        double angle = GetGlyphRotation(index);
        double glyphStretch = stretch ? GlyphStretch : 1;
        // Blink truncates the source-font rectangle before viewport mapping.
        // Apply its baseline displacement to the paint copy of the glyph.
        if (stretch && angle == 0 && _deviceMetrics is {} paintMetrics)
        {
            SvgGlyphFontStyle face = GetGlyphFont(index);
            float size = SvgGlyphDeviceMetrics.GetEffectiveFontSize(face.FontSize * paintMetrics.Scale);
            OpenTypeFontMetrics font = face.Font.Metrics;
            float ascent = paintMetrics.Target == SvgGlyphMetricTarget.Windows
                ? size * font.LineMetrics.WindowsAscender / font.UnitsPerEm
                : font.LineMetrics.Ascender * (size / font.UnitsPerEm);
            float above = MathF.Floor(ascent + .5f);
            float sourceY = (float)glyph.Y * (float)paintMetrics.Scale;
            float top = sourceY - above;
            float painted = MathF.Truncate(top * 64) / 64 + above;
            double y = glyph.Y + (painted - sourceY) / paintMetrics.Scale;
            if (!double.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "The text paint baseline exceeds the numeric range.");
            glyph = glyph with { Y = y };
        }
        if (angle == 0 && glyphStretch == 1) return placement.IsTranslation ? (glyph.Translate(placement), SvgAffineTransform.Identity) : (glyph, placement);
        SvgAffineTransform map = SvgAffineTransform.FromTranslation(glyph.X, glyph.Y)
            .ComposeChecked(SvgAffineTransform.FromRotationDegrees(angle)).ComposeChecked(SvgAffineTransform.FromScale(glyphStretch, 1));
        if (angle != 0 && _deviceMetrics is { } metrics)
        {
            SvgGlyphFontStyle font = GetGlyphFont(index);
            map = metrics.PlaceTextPathPaint(map, 0, 0, font.FontSize, font.Font.Metrics);
        }
        return (glyph with { X = 0, Y = 0 }, placement.ComposeChecked(map));
    }

    public void Dispose() { if (_ownsGlyphs) { _glyphs.Dispose(); _recordingScopes.Dispose(); } else _viewFragments.Dispose(); }
}
