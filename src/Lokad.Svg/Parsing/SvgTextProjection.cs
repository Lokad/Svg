using System.Text;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Temporarily owns emitted text and lifted intervals after original SVG positions have been resolved.</summary>
/// <remarks>Parser-thread confined. Input text and spans remain borrowed. Identity conversion allocates nothing.
/// Material strings, maps and replacement arrays reserve the shared budget before allocation; disposal releases
/// temporary storage after the consuming layout has copied its retained data. Cancellation and refusal leave inputs reusable.</remarks>
internal ref struct SvgTextProjection
{
    private BudgetedText _text;
    private OwnedParserArray<int> _map;
    private RenderBudget.BudgetReservation? _workspace;
    private readonly ReadOnlySpan<SvgTextLetterSpacingSpan> _spacing;
    private readonly ReadOnlySpan<SvgTextPositionAdjustment> _positions;
    private readonly ReadOnlySpan<SvgTextPaintSpan> _paints;
    private readonly ReadOnlySpan<SvgTextFontSpan> _fonts;
    private readonly ReadOnlySpan<SvgTextNodeBoundary> _nodes;
    private readonly ReadOnlySpan<SvgTextPlacementRange> _placements;
    private readonly ReadOnlySpan<SvgTextRecordingScope> _recordings;
    private BudgetedParserList<SvgTextLetterSpacingSpan>? _liftedSpacing;
    private BudgetedParserList<SvgTextPositionAdjustment>? _liftedPositions;
    private BudgetedParserList<SvgTextPaintSpan>? _liftedPaints;
    private BudgetedParserList<SvgTextFontSpan>? _liftedFonts;
    private BudgetedParserList<SvgTextNodeBoundary>? _liftedNodes;
    private BudgetedParserList<SvgTextPlacementRange>? _liftedPlacements;
    private BudgetedParserList<SvgTextRecordingScope>? _liftedRecordings;

    internal SvgTextProjection(SvgTextStyle style, string source,
        ReadOnlySpan<SvgTextLetterSpacingSpan> spacing, ReadOnlySpan<SvgTextPositionAdjustment> positions,
        ReadOnlySpan<SvgTextPaintSpan> paints, ReadOnlySpan<SvgTextFontSpan> fonts,
        ReadOnlySpan<SvgTextNodeBoundary> nodes, ReadOnlySpan<SvgTextPlacementRange> placements,
        ReadOnlySpan<SvgTextRecordingScope> recordings, RenderBudget budget, CancellationToken token)
    {
        this = default;
        Style = style; _text = new(source, null); _map = new([], null);
        _spacing = spacing; _positions = positions; _paints = paints; _fonts = fonts;
        _nodes = nodes; _placements = placements; _recordings = recordings;
        bool hasTransform = style.TextTransform != SvgTextTransform.None;
        foreach (SvgTextFontSpan span in fonts) hasTransform |= span.Font.TextTransform != SvgTextTransform.None;
        if (!hasTransform || source.Length == 0) return;
        int fontEnd = 0;
        foreach (SvgTextFontSpan span in fonts)
        {
            if (span.Start != fontEnd || span.End <= span.Start || span.End > source.Length
                || span.End < source.Length && char.IsSurrogatePair(source, span.End - 1))
                throw new ArgumentException("Source text style intervals must partition original scalar boundaries.", nameof(fonts));
            fontEnd = span.End;
        }
        if (!fonts.IsEmpty && fontEnd != source.Length) throw new ArgumentException("Source text styles must cover the original text.", nameof(fonts));
        Span<char> output = stackalloc char[3];
        int sourceOffset = 0, fontIndex = 0, emittedLength = 0;
        SvgTextCasingContext context = default;
        bool changed = false, needsMap = false, hasDeletion = false;
        foreach (Rune scalar in source.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            while (fontIndex < fonts.Length && sourceOffset >= fonts[fontIndex].End) fontIndex++;
            SvgTextTransform transform = fonts.IsEmpty ? style.TextTransform : fonts[fontIndex].Font.TextTransform;
            string language = fonts.IsEmpty ? style.Language : fonts[fontIndex].Font.Language;
            int length = WriteSource(source, sourceOffset, scalar, transform, language, ref context, output, token);
            changed |= !source.AsSpan(sourceOffset, scalar.Utf16SequenceLength).SequenceEqual(output[..length]);
            needsMap |= length != scalar.Utf16SequenceLength;
            hasDeletion |= length == 0;
            emittedLength = checked(emittedLength + length);
            sourceOffset += scalar.Utf16SequenceLength;
        }
        if (!changed) return;
        try
        {
            _workspace = budget.Reserve(768, SvgMemoryCategory.Parser);
            if (needsMap)
            {
                RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(checked(source.Length + 1)), SvgMemoryCategory.Parser);
                try { _map = new(new int[checked(source.Length + 1)], owner); }
                catch { owner.Dispose(); throw; }
                Array.Fill(_map.Items, -1);
            }
            using BudgetedTextBuffer emitted = new(budget);
            sourceOffset = 0; fontIndex = 0; context = default;
            foreach (Rune scalar in source.EnumerateRunes())
            {
                token.ThrowIfCancellationRequested();
                while (fontIndex < fonts.Length && sourceOffset >= fonts[fontIndex].End) fontIndex++;
                SvgTextTransform transform = fonts.IsEmpty ? style.TextTransform : fonts[fontIndex].Font.TextTransform;
                string language = fonts.IsEmpty ? style.Language : fonts[fontIndex].Font.Language;
                int length = WriteSource(source, sourceOffset, scalar, transform, language, ref context, output, token);
                if (needsMap) _map.Items[sourceOffset] = emitted.Length;
                emitted.Append(output[..length], emittedLength, token);
                sourceOffset += scalar.Utf16SequenceLength;
            }
            if (needsMap) _map.Items[source.Length] = emitted.Length;
            _text = emitted.Detach();
            if (!needsMap) return;

            // Root lists consume original scalar addresses, not the expanded
            // string. Their lifted entries then share ordinary glyph layout.
            using OwnedParserArray<SvgTextPositionAdjustment> rootPositions = style.PositionLists.IsEmpty
                ? new([], null) : style.PositionLists.Build(source, positions, budget, token);
            ReadOnlySpan<SvgTextPositionAdjustment> originalPositions = style.PositionLists.IsEmpty ? positions : rootPositions.Items;
            if (!style.PositionLists.IsEmpty) Style = style with { X = 0, Y = 0, Dx = 0, Dy = 0, PositionLists = default };
            if (!originalPositions.IsEmpty)
            {
                _liftedPositions = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextPositionAdjustment position in originalPositions)
                {
                    token.ThrowIfCancellationRequested();
                    int start = Map(_map.Items, position.Start);
                    int end = Map(_map.Items, checked(position.Start + Rune.GetRuneAt(source, position.Start).Utf16SequenceLength));
                    if (start == end)
                    {
                        // A source character with no glyph has no absolute
                        // position/rotation. Its relative displacement belongs
                        // before the next surviving character, if one exists.
                        if (start < _text.Value.Length && (position.Dx != 0 || position.Dy != 0))
                            AddPosition(_liftedPositions, new(start, null, null, position.Dx, position.Dy));
                        continue;
                    }
                    AddPosition(_liftedPositions, position with { Start = start });
                    if (position.Rotation is null) continue;
                    for (int offset = start + Rune.GetRuneAt(_text.Value, start).Utf16SequenceLength; offset < end;
                        offset += Rune.GetRuneAt(_text.Value, offset).Utf16SequenceLength)
                        _liftedPositions.Add(new(offset, null, null, 0, 0) { Rotation = position.Rotation, IgnoreY = position.IgnoreY });
                }
            }
            if (!spacing.IsEmpty)
            {
                _liftedSpacing = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextLetterSpacingSpan span in spacing)
                {
                    int start = Map(_map.Items, span.Start), end = Map(_map.Items, span.End);
                    if (start < end) _liftedSpacing.Add(span with { Start = start, End = end });
                }
            }
            if (!paints.IsEmpty)
            {
                _liftedPaints = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextPaintSpan span in paints)
                {
                    int start = Map(_map.Items, span.Start), end = Map(_map.Items, span.End);
                    if (start < end) _liftedPaints.Add(span with { Start = start, End = end });
                }
            }
            if (!fonts.IsEmpty)
            {
                _liftedFonts = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextFontSpan span in fonts)
                {
                    int start = Map(_map.Items, span.Start), end = Map(_map.Items, span.End);
                    if (start < end) _liftedFonts.Add(span with { Start = start, End = end });
                }
            }
            if (!placements.IsEmpty)
            {
                _liftedPlacements = new(budget, SvgMemoryCategory.Parser, token);
                bool? previousPathSurvives = null;
                foreach (SvgTextPlacementRange range in placements)
                {
                    int start = Map(_map.Items, range.Start), end = Map(_map.Items, range.End);
                    if (range.Path is not null) previousPathSurvives = start < end;
                    if (start < end) _liftedPlacements.Add(range with { Start = start, End = end,
                        AfterPathOrigin = range.Path is null && previousPathSurvives == false ? null : range.AfterPathOrigin });
                }
            }
            if (!nodes.IsEmpty)
            {
                _liftedNodes = new(budget, SvgMemoryCategory.Parser, token);
                if (!hasDeletion)
                    foreach (SvgTextNodeBoundary boundary in nodes) _liftedNodes.Add(boundary with { Offset = Map(_map.Items, boundary.Offset) });
                else
                {
                    ReadOnlySpan<SvgTextPlacementRange> mappedRanges = _liftedPlacements is {} lifted ? lifted.AsSpan() : placements;
                    int oldStart = 0, owner = nodes[0].PreviousElementIdentity, lastOwner = owner, rangeIndex = 0, previousRange = 0;
                    for (int index = 0; index <= nodes.Length; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        int oldEnd = index == nodes.Length ? source.Length : nodes[index].Offset;
                        int start = Map(_map.Items, oldStart), end = Map(_map.Items, oldEnd);
                        if (start < end)
                        {
                            while (rangeIndex + 1 < mappedRanges.Length && mappedRanges[rangeIndex].End <= start) rangeIndex++;
                            bool placementChanges = !mappedRanges.IsEmpty && rangeIndex != previousRange
                                && (mappedRanges[rangeIndex].Path is not null || mappedRanges[previousRange].Path is not null
                                    || mappedRanges[rangeIndex].AfterPathOrigin is not null);
                            if (start != 0) _liftedNodes.Add(nodes[index - 1] with { Offset = start,
                                ChangesElement = owner != lastOwner || placementChanges, PreviousElementIdentity = lastOwner });
                            lastOwner = owner; previousRange = rangeIndex;
                        }
                        oldStart = oldEnd;
                        if (index < nodes.Length) owner = nodes[index].ElementIdentity;
                    }
                }
            }
            if (!recordings.IsEmpty)
            {
                _liftedRecordings = new(budget, SvgMemoryCategory.Parser, token);
                for (int index = 0; index < recordings.Length; index++)
                {
                    SvgTextRecordingScope scope = recordings[index];
                    int start = Map(_map.Items, scope.Start), end = Map(_map.Items, scope.End);
                    if (start == end) continue;
                    int first = FindDirectText(source, _map.Items, paints, recordings, index, token);
                    if (first >= 0) _liftedRecordings.Add(scope with { Start = start, End = end, FirstText = first });
                }
            }
        }
        catch { Dispose(); throw; }

        static int WriteSource(string source, int offset, Rune scalar, SvgTextTransform transform, string language,
            ref SvgTextCasingContext context, Span<char> output, CancellationToken token)
        {
            SvgTextCasingLookahead lookahead = SvgTextCasing.GetLookahead(scalar, transform, language, context.PrecedingCased);
            SvgTextCasingConditions decision = SvgTextCasingConditions.None;
            if (lookahead != SvgTextCasingLookahead.None)
            {
                bool matched = false;
                foreach (Rune following in source.AsSpan(offset + scalar.Utf16SequenceLength).EnumerateRunes())
                {
                    token.ThrowIfCancellationRequested();
                    SvgTextCasingProperties properties = SvgTextCasing.GetProperties(following);
                    if (SvgTextCasing.TryResolve(lookahead, following, properties, out bool result)) { matched = result; break; }
                }
                decision = SvgTextCasing.Resolve(lookahead, matched);
            }
            SvgTextCasingConditions conditions = context.Observe(scalar, SvgTextCasing.GetProperties(scalar)) | decision;
            return SvgTextCasing.Write(scalar, transform, language, conditions, output);
        }

        static int Map(ReadOnlySpan<int> map, int offset)
        {
            if ((uint)offset >= (uint)map.Length || map[offset] < 0)
                throw new ArgumentException("A projected interval must use an original scalar boundary.", nameof(offset));
            return map[offset];
        }

        static void AddPosition(BudgetedParserList<SvgTextPositionAdjustment> list, SvgTextPositionAdjustment position)
        {
            if (list.Count != 0 && list[^1].Start == position.Start)
            {
                SvgTextPositionAdjustment previous = list[^1];
                double dx = previous.Dx + position.Dx, dy = previous.Dy + position.Dy;
                if (!double.IsFinite(dx) || !double.IsFinite(dy))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Projected text offsets exceed the admitted numeric range.");
                list[^1] = position with { X = position.X ?? previous.X, Y = position.Y ?? previous.Y, Dx = dx, Dy = dy };
            }
            else list.Add(position);
        }

        static int FindDirectText(string text, ReadOnlySpan<int> map, ReadOnlySpan<SvgTextPaintSpan> paints,
            ReadOnlySpan<SvgTextRecordingScope> scopes, int scopeIndex, CancellationToken token)
        {
            SvgTextRecordingScope scope = scopes[scopeIndex];
            int at = scope.FirstText, paintIndex = 0;
            if (Map(map, at) < Map(map, at + Rune.GetRuneAt(text, at).Utf16SequenceLength)) return Map(map, at);
            at = SkipInactive(text, map, paints, at, scope.End, ref paintIndex, token);
            // Closing-order ends are monotonic. Each skipped descendant can
            // advance the candidate only beyond already visited descendants.
            int low = 0, high = scopes.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (scopes[middle].End <= scope.FirstText) low = middle + 1; else high = middle;
            }
            for (int index = low; index < scopes.Length && scopes[index].End <= scope.End && at < scope.End; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgTextRecordingScope child = scopes[index];
                if (index != scopeIndex && !child.IsRoot && child.Start >= scope.Start && child.Start <= at && at < child.End)
                    at = SkipInactive(text, map, paints, child.End, scope.End, ref paintIndex, token);
            }
            return at < scope.End ? Map(map, at) : -1;

            static int SkipInactive(string text, ReadOnlySpan<int> map, ReadOnlySpan<SvgTextPaintSpan> paints,
                int at, int end, ref int paintIndex, CancellationToken token)
            {
                while (at < end)
                {
                    token.ThrowIfCancellationRequested();
                    while (paintIndex < paints.Length && paints[paintIndex].End <= at) paintIndex++;
                    if (paintIndex < paints.Length && !paints[paintIndex].Paint.IsPainted) { at = paints[paintIndex].End; continue; }
                    int next = at + Rune.GetRuneAt(text, at).Utf16SequenceLength;
                    if (Map(map, at) < Map(map, next)) break;
                    at = next;
                }
                return at;
            }
        }
    }

    internal SvgTextStyle Style { get; private set; }
    internal string Value => _text.Value;
    internal ReadOnlySpan<SvgTextLetterSpacingSpan> Spacing => _liftedSpacing is {} value ? value.AsSpan() : _spacing;
    internal ReadOnlySpan<SvgTextPositionAdjustment> Positions => _liftedPositions is {} value ? value.AsSpan() : _positions;
    internal ReadOnlySpan<SvgTextPaintSpan> Paints => _liftedPaints is {} value ? value.AsSpan() : _paints;
    internal ReadOnlySpan<SvgTextFontSpan> Fonts => _liftedFonts is {} value ? value.AsSpan() : _fonts;
    internal ReadOnlySpan<SvgTextNodeBoundary> Nodes => _liftedNodes is {} value ? value.AsSpan() : _nodes;
    internal ReadOnlySpan<SvgTextPlacementRange> Placements => _liftedPlacements is {} value ? value.AsSpan() : _placements;
    internal ReadOnlySpan<SvgTextRecordingScope> Recordings => _liftedRecordings is {} value ? value.AsSpan() : _recordings;

    public void Dispose()
    {
        _liftedRecordings?.Dispose(); _liftedPlacements?.Dispose(); _liftedNodes?.Dispose();
        _liftedFonts?.Dispose(); _liftedPaints?.Dispose(); _liftedPositions?.Dispose(); _liftedSpacing?.Dispose();
        _map.Dispose(); _text.Dispose(); _workspace?.Dispose();
    }
}
