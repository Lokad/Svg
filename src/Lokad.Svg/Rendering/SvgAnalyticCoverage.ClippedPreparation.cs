// Vertical edge combination adapts Skia's SkEdgeBuilder.cpp at the revision
// recorded in THIRD_PARTY_NOTICES.txt. Copyright 2011 Google Inc.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

internal ref partial struct SvgAnalyticCoverage
{
    private static void CheckClippedSetupWork(long requiredWork, long maximumWork, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (requiredWork > maximumWork) throw new SvgException(SvgErrorCode.LimitExceeded, "Clipped analytic preparation exceeds the remaining work limit.");
    }


    /// <summary>Counts and constructs clipped verbs without retaining an intermediate piece tape.</summary>
    /// <remarks>Both passes charge clipping, validation and traversal. The first pass proves an edge bound; the second reserves objects, list growth and final arrays before construction. Empty clipped curves stay empty. Horizontal clip-wrapper routing is retained separately from curve clipping; vertical-only clips preserve specialized pairs. Convex fills preserve their separately gated pre-stepped walker. Reservations transfer to the returned instance or release on every exception.</remarks>
    private static SvgAnalyticCoverage PrepareClipped(long nativePointCount, scoped ReadOnlySpan<SvgDevicePoint> points, scoped ReadOnlySpan<int> ends, scoped ReadOnlySpan<int> votes, bool hasVotes, bool isEvenOdd, SvgQuadSpan[] quads, SvgCubicSpan[] cubics, bool useSpans, bool convex, bool useRunBlitter, bool usesHorizontalClip, int xStart, int xEnd, int yStart, int yEnd, SvgAnalyticClipBounds clip, long maximumWork, RenderBudget budget, scoped ref long setupWork, out bool eligible, CancellationToken token)
    {
        eligible = false;
        int width = xEnd - xStart;
        long maximumEdges = 0;
        Span<SvgAnalyticClipPiece> pieces = stackalloc SvgAnalyticClipPiece[Clipper.MaximumPieces];
        List<AnalyticEdge>? kept = null;
        RenderBudget.BudgetReservation? scratch = null;
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    if (maximumEdges > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Clipped analytic edge count exceeds the supported range.");
                    scratch = budget.Reserve(ScratchByteCount(0, width, maximumEdges), SvgMemoryCategory.Surface);
                    kept = new List<AnalyticEdge>((int)Math.Min(points.Length, maximumEdges));
                }
                int priorEnd = 0;
                int quadCursor = 0;
                int cubicCursor = 0;
                for (int contour = 0; contour < ends.Length; contour++)
                {
                    int start = priorEnd;
                    int end = ends[contour];
                    priorEnd = end;
                    ChargeClippedSetupWork(ref setupWork, end - start + 1L, maximumWork, token);
                    if (end - start < 3 || (hasVotes && votes[contour] == 0) || TwiceArea(points[start..end]) == 0) continue;
                    while (quadCursor < quads.Length && quads[quadCursor].Contour < contour) { ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token); quadCursor++; }
                    while (cubicCursor < cubics.Length && cubics[cubicCursor].Contour < contour) { ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token); cubicCursor++; }
                    int quadEnd = quadCursor;
                    int cubicEnd = cubicCursor;
                    while (quadEnd < quads.Length && quads[quadEnd].Contour == contour) { ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token); quadEnd++; }
                    while (cubicEnd < cubics.Length && cubics[cubicEnd].Contour == contour) { ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token); cubicEnd++; }
                    ChargeClippedSetupWork(ref setupWork, (long)quadEnd - quadCursor + cubicEnd - cubicCursor, maximumWork, token);
                    bool contourSpans = useSpans && (quadEnd == quadCursor || quads[quadEnd - 1].EndIndex < end) && (cubicEnd == cubicCursor || cubics[cubicEnd - 1].EndIndex < end) && ContourSpansValid(start, end, quads, quadCursor, quadEnd, cubics, cubicCursor, cubicEnd, out _, token);
                    for (int index = start; index < end; index++)
                    {
                        ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token);
                        Clipper clipper = new(clip, !convex, pieces, maximumWork - setupWork, token);
                        try
                        {
                            if (contourSpans && quadCursor < quadEnd && quads[quadCursor].StartIndex == index)
                            {
                                SvgQuadSpan quad = quads[quadCursor++];
                                clipper.ClipQuad(ToClipPoint(points[index]), ToClipPoint(quad.Control), ToClipPoint(points[quad.EndIndex]));
                                index = quad.EndIndex - 1;
                            }
                            else if (contourSpans && cubicCursor < cubicEnd && cubics[cubicCursor].StartIndex == index)
                            {
                                SvgCubicSpan cubic = cubics[cubicCursor++];
                                clipper.ClipCubic(ToClipPoint(points[index]), ToClipPoint(cubic.FirstControl), ToClipPoint(cubic.SecondControl), ToClipPoint(points[cubic.EndIndex]));
                                index = cubic.EndIndex - 1;
                            }
                            else clipper.ClipLine(ToClipPoint(points[index]), ToClipPoint(points[index + 1 < end ? index + 1 : start]));
                        }
                        finally { setupWork += clipper.WorkUnits; }
                        for (int pieceIndex = 0; pieceIndex < clipper.Count; pieceIndex++)
                        {
                            SvgAnalyticClipPiece piece = pieces[pieceIndex];
                            ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token);
                            if (pass == 0)
                            {
                                // Monotonic live pieces own one edge. The convex route can
                                // expand each curve into at most 64 forward-difference steps.
                                maximumEdges += convex && piece.Kind != SvgAnalyticClipKind.Line ? 64 : 1;
                                continue;
                            }
                            if (kept is null) throw new InvalidOperationException("Clipped edge storage was not reserved.");
                            // Check the complete bounded numerical phase before entering
                            // it, then report its actual setup and initial stepping cost.
                            long phaseBound = piece.Kind == SvgAnalyticClipKind.Line ? 2 : 132;
                            CheckClippedSetupWork(setupWork + phaseBound, maximumWork, token);
                            long edgeWork = 0;
                            int contourEdges = kept.Count;
                            switch (piece.Kind)
                            {
                                case SvgAnalyticClipKind.Line:
                                    AnalyticEdge? edge = SetLine(piece.First.X, piece.First.Y, piece.Second.X, piece.Second.Y, true);
                                    edgeWork = 2;
                                    if (edge is not null) AddClippedLine(kept, edge);
                                    break;
                                case SvgAnalyticClipKind.Quad:
                                    if (convex) AppendQuadPiece(kept, piece.First.X, piece.First.Y, piece.Second.X, piece.Second.Y, piece.Third.X, piece.Third.Y, ref contourEdges, ref edgeWork, token);
                                    else BuildQuadLiveEdge(kept, piece.First.X, piece.First.Y, piece.Second.X, piece.Second.Y, piece.Third.X, piece.Third.Y, ref contourEdges, ref edgeWork);
                                    // Existing quad helpers charge setup only for nonempty
                                    // pieces; clipping also charges rejected setups.
                                    if (edgeWork == 0) edgeWork = 4;
                                    break;
                                case SvgAnalyticClipKind.Cubic:
                                    if (convex) AppendCubicPiece(kept, piece.First.Coordinates, piece.Second.Coordinates, piece.Third.Coordinates, piece.Fourth.Coordinates, ref contourEdges, ref edgeWork, token);
                                    else BuildCubicLiveEdge(kept, piece.First.Coordinates, piece.Second.Coordinates, piece.Third.Coordinates, piece.Fourth.Coordinates, ref contourEdges, ref edgeWork);
                                    break;
                                default: throw new InvalidOperationException("Unknown clipped analytic verb.");
                            }
                            if (edgeWork > phaseBound) throw new InvalidOperationException("Clipped edge construction exceeded its bounded numerical phase.");
                            setupWork += edgeWork;
                        }
                    }
                }
            }
            if (kept is null) throw new InvalidOperationException("Clipped edge storage was not constructed.");
            int levels = 0;
            for (int level = kept.Count; level > 0; level >>= 1) levels++;
            ChargeClippedSetupWork(ref setupWork, (long)kept.Count * (levels + 3) + (useRunBlitter ? width : 0), maximumWork, token);
            AnalyticEdge[] edges = kept.ToArray();
            Array.Sort(edges, EdgeOrder.Instance);
            AnalyticEdge head = new() { UpperY = int.MinValue, LowerY = int.MinValue, X = int.MinValue, UpperX = int.MinValue, DY = int.MaxValue };
            AnalyticEdge tail = new() { UpperY = int.MaxValue, LowerY = int.MaxValue, X = int.MaxValue, UpperX = int.MaxValue, DY = int.MaxValue };
            head.Next = edges.Length > 0 ? edges[0] : tail;
            tail.Prev = edges.Length > 0 ? edges[^1] : head;
            for (int index = 0; index < edges.Length; index++)
            {
                if ((index & 1023) == 0) token.ThrowIfCancellationRequested();
                edges[index].Prev = index == 0 ? head : edges[index - 1];
                edges[index].Next = index + 1 == edges.Length ? tail : edges[index + 1];
            }
            byte[] row = new byte[width + 2];
            int[] temp = new int[width + 4];
            int[] alphas = new int[width + 4];
            eligible = true;
            bool skipIntersect = nativePointCount > (long)(Math.Min(yEnd, clip.Bottom) - Math.Max(yStart, clip.Top)) * 2;
            return new(edges, head, tail, row, temp, alphas, xStart, width, Math.Min(yEnd, clip.Bottom), skipIntersect, isEvenOdd, convex, useRunBlitter, usesHorizontalClip, new(clip.Left, clip.Right), budget, scratch);
        }
        catch { scratch?.Dispose(); throw; }

        static void ChargeClippedSetupWork(scoped ref long work, long units, long maximumWork, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (units > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Clipped analytic preparation exceeds the remaining work limit.");
            work += units;
        }


        static SvgAnalyticClipPoint ToClipPoint(SvgDevicePoint point) => new((float)point.X, (float)point.Y);

        // Projected vertical lines can trim or cancel earlier projected lines.
        // Unlike the legacy uncut helper, both partial and total combinations
        // consume the incoming edge; a total combination removes the prior one.
        static void AddClippedLine(List<AnalyticEdge> edges, AnalyticEdge edge)
        {
            if (edge.DX == 0 && edges.Count > 0)
            {
                AnalyticEdge last = edges[^1];
                if (last.InitialCurveCount == 0 && last.DX == 0 && edge.X == last.X)
                {
                    if (edge.Winding == last.Winding)
                    {
                        if (edge.LowerY == last.UpperY) { last.UpperY = edge.UpperY; last.Y = last.UpperY; return; }
                        if (SkAbs32(edge.UpperY - last.LowerY) < 0x100) { last.LowerY = edge.LowerY; return; }
                    }
                    else if (SkAbs32(edge.UpperY - last.UpperY) < 0x100)
                    {
                        if (SkAbs32(edge.LowerY - last.LowerY) < 0x100) { edges.RemoveAt(edges.Count - 1); return; }
                        if (edge.LowerY < last.LowerY) { last.UpperY = edge.LowerY; last.Y = last.UpperY; return; }
                        last.UpperY = last.LowerY; last.Y = last.UpperY; last.LowerY = edge.LowerY; last.Winding = edge.Winding; return;
                    }
                    else if (SkAbs32(edge.LowerY - last.LowerY) < 0x100)
                    {
                        if (edge.UpperY > last.UpperY) { last.LowerY = edge.UpperY; return; }
                        last.LowerY = last.UpperY; last.UpperY = edge.UpperY; last.Y = last.UpperY; last.Winding = edge.Winding; return;
                    }
                }
            }
            edges.Add(edge);
        }
    }
}
