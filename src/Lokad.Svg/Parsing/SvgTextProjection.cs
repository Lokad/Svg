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
        bool changed = false, needsMap = false;
        foreach (Rune scalar in source.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            while (fontIndex < fonts.Length && sourceOffset >= fonts[fontIndex].End) fontIndex++;
            SvgTextTransform transform = fonts.IsEmpty ? style.TextTransform : fonts[fontIndex].Font.TextTransform;
            string language = fonts.IsEmpty ? style.Language : fonts[fontIndex].Font.Language;
            int length = SvgTextCasing.Write(scalar, transform, language, output);
            changed |= !source.AsSpan(sourceOffset, scalar.Utf16SequenceLength).SequenceEqual(output[..length]);
            needsMap |= length != scalar.Utf16SequenceLength;
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
            sourceOffset = 0; fontIndex = 0;
            foreach (Rune scalar in source.EnumerateRunes())
            {
                token.ThrowIfCancellationRequested();
                while (fontIndex < fonts.Length && sourceOffset >= fonts[fontIndex].End) fontIndex++;
                SvgTextTransform transform = fonts.IsEmpty ? style.TextTransform : fonts[fontIndex].Font.TextTransform;
                string language = fonts.IsEmpty ? style.Language : fonts[fontIndex].Font.Language;
                int length = SvgTextCasing.Write(scalar, transform, language, output);
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
                    _liftedPositions.Add(position with { Start = start });
                    if (position.Rotation is null) continue;
                    int end = Map(_map.Items, checked(position.Start + Rune.GetRuneAt(source, position.Start).Utf16SequenceLength));
                    for (int offset = start + Rune.GetRuneAt(_text.Value, start).Utf16SequenceLength; offset < end;
                        offset += Rune.GetRuneAt(_text.Value, offset).Utf16SequenceLength)
                        _liftedPositions.Add(new(offset, null, null, 0, 0) { Rotation = position.Rotation, IgnoreY = position.IgnoreY });
                }
            }
            if (!spacing.IsEmpty)
            {
                _liftedSpacing = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextLetterSpacingSpan span in spacing) _liftedSpacing.Add(span with { Start = Map(_map.Items, span.Start), End = Map(_map.Items, span.End) });
            }
            if (!paints.IsEmpty)
            {
                _liftedPaints = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextPaintSpan span in paints) _liftedPaints.Add(span with { Start = Map(_map.Items, span.Start), End = Map(_map.Items, span.End) });
            }
            if (!fonts.IsEmpty)
            {
                _liftedFonts = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextFontSpan span in fonts) _liftedFonts.Add(span with { Start = Map(_map.Items, span.Start), End = Map(_map.Items, span.End) });
            }
            if (!nodes.IsEmpty)
            {
                _liftedNodes = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextNodeBoundary boundary in nodes) _liftedNodes.Add(boundary with { Offset = Map(_map.Items, boundary.Offset) });
            }
            if (!placements.IsEmpty)
            {
                _liftedPlacements = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextPlacementRange range in placements) _liftedPlacements.Add(range with { Start = Map(_map.Items, range.Start), End = Map(_map.Items, range.End) });
            }
            if (!recordings.IsEmpty)
            {
                _liftedRecordings = new(budget, SvgMemoryCategory.Parser, token);
                foreach (SvgTextRecordingScope scope in recordings) _liftedRecordings.Add(scope with { Start = Map(_map.Items, scope.Start), End = Map(_map.Items, scope.End), FirstText = Map(_map.Items, scope.FirstText) });
            }
        }
        catch { Dispose(); throw; }

        static int Map(ReadOnlySpan<int> map, int offset)
        {
            if ((uint)offset >= (uint)map.Length || map[offset] < 0)
                throw new ArgumentException("A projected interval must use an original scalar boundary.", nameof(offset));
            return map[offset];
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
