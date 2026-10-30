using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns the normalized source ranges of one mixed direct/path text element until its local paths are available.</summary>
/// <remarks>The queued record owns this metadata allowance. The owner adopts extracted source arrays and borrows the caller font snapshot.
/// Preparation and emission are parser-thread confined; temporary text, maps and glyph views reserve storage and release on every exit.
/// The queue disposes this owner after successful emission; repeated disposal is harmless and failure leaves document teardown responsible for it.</remarks>
internal sealed class SvgPendingTextLayout : IDisposable
{
    private OwnedParserArray<SvgExtractedTextRun> _sources;
    private SvgFontSnapshot? _snapshot;
    private readonly SvgTextStyle _style;
    private readonly SvgTextGlyphPaint _paint;
    private readonly SvgGlyphDeviceMetrics? _metrics;

    internal SvgPendingTextLayout(OwnedParserArray<SvgExtractedTextRun> sources, SvgTextStyle style, SvgTextGlyphPaint paint,
        SvgGlyphDeviceMetrics? metrics, SvgFontSnapshot snapshot)
    { _sources = sources; _style = style; _paint = paint; _metrics = metrics; _snapshot = snapshot; }

    /// <summary>Shapes the element once and emits its source ranges using scoped views that borrow the shared glyph storage.</summary>
    internal void Emit(SvgPendingTextRun pending, SvgDocumentState state, SvgRenderLimits limits, CancellationToken token)
    {
        SvgFontSnapshot snapshot = _snapshot ?? throw new ObjectDisposedException(nameof(SvgPendingTextLayout));
        RenderBudget budget = state.Budget;
        using BudgetedTextBuffer textBuffer = new(budget);
        using BudgetedParserList<SvgTextLetterSpacingSpan> spacing = new(budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgTextPositionAdjustment> descendants = new(budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgTextPaintSpan> paints = new(budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgTextFontSpan> fonts = new(budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgTextNodeBoundary> nodes = new(budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgTextRecordingScope> recordings = new(budget, SvgMemoryCategory.Parser, token);
        int rootFirstText = -1;
        double rootRecordingWidth = 0;
        using BudgetedParserList<MeasuredRange> measured = new(budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgTextPlacementRange> ranges = new(budget, SvgMemoryCategory.Parser, token);
        SvgInheritedTextStyle defaultFont = new(_style.Font.Family, _style.Font.Weight, _style.Font.Italic,
            _style.FontSize, SvgTextWhitespaceMode.Default, _style.LetterSpacing, _style.Anchor) { WordSpacing = _style.WordSpacing };
        using RenderBudget.BudgetReservation scratchOwner = budget.Reserve(1024, SvgMemoryCategory.Parser);
        SvgDevicePoint? followingOrigin = null;
        int offset = 0;
        try
        {
            foreach (SvgExtractedTextRun source in _sources.Items)
            {
                token.ThrowIfCancellationRequested();
                int start = offset;
                textBuffer.Append(source.Layout.Value, SvgTextLimits.MaximumCharacters, token);
                offset = textBuffer.Length;
                foreach (SvgTextRecordingScope scope in source.RecordingScopes.Items)
                {
                    token.ThrowIfCancellationRequested();
                    if (scope.IsRoot)
                    {
                        rootFirstText = rootFirstText < 0 ? checked(start + scope.FirstText) : Math.Min(rootFirstText, checked(start + scope.FirstText));
                        rootRecordingWidth = scope.StrokeWidth;
                    }
                    else recordings.Add(scope with { Start = checked(start + scope.Start), End = checked(start + scope.End), FirstText = checked(start + scope.FirstText) });
                }
                if (start != 0) nodes.Add(new(start, true));
                foreach (SvgTextNodeBoundary node in source.NodeBoundaries.Items)
                { token.ThrowIfCancellationRequested(); nodes.Add(node with { Offset = checked(start + node.Offset) }); }
                foreach (SvgTextPositionAdjustment adjustment in source.Layout.PositionAdjustments)
                { token.ThrowIfCancellationRequested(); descendants.Add(adjustment with { Start = checked(start + adjustment.Start) }); }
                if (source.Layout.LetterSpacingSpans.Length == 0) AddSpacing(start, offset, _style.LetterSpacing, _style.WordSpacing);
                else foreach (SvgTextLetterSpacingSpan span in source.Layout.LetterSpacingSpans)
                    AddSpacing(start + span.Start, start + span.End, span.LetterSpacing, span.WordSpacing);
                if (source.PaintSpans.Items.Length == 0) AddPaint(start, offset, _paint);
                else foreach (SvgTextPaintSpan span in source.PaintSpans.Items) AddPaint(start + span.Start, start + span.End, span.Paint);
                if (source.FontSpans.Items.Length == 0) AddFont(start, offset, defaultFont);
                else foreach (SvgTextFontSpan span in source.FontSpans.Items) AddFont(start + span.Start, start + span.End, span.Font);

                SvgMeasuredTextPath? path = null;
                double pathOffset = 0;
                if (source.Path is {} reference)
                {
                    if (!state.Definitions.TryGetValue(reference.PathId, out SvgDefinition definition) || definition.LeafSource is not {} leaf)
                        throw new SvgException(SvgErrorCode.UnresolvedResource, "The local text-path reference was not found.");
                    path = SvgTextPathLayout.ReadMeasured(leaf, limits.MaximumPathCommands, budget, token);
                    try
                    {
                        pathOffset = reference.IsPercentage ? path.Baseline.Length * (reference.StartOffset / 100)
                            : reference.StartOffset * path.Baseline.OffsetScale;
                        if (!double.IsFinite(pathOffset)) throw new SvgException(SvgErrorCode.LimitExceeded, "The resolved text-path offset is not finite.");
                        followingOrigin = path.EndPoint;
                        measured.Add(new(path, pathOffset));
                    }
                    catch { path.Dispose(); throw; }
                }
                else measured.Add(new(null, 0));
                ranges.Add(new(start, offset, source.Path, source.Path is null && source.ResumeAfterPath is not null ? followingOrigin : null));
            }
            if (rootFirstText >= 0) recordings.Add(new(0, offset, rootFirstText, rootRecordingWidth, true));
            using BudgetedText text = textBuffer.Detach();
            using BudgetedParserList<SvgTextPositionAdjustment> merged = new(budget, SvgMemoryCategory.Parser, token);
            if (_style.PositionLists.IsEmpty)
            {
                SvgTextPositionAdjustment first = descendants.Count != 0 && descendants[0].Start == 0
                    ? descendants[0] : new(0, null, null, 0, 0);
                merged.Add(first with { X = first.X ?? _style.X, Y = first.Y ?? _style.Y,
                    Dx = first.OverridesDx ? first.Dx : _style.Dx + first.Dx,
                    Dy = first.OverridesDy ? first.Dy : _style.Dy + first.Dy });
                int initial = descendants.Count != 0 && descendants[0].Start == 0 ? 1 : 0;
                for (int index = initial; index < descendants.Count; index++) { token.ThrowIfCancellationRequested(); merged.Add(descendants[index]); }
            }
            else
            {
                using OwnedParserArray<SvgTextPositionAdjustment> positions = (_style.PositionLists with { CharacterOffset = 0 }).Build(text.Value, descendants.AsSpan(), budget, token);
                foreach (SvgTextPositionAdjustment adjustment in positions.Items) { token.ThrowIfCancellationRequested(); merged.Add(adjustment); }
            }
            using BudgetedParserList<SvgTextPositionAdjustment> resolved = new(budget, SvgMemoryCategory.Parser, token);
            int positionIndex = 0;
            foreach (SvgTextPlacementRange range in ranges.AsSpan())
            {
                token.ThrowIfCancellationRequested();
                if (range.Path is not null && (positionIndex == merged.Count || merged[positionIndex].Start != range.Start))
                    resolved.Add(new(range.Start, 0, null, 0, 0));
                while (positionIndex < merged.Count && merged[positionIndex].Start < range.End)
                {
                    token.ThrowIfCancellationRequested();
                    SvgTextPositionAdjustment adjustment = merged[positionIndex++];
                    if (range.Path is not null)
                        adjustment = adjustment with { IgnoreY = true,
                            X = adjustment.Start == range.Start ? adjustment.X ?? 0 : adjustment.X };
                    resolved.Add(adjustment);
                }
            }
            SvgTextStyle sharedStyle = _style with { X = 0, Y = 0, Dx = 0, Dy = 0, PositionLists = default };
            ReadOnlySpan<SvgTextFontSpan> fontIntervals = fonts.AsSpan();
            if (fonts.Count == 1 && fonts[0].Font.Family == _style.Font.Family && fonts[0].Font.Weight == _style.Font.Weight
                && fonts[0].Font.Italic == _style.Font.Italic && fonts[0].Font.FontSize == _style.FontSize)
                fontIntervals = [];
            using RenderBudget.BudgetReservation sharedOwner = budget.Reserve(512, SvgMemoryCategory.Parser);
            using SvgTextRun shared = SvgTextRunBuilder.CreateShared(sharedStyle, text.Value, spacing.AsSpan(), resolved.AsSpan(), paints.AsSpan(),
                fontIntervals, nodes.AsSpan(), ranges.AsSpan(), _paint, _metrics, snapshot, budget, token);
            shared.RetainRecordingScopes(recordings.AsSpan(), _sources.Items[0].ElementStroke, budget, token);
            ReadOnlySpan<SvgTextPaintFragment> sharedFragments = shared.PaintFragments;
            bool needsObjectBox = pending.FilterGroupDepth > 0;
            for (int index = 0; index < shared.Glyphs.Length && !needsObjectBox; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgTextGlyphPaint paint = shared.GetGlyphPaint(index, pending.Color);
                needsObjectBox = UsesObjectBox(paint.FillServer, state) || UsesObjectBox(paint.Stroke?.Style.Stroke, state);
            }
            SvgRectangle? objectBox = null;
            for (int pass = needsObjectBox ? 0 : 1; pass < 2; pass++)
            {
                int fragmentIndex = 0;
                for (int index = 0; index < ranges.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgTextPlacementRange range = ranges[index];
                    using BudgetedParserList<SvgTextPaintFragment> viewFragments = new(budget, SvgMemoryCategory.Geometry, token);
                    int first = -1, last = 0;
                    while (fragmentIndex < sharedFragments.Length && sharedFragments[fragmentIndex].SourceStart < range.End)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgTextPaintFragment fragment = sharedFragments[fragmentIndex++];
                        if (fragment.SourceStart < range.Start) throw new InvalidOperationException("Shared source fragments must follow their placement ranges.");
                        if (first < 0) first = fragment.Start;
                        last = fragment.End;
                        viewFragments.Add(fragment with { Start = fragment.Start - first, End = fragment.End - first });
                    }
                    if (first < 0) continue;
                    using RenderBudget.BudgetReservation viewOwner = budget.Reserve(512, SvgMemoryCategory.Parser);
                    OwnedParserArray<SvgTextPaintFragment> fragments = viewFragments.Detach();
                    SvgTextRun view;
                    try { view = new(shared, first, last - first, fragments, sharedStyle,
                        shared.DeviceMetrics is {} metrics ? metrics with { IsTextPath = range.Path is not null } : null, token); }
                    catch { fragments.Dispose(); throw; }
                    using (view)
                    {
                        SvgMeasuredTextPath? path = measured[index].Path;
                        if (pass == 0)
                        {
                            SvgRectangle box = path is not null
                                ? SvgTextPathLayout.MeasurePath(state, pending, view, path, measured[index].Offset, token)
                                : SvgTextLayoutBounds.Measure(view, [], pending.Placement, state.TextBoundsMetrics, token);
                            if (box.Width > 0 && box.Height > 0)
                            {
                                if (objectBox is not {} previous) objectBox = box;
                                else
                                {
                                    float left = MathF.Min((float)previous.X, (float)box.X), top = MathF.Min((float)previous.Y, (float)box.Y);
                                    float right = MathF.Max((float)previous.X + (float)previous.Width, (float)box.X + (float)box.Width);
                                    float bottom = MathF.Max((float)previous.Y + (float)previous.Height, (float)box.Y + (float)box.Height);
                                    float width = right - left, height = bottom - top;
                                    if (left + width < right && width < float.MaxValue) width = MathF.BitIncrement(width);
                                    if (top + height < bottom && height < float.MaxValue) height = MathF.BitIncrement(height);
                                    objectBox = new(left, top, width, height, default, false, null);
                                }
                            }
                        }
                        else if (path is not null)
                            SvgTextPathLayout.EmitPath(state, pending, view, path, measured[index].Offset, objectBox, limits, token);
                        else SvgTextGlyphPainter.EmitDirect(view, pending.Placement, pending.ViewportClip, pending.Color, state,
                            limits.MaximumPathCommands, budget, pending.RecordingTransform, objectBox, token);
                    }
                }
            }
        }
        finally { foreach (MeasuredRange range in measured.AsSpan()) range.Path?.Dispose(); }

        static bool UsesObjectBox(string? value, SvgDocumentState state)
        {
            if (value is null || !SvgPaintReference.TryParseId(value, out string id)) return false;
            return state.Gradients.TryGetValue(id, out SvgLinearGradient linear) && !linear.IsUserSpace && !linear.IsEmpty
                || state.RadialGradients.TryGetValue(id, out SvgRadialGradient radial) && !radial.IsUserSpace && !radial.IsEmpty
                || state.Patterns.TryGetValue(id, out SvgPatternTile pattern) && pattern.Coordinates is not null;
        }

        void AddSpacing(int start, int end, double letter, double word)
        {
            token.ThrowIfCancellationRequested();
            if (spacing.Count != 0 && spacing[^1].End == start && spacing[^1].LetterSpacing == letter && spacing[^1].WordSpacing == word)
                spacing[^1] = spacing[^1] with { End = end };
            else spacing.Add(new(start, end, letter) { WordSpacing = word });
        }
        void AddPaint(int start, int end, SvgTextGlyphPaint paint)
        {
            token.ThrowIfCancellationRequested();
            if (paints.Count != 0 && paints[^1].End == start && paints[^1].Paint == paint) paints[^1] = paints[^1] with { End = end };
            else paints.Add(new(start, end, paint));
        }
        void AddFont(int start, int end, SvgInheritedTextStyle font)
        {
            token.ThrowIfCancellationRequested();
            if (fonts.Count != 0 && fonts[^1].End == start && fonts[^1].Font.Family == font.Family
                && fonts[^1].Font.Weight == font.Weight && fonts[^1].Font.Italic == font.Italic && fonts[^1].Font.FontSize == font.FontSize)
                fonts[^1] = fonts[^1] with { End = end };
            else fonts.Add(new(start, end, font));
        }
    }

    private readonly record struct MeasuredRange(SvgMeasuredTextPath? Path, double Offset);

    public void Dispose()
    {
        if (_snapshot is null) return;
        foreach (SvgExtractedTextRun source in _sources.Items) source.Dispose();
        _sources.Dispose();
        _sources = new([], null);
        _snapshot = null;
    }
}
