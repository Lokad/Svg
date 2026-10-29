// Adapts pinned Skia SkTSort.h. Copyright 2006 The Android Open Source Project.
// See THIRD_PARTY_NOTICES.txt.
namespace Lokad.Svg.Rendering;

internal ref partial struct SvgAnalyticCoverage
{
    /// <summary>Preserves native ordering of coincident edges, including introsort's equal-key permutations.</summary>
    /// <remarks>Sorts the caller-owned array in place without allocating. Partition, insertion and heap loops observe cancellation during bounded O(n log n) sorting. Stack depth
    /// is limited to twice the logarithm of the edge count. No state is shared between conversions.</remarks>
    private static void SortEdges(Span<AnalyticEdge> edges, CancellationToken token)
    {
        if (edges.Length <= 1) return;
        int levels = 0;
        for (int count = edges.Length - 2; count > 0; count >>= 1) levels++;
        IntroSort(edges, 2 * levels, token);

        static bool Less(AnalyticEdge left, AnalyticEdge right)
            => left.UpperY != right.UpperY ? left.UpperY < right.UpperY
                : left.X != right.X ? left.X < right.X : left.DX < right.DX;

        static void IntroSort(Span<AnalyticEdge> values, int depth, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (values.Length <= 32) { InsertionSort(values, token); return; }
                if (depth == 0) { HeapSort(values, token); return; }
                depth--;
                int pivot = Partition(values, (values.Length - 1) >> 1, token);
                IntroSort(values[..pivot], depth, token);
                values = values[(pivot + 1)..];
            }

            static int Partition(Span<AnalyticEdge> values, int pivot, CancellationToken token)
            {
                AnalyticEdge pivotValue = values[pivot];
                (values[pivot], values[^1]) = (values[^1], values[pivot]);
                int nextPivot = 0;
                for (int index = 0; index < values.Length - 1; index++)
                {
                    if ((index & 1023) == 0) token.ThrowIfCancellationRequested();
                    if (!Less(values[index], pivotValue)) continue;
                    (values[index], values[nextPivot]) = (values[nextPivot], values[index]);
                    nextPivot++;
                }
                (values[nextPivot], values[^1]) = (values[^1], values[nextPivot]);
                return nextPivot;
            }

            static void InsertionSort(Span<AnalyticEdge> values, CancellationToken token)
            {
                for (int index = 1; index < values.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Less(values[index], values[index - 1])) continue;
                    AnalyticEdge insert = values[index];
                    int hole = index;
                    do { values[hole] = values[hole - 1]; hole--; }
                    while (hole > 0 && Less(insert, values[hole - 1]));
                    values[hole] = insert;
                }
            }

            static void HeapSort(Span<AnalyticEdge> values, CancellationToken token)
            {
                for (int root = values.Length >> 1; root > 0; root--)
                {
                    token.ThrowIfCancellationRequested();
                    SiftDown(values, root);
                }
                for (int last = values.Length - 1; last > 0; last--)
                {
                    token.ThrowIfCancellationRequested();
                    (values[0], values[last]) = (values[last], values[0]);
                    SiftUp(values[..last]);
                }

                static void SiftDown(Span<AnalyticEdge> values, int root)
                {
                    AnalyticEdge value = values[root - 1];
                    int child = root << 1;
                    while (child <= values.Length)
                    {
                        if (child < values.Length && Less(values[child - 1], values[child])) child++;
                        if (!Less(value, values[child - 1])) break;
                        values[root - 1] = values[child - 1];
                        root = child;
                        child = root << 1;
                    }
                    values[root - 1] = value;
                }

                static void SiftUp(Span<AnalyticEdge> values)
                {
                    AnalyticEdge value = values[0];
                    int root = 1, child = root << 1;
                    while (child <= values.Length)
                    {
                        if (child < values.Length && Less(values[child - 1], values[child])) child++;
                        values[root - 1] = values[child - 1];
                        root = child;
                        child = root << 1;
                    }
                    int parent = root >> 1;
                    while (parent >= 1 && Less(values[parent - 1], value))
                    {
                        values[root - 1] = values[parent - 1];
                        root = parent;
                        parent = root >> 1;
                    }
                    values[root - 1] = value;
                }
            }
        }
    }
}
