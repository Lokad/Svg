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
    internal void Emit(SvgPendingTextRun pending, SvgDocumentState state, SvgRenderLimits limits, ParserWorkCounter work, CancellationToken token)
    {
        SvgFontSnapshot snapshot = _snapshot ?? throw new ObjectDisposedException(nameof(SvgPendingTextLayout));
        RenderBudget budget = state.Budget;
        using BudgetedTextBuffer textBuffer = new(budget);
        using BudgetedParserList<SvgTextLetterSpacingSpan> spacing = new(budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgTextPositionAdjustment> descendants = new(budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgTextPaintSpan> paints = new(budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgTextFontSpan> fonts = new(budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgTextNodeBoundary> nodes = new(budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<MeasuredRange> measured = new(budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgTextPlacementRange> ranges = new(budget, SvgMemoryCategory.Parser, work, token);
        SvgInheritedTextStyle defaultFont = new(_style.Font.Family, _style.Font.Weight, _style.Font.Italic,
            _style.FontSize, SvgTextWhitespaceMode.Default, _style.LetterSpacing, _style.Anchor) { WordSpacing = _style.WordSpacing };
        using RenderBudget.BudgetReservation scratchOwner = budget.Reserve(1024, SvgMemoryCategory.Parser);
        SvgDevicePoint? followingOrigin = null;
        int offset = 0;
        try
        {
            foreach (SvgExtractedTextRun source in _sources.Items)
            {
                work.Consume(token);
                int start = offset;
                textBuffer.Append(source.Layout.Value, SvgTextLimits.MaximumCharacters, work, token);
                offset = textBuffer.Length;
                if (start != 0) nodes.Add(new(start, true));
                foreach (SvgTextNodeBoundary node in source.NodeBoundaries.Items)
                { work.Consume(token); nodes.Add(node with { Offset = checked(start + node.Offset) }); }
                foreach (SvgTextPositionAdjustment adjustment in source.Layout.PositionAdjustments)
                { work.Consume(token); descendants.Add(adjustment with { Start = checked(start + adjustment.Start) }); }
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
                    path = SvgTextPathLayout.ReadMeasured(leaf, limits.MaximumPathCommands, budget, work, token);
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
            using BudgetedText text = textBuffer.Detach();
            using BudgetedParserList<SvgTextPositionAdjustment> merged = new(budget, SvgMemoryCategory.Parser, work, token);
            if (_style.PositionLists.IsEmpty)
            {
                SvgTextPositionAdjustment first = descendants.Count != 0 && descendants[0].Start == 0
                    ? descendants[0] : new(0, null, null, 0, 0);
                merged.Add(first with { X = first.X ?? _style.X, Y = first.Y ?? _style.Y,
                    Dx = first.OverridesDx ? first.Dx : _style.Dx + first.Dx,
                    Dy = first.OverridesDy ? first.Dy : _style.Dy + first.Dy });
                int initial = descendants.Count != 0 && descendants[0].Start == 0 ? 1 : 0;
                for (int index = initial; index < descendants.Count; index++) { work.Consume(token); merged.Add(descendants[index]); }
            }
            else
            {
                using OwnedParserArray<SvgTextPositionAdjustment> positions = (_style.PositionLists with { CharacterOffset = 0 }).Build(text.Value, descendants.AsSpan(), budget, work, token);
                foreach (SvgTextPositionAdjustment adjustment in positions.Items) { work.Consume(token); merged.Add(adjustment); }
            }
            using BudgetedParserList<SvgTextPositionAdjustment> resolved = new(budget, SvgMemoryCategory.Parser, work, token);
            int positionIndex = 0;
            foreach (SvgTextPlacementRange range in ranges.AsSpan())
            {
                work.Consume(token);
                if (range.Path is not null && (positionIndex == merged.Count || merged[positionIndex].Start != range.Start))
                    resolved.Add(new(range.Start, 0, null, 0, 0));
                while (positionIndex < merged.Count && merged[positionIndex].Start < range.End)
                {
                    work.Consume(token);
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
                fontIntervals, nodes.AsSpan(), ranges.AsSpan(), _paint, _metrics, snapshot, budget, work, token);
            ReadOnlySpan<SvgTextPaintFragment> sharedFragments = shared.PaintFragments;
            bool needsObjectBox = pending.FilterGroupDepth > 0;
            for (int index = 0; index < shared.Glyphs.Length && !needsObjectBox; index++)
            {
                work.Consume(token);
                SvgTextGlyphPaint paint = shared.GetGlyphPaint(index, pending.Color);
                needsObjectBox = UsesObjectBox(paint.FillServer, state) || UsesObjectBox(paint.Stroke?.Style.Stroke, state);
            }
            SvgRectangle? objectBox = null;
            for (int pass = needsObjectBox ? 0 : 1; pass < 2; pass++)
            {
                int fragmentIndex = 0;
                for (int index = 0; index < ranges.Count; index++)
                {
                    work.Consume(token);
                    SvgTextPlacementRange range = ranges[index];
                    using BudgetedParserList<SvgTextPaintFragment> viewFragments = new(budget, SvgMemoryCategory.Geometry, work, token);
                    int first = -1, last = 0;
                    while (fragmentIndex < sharedFragments.Length && sharedFragments[fragmentIndex].SourceStart < range.End)
                    {
                        work.Consume(token);
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
                        shared.DeviceMetrics is {} metrics ? metrics with { IsTextPath = range.Path is not null } : null); }
                    catch { fragments.Dispose(); throw; }
                    using (view)
                    {
                        SvgMeasuredTextPath? path = measured[index].Path;
                        if (pass == 0)
                        {
                            SvgRectangle box = path is not null
                                ? SvgTextPathLayout.MeasurePath(state, pending, view, path, measured[index].Offset, work, token)
                                : SvgTextLayoutBounds.Measure(view, [], pending.Placement, state.TextBoundsMetrics, work, token);
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
                            SvgTextPathLayout.EmitPath(state, pending, view, path, measured[index].Offset, objectBox, limits, work, token);
                        else SvgTextGlyphPainter.EmitDirect(view, pending.Placement, pending.ViewportClip, pending.Color, state,
                            limits.MaximumPathCommands, budget, work, pending.RecordingTransform, objectBox, token);
                    }
                }
            }
        }
        finally { foreach (MeasuredRange range in measured.AsSpan()) range.Path?.Dispose(); }

        static bool UsesObjectBox(string? value, SvgDocumentState state)
        {
            if (value is null || !SvgLocalUrl.TryParseId(value, out string id)) return false;
            return state.Gradients.TryGetValue(id, out SvgLinearGradient linear) && !linear.IsUserSpace && !linear.IsEmpty
                || state.RadialGradients.TryGetValue(id, out SvgRadialGradient radial) && !radial.IsUserSpace && !radial.IsEmpty
                || state.Patterns.TryGetValue(id, out SvgPatternTile pattern) && pattern.Coordinates is not null;
        }

        void AddSpacing(int start, int end, double letter, double word)
        {
            work.Consume(token);
            if (spacing.Count != 0 && spacing[^1].End == start && spacing[^1].LetterSpacing == letter && spacing[^1].WordSpacing == word)
                spacing[^1] = spacing[^1] with { End = end };
            else spacing.Add(new(start, end, letter) { WordSpacing = word });
        }
        void AddPaint(int start, int end, SvgTextGlyphPaint paint)
        {
            work.Consume(token);
            if (paints.Count != 0 && paints[^1].End == start && paints[^1].Paint == paint) paints[^1] = paints[^1] with { End = end };
            else paints.Add(new(start, end, paint));
        }
        void AddFont(int start, int end, SvgInheritedTextStyle font)
        {
            work.Consume(token);
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
