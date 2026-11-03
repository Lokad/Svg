namespace Lokad.Svg.Fonts;

using System.Text;
using Lokad.Svg.Parsing;

internal static partial class SvgCanonicalText
{
    /// <summary>Appends one physical-font/style run's font-aware canonical glyph sequence.</summary>
    /// <remarks>Both the supplied font and mutable destination are borrowed. The caller keeps
    /// their owners alive and does not access the destination concurrently. SourceOffset selects
    /// the original paint owner; ClusterStart/End enclose all source characters consumed by the
    /// cluster. Decomposition, reordering and composition retain these independently. Temporary
    /// capacity belongs to the caller's budgeted destination, and all traversals observe cancellation.</remarks>
    internal static void AppendGlyphs(SvgPreparedFont font, string text, int start, int end,
        BudgetedParserList<SvgShapingGlyph> destination, CancellationToken token)
    {
        if (start < 0 || end < start || end > text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        int first = destination.Count, clusterStart = start;
        Span<Rune> decomposed = stackalloc Rune[MaximumDecomposedScalars];
        for (int source = start; source < end;)
        {
            token.ThrowIfCancellationRequested();
            Rune scalar = Rune.GetRuneAt(text, source);
            int next = source + scalar.Utf16SequenceLength;
            if (next > end) throw new ArgumentException("A shaping run cannot divide a Unicode scalar.", nameof(end));
            if (!GetProperties(scalar).IsMark) clusterStart = source;
            ushort originalGlyph = OpenTypeFont.GetGlyphIndex(font.Bytes, (uint)scalar.Value, font.Metrics, token);
            int count = Decompose(scalar, decomposed);
            bool covered = true;
            if (originalGlyph != 0)
            {
                for (int component = 0; component < count; component++)
                {
                    token.ThrowIfCancellationRequested();
                    covered &= OpenTypeFont.GetGlyphIndex(font.Bytes, (uint)decomposed[component].Value, font.Metrics, token) != 0;
                }
            }
            if (!covered) { decomposed[0] = scalar; count = 1; }
            for (int component = 0; component < count; component++)
            {
                token.ThrowIfCancellationRequested();
                if (destination.Count - first == SvgTextLimits.MaximumCharacters)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Canonical text expansion exceeds the admitted glyph limit.");
                Rune part = decomposed[component];
                byte combiningClass = GetProperties(part).CombiningClass;
                ushort glyph = OpenTypeFont.GetGlyphIndex(font.Bytes, (uint)part.Value, font.Metrics, token);
                destination.Add(new(part, glyph, source, clusterStart, next, destination.Count - first, combiningClass, IsDefaultIgnorable(part), 0, 0, SvgGlyphAttachment.None));
            }
            source = next;
        }
        Span<SvgShapingGlyph> glyphs = destination.AsMutableSpan()[first..];
        MergeClusters(glyphs, token);
        // Only nonzero canonical classes reorder. Equal classes retain the
        // original decomposition order; a class-zero mark remains a barrier.
        for (int begin = 0; begin < glyphs.Length;)
        {
            token.ThrowIfCancellationRequested();
            if (glyphs[begin].CombiningClass == 0) { begin++; continue; }
            int limit = begin + 1;
            while (limit < glyphs.Length && glyphs[limit].CombiningClass != 0)
            {
                token.ThrowIfCancellationRequested();
                limit++;
            }
            SortMarks(glyphs[begin..limit], token);
            begin = limit;
        }
        int written = 0, starter = -1;
        byte lastClass = 0;
        for (int index = 0; index < glyphs.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgShapingGlyph current = glyphs[index];
            if (starter >= 0 && (lastClass == 0 || lastClass < current.CombiningClass)
                && TryCompose(glyphs[starter].Scalar, current.Scalar, out Rune composite))
            {
                ushort composedGlyph = OpenTypeFont.GetGlyphIndex(font.Bytes, (uint)composite.Value, font.Metrics, token);
                if (composedGlyph != 0)
                {
                    SvgShapingGlyph previous = glyphs[starter];
                    glyphs[starter] = previous with { Scalar = composite, GlyphIndex = composedGlyph,
                        ClusterStart = Math.Min(previous.ClusterStart, current.ClusterStart), ClusterEnd = Math.Max(previous.ClusterEnd, current.ClusterEnd) };
                    continue;
                }
            }
            if (current.CombiningClass == 0) starter = written;
            lastClass = current.CombiningClass;
            glyphs[written++] = current;
        }
        while (destination.Count > first + written) { token.ThrowIfCancellationRequested(); destination.RemoveLast(); }
        // Composition can join initially separate clusters (for example Hangul
        // jamo). Marks of the consumed character join that same source cluster.
        MergeClusters(destination.AsMutableSpan()[first..], token);

        // Cancellable stable ordering without quadratic work on long mark runs.
        // Original order is an explicit secondary key, so an in-place heap sort
        // preserves equal-class order without another retained array.
        static void SortMarks(Span<SvgShapingGlyph> marks, CancellationToken cancellation)
        {
            for (int root = marks.Length / 2 - 1; root >= 0; root--) Sift(marks, root, marks.Length, cancellation);
            for (int limit = marks.Length - 1; limit > 0; limit--)
            {
                cancellation.ThrowIfCancellationRequested();
                (marks[0], marks[limit]) = (marks[limit], marks[0]);
                Sift(marks, 0, limit, cancellation);
            }

            static void Sift(Span<SvgShapingGlyph> items, int root, int count, CancellationToken cancellation)
            {
                while (root < count / 2)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int child = root * 2 + 1;
                    if (child + 1 < count && Compare(items[child], items[child + 1]) < 0) child++;
                    if (Compare(items[root], items[child]) >= 0) break;
                    (items[root], items[child]) = (items[child], items[root]);
                    root = child;
                }
            }

            static int Compare(SvgShapingGlyph left, SvgShapingGlyph right)
            {
                int comparison = left.CombiningClass.CompareTo(right.CombiningClass);
                return comparison != 0 ? comparison : left.CanonicalOrder.CompareTo(right.CanonicalOrder);
            }
        }
    }
    internal static void MergeClusters(Span<SvgShapingGlyph> items, CancellationToken cancellation)
    {
        for (int begin = 0; begin < items.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            int limit = begin + 1, sourceStart = items[begin].ClusterStart, sourceEnd = items[begin].ClusterEnd;
            while (limit < items.Length && items[limit].ClusterStart < sourceEnd)
            {
                cancellation.ThrowIfCancellationRequested();
                sourceEnd = Math.Max(sourceEnd, items[limit++].ClusterEnd);
            }
            for (int index = begin; index < limit; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                items[index] = items[index] with { ClusterStart = sourceStart, ClusterEnd = sourceEnd };
            }
            begin = limit;
        }
    }

}

/// <summary>A run-local glyph, its original scalar paint owner and its shared emitted-text cluster.</summary>
internal readonly record struct SvgShapingGlyph(System.Text.Rune Scalar, ushort GlyphIndex, int SourceOffset,
    int ClusterStart, int ClusterEnd, int CanonicalOrder, byte CombiningClass, bool IsDefaultIgnorable,
    int LigatureComponents, int ContextAdvance, SvgGlyphAttachment Attachment);

/// <summary>A preceding glyph and the difference of its validated anchors, in font units. Owns no storage.</summary>
internal readonly record struct SvgGlyphAttachment(int Parent, int X, int Y)
{
    internal static SvgGlyphAttachment None => new(-1, 0, 0);
}
