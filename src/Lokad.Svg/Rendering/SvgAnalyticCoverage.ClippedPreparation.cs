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
    /// <remarks>Both passes charge clipping, validation and traversal. Authored points map directly into bitmap coordinates; all clipped verbs construct there, combine there, then translate their fixed walk/curve state to canvas space. Source contours are borrowed during construction and no extra mapped tape is allocated. Rational verbs convert after device mapping, one quadratic at a time through the same bounded piece buffer. The first pass proves an edge bound; the second reserves objects, list growth and final arrays before construction. Empty clipped curves stay empty. Cubic and rational convex edges retain live state; polynomial convex quadratics preserve their pre-stepped gate. Horizontal/AA paint clipping stays separate from curve clipping. Reservations transfer to the returned instance or release on every exception.</remarks>
    private static SvgAnalyticCoverage PrepareClipped(long nativePointCount, scoped ReadOnlySpan<SvgDevicePoint> points, scoped ReadOnlySpan<int> ends, scoped ReadOnlySpan<int> votes, bool hasVotes, bool isEvenOdd, SvgPolygonMapping mapping, SvgQuadSpan[] quads, SvgCubicSpan[] cubics, bool useSpans, bool convex, bool rawBox, bool useRunBlitter, SvgAnalyticPaintClip paintClip, bool usesHorizontalClip, int xStart, int xEnd, int yStart, int yEnd, SvgAnalyticClipBounds clip, SvgCpuBitmapOrigin origin, SvgPreparedPathContours? source, SvgAffineTransform sourceMap, long maximumWork, RenderBudget budget, scoped ref long setupWork, out bool eligible, CancellationToken token)
    {
        eligible = false;
        int width = xEnd - xStart;
        ReadOnlySpan<SvgDevicePoint> sourcePoints = source is null ? points : source.Points;
        SvgAffineTransform? localSourceMap = source is null ? null : sourceMap;
        long maximumEdges = 0;
        Span<SvgAnalyticClipPiece> pieces = stackalloc SvgAnalyticClipPiece[Clipper.MaximumPieces];
        Span<SvgDevicePoint> conicPoints = stackalloc SvgDevicePoint[SvgRationalQuadratic.MaximumPointCount];
        List<AnalyticEdge>? kept = null;
        RenderBudget.BudgetReservation? scratch = null;
        try
        {
            ChargeClippedSetupWork(ref setupWork, 4, maximumWork, token);
            SvgAnalyticClipBounds localClip = new(clip.Left - origin.X, clip.Top - origin.Y, clip.Right - origin.X, clip.Bottom - origin.Y);
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
                    if (end - start < 3 || (hasVotes && votes[contour] == 0) || (mapping != SvgPolygonMapping.NativeFillEdges && TwiceArea(points[start..end]) == 0)) continue;
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
                        int sourceIndex = index;
                        bool hasQuad = contourSpans && quadCursor < quadEnd && quads[quadCursor].StartIndex == index;
                        bool hasCubic = contourSpans && cubicCursor < cubicEnd && cubics[cubicCursor].StartIndex == index;
                        bool localLine = !hasQuad && !hasCubic;
                        int conicCount = 1;
                        bool rational = hasQuad && quads[quadCursor].ConicWeight != 1f;
                        if (rational)
                        {
                            SvgQuadSpan conic = source is null ? quads[quadCursor] : source.QuadSpans[quadCursor];
                            ChargeClippedSetupWork(ref setupWork, 6, maximumWork, token);
                            CheckClippedSetupWork(setupWork + SvgRationalQuadratic.MaximumConversionWork, maximumWork, token);
                            // Subdivide after mapping the source conic into this bitmap.
                            SvgAnalyticClipPoint first = ToLocalClipPoint(sourcePoints[index], origin, localSourceMap);
                            SvgAnalyticClipPoint control = ToLocalClipPoint(conic.Control, origin, localSourceMap);
                            SvgAnalyticClipPoint last = ToLocalClipPoint(sourcePoints[conic.EndIndex], origin, localSourceMap);
                            conicCount = SvgRationalQuadratic.Convert(new(first.X, first.Y), new(control.X, control.Y), new(last.X, last.Y), conic.ConicWeight,
                                SvgRationalQuadratic.DeviceTolerance, conicPoints, out int conicWork, token);
                            ChargeClippedSetupWork(ref setupWork, conicWork, maximumWork, token);
                        }
                        for (int conicIndex = 0; conicIndex < conicCount; conicIndex++)
                        {
                            ChargeClippedSetupWork(ref setupWork, localLine ? 4 : hasCubic ? 8 : 6, maximumWork, token);
                            Clipper clipper = new(localClip, !convex, pieces, maximumWork - setupWork, token);
                            try
                            {
                                if (hasQuad)
                                {
                                    SvgQuadSpan quad = source is null ? quads[quadCursor] : source.QuadSpans[quadCursor];
                                    if (rational)
                                    {
                                        clipper.ClipQuad(
                                            new((float)conicPoints[2 * conicIndex].X, (float)conicPoints[2 * conicIndex].Y),
                                            new((float)conicPoints[2 * conicIndex + 1].X, (float)conicPoints[2 * conicIndex + 1].Y),
                                            new((float)conicPoints[2 * conicIndex + 2].X, (float)conicPoints[2 * conicIndex + 2].Y));
                                    }
                                    else clipper.ClipQuad(ToLocalClipPoint(sourcePoints[sourceIndex], origin, localSourceMap), ToLocalClipPoint(quad.Control, origin, localSourceMap), ToLocalClipPoint(sourcePoints[quad.EndIndex], origin, localSourceMap));
                                    index = quad.EndIndex - 1;
                                }
                                else if (hasCubic)
                                {
                                    SvgCubicSpan cubic = source is null ? cubics[cubicCursor] : source.CubicSpans[cubicCursor];
                                    clipper.ClipCubic(ToLocalClipPoint(sourcePoints[sourceIndex], origin, localSourceMap), ToLocalClipPoint(cubic.FirstControl, origin, localSourceMap), ToLocalClipPoint(cubic.SecondControl, origin, localSourceMap), ToLocalClipPoint(sourcePoints[cubic.EndIndex], origin, localSourceMap));
                                    index = cubic.EndIndex - 1;
                                }
                                else clipper.ClipLine(ToLocalClipPoint(sourcePoints[index], origin, localSourceMap), ToLocalClipPoint(sourcePoints[index + 1 < end ? index + 1 : start], origin, localSourceMap));
                            }
                            finally { setupWork += clipper.WorkUnits; }
                            for (int pieceIndex = 0; pieceIndex < clipper.Count; pieceIndex++)
                            {
                                SvgAnalyticClipPiece piece = pieces[pieceIndex];
                                ChargeClippedSetupWork(ref setupWork, 1, maximumWork, token);
                                if (pass == 0)
                                {
                                    // Live pieces own one edge. Polynomial convex quadratics can
                                    // expand into at most 64 forward-difference steps.
                                    maximumEdges += convex && !rational && piece.Kind == SvgAnalyticClipKind.Quad ? 64 : 1;
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
                                        if (edge is not null)
                                        {
                                            if (!CombineVertical(edge, kept)) kept.Add(edge);
                                        }
                                        break;
                                    case SvgAnalyticClipKind.Quad:
                                        if (convex && !rational) AppendQuadPiece(kept, piece.First.X, piece.First.Y, piece.Second.X, piece.Second.Y, piece.Third.X, piece.Third.Y, ref contourEdges, ref edgeWork, token);
                                        else BuildQuadLiveEdge(kept, piece.First.X, piece.First.Y, piece.Second.X, piece.Second.Y, piece.Third.X, piece.Third.Y, ref contourEdges, ref edgeWork);
                                        // Existing quad helpers charge setup only for nonempty
                                        // pieces; clipping also charges rejected setups.
                                        if (edgeWork == 0) edgeWork = 4;
                                        break;
                                    case SvgAnalyticClipKind.Cubic:
                                        BuildCubicLiveEdge(kept, piece.First.Coordinates, piece.Second.Coordinates, piece.Third.Coordinates, piece.Fourth.Coordinates, ref contourEdges, ref edgeWork);
                                        break;
                                    default: throw new InvalidOperationException("Unknown clipped analytic verb.");
                                }
                                if (edgeWork > phaseBound) throw new InvalidOperationException("Clipped edge construction exceeded its bounded numerical phase.");
                                setupWork += edgeWork;
                            }
                        }
                        if (hasQuad) quadCursor++;
                        else if (hasCubic) cubicCursor++;
                    }
                }
            }
            if (kept is null) throw new InvalidOperationException("Clipped edge storage was not constructed.");
            foreach (AnalyticEdge edge in kept)
            {
                TranslateConstructedEdge(edge, origin, ref setupWork, maximumWork, token);
            }
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
            return new(edges, head, tail, row, temp, alphas, xStart, width, Math.Min(yEnd, clip.Bottom), skipIntersect, isEvenOdd, convex, rawBox, useRunBlitter, usesHorizontalClip, paintClip, new(clip.Left, clip.Right), budget, scratch);
        }
        catch { scratch?.Dispose(); throw; }

        static void ChargeClippedSetupWork(scoped ref long work, long units, long maximumWork, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (units > maximumWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Clipped analytic preparation exceeds the remaining work limit.");
            work += units;
        }

        static SvgAnalyticClipPoint ToLocalClipPoint(SvgDevicePoint point, SvgCpuBitmapOrigin origin, SvgAffineTransform? sourceMap)
        {
            SvgDevicePoint local = sourceMap is {} map ? SvgAuthoredPath.MapPoint(map, point, origin) : new(point.X - origin.X, point.Y - origin.Y);
            return new((float)local.X, (float)local.Y);
        }

    }
}
