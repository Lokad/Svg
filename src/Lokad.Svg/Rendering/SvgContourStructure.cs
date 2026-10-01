namespace Lokad.Svg.Rendering;

/// <summary>Structural flags for closed device-space contours.</summary>
[Flags]
internal enum SvgContourFlags
{
    /// <summary>Simple closed contour with no detected degeneracy.</summary>
    None = 0,

    /// <summary>At least one zero-length edge with bitwise-equal endpoints.</summary>
    ZeroLengthEdge = 1,

    /// <summary>At least one interior crossing between non-adjacent edges.</summary>
    ProperCrossing = 2,

    /// <summary>At least one collinear overlap of positive length between non-adjacent edges.</summary>
    CoincidentOverlap = 4,

    /// <summary>At least one endpoint contact between non-adjacent edges of one contour.</summary>
    SelfTouch = 8,

    /// <summary>At least one vertex, crossing or collinear contact between distinct contours.</summary>
    TouchesOtherContour = 16,
}

/// <summary>Classifies closed device-space contours by edge contact without allocating.</summary>
/// <remarks>
/// <para>Segment pairs share a bounding-box precheck, then orientation predicates: strictly opposite nonzero
/// signs on both sides report a proper crossing, any zero orientation with the point inside the other bounding
/// box reports a touch, and four zero orientations fall through to a dominant-axis overlap test that
/// separates positive-length coincidence from single-point contact. Adjacent edges, including the closing
/// adjacency, never count; zero-length edges are flagged yet skip contact tests.</para>
/// <para>Predicates evaluate in double arithmetic and are exact for integer-scale geometry; every finite
/// input classifies deterministically. Non-finite intermediates fail with
/// <see cref="SvgErrorCode.LimitExceeded"/>. Touching and coincident configurations classify exactly but
/// carry no nesting meaning; that family stays outside admission. Callers charge one validation work unit
/// per examined segment pair; this classifier holds no budget itself and allocates nothing.</para>
/// </remarks>
internal static class SvgContourStructure
{
    /// <summary>Classifies one closed contour slice, following the implicit edge from its last point back to its first.</summary>
    internal static SvgContourFlags ClassifyContour(ReadOnlySpan<SvgDevicePoint> points, int start, int end, CancellationToken cancellationToken)
    {
        if (start < 0 || start > points.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start || end > points.Length) throw new ArgumentOutOfRangeException(nameof(end));
        SvgContourFlags flags = SvgContourFlags.None;
        int count = end - start;
        int pairs = 0;
        for (int first = 0; first < count; first++)
        {
            (SvgDevicePoint firstStart, SvgDevicePoint firstEnd) = EdgeAt(points, start, count, first);
            if (firstStart == firstEnd)
            {
                flags |= SvgContourFlags.ZeroLengthEdge;
                continue;
            }

            for (int second = first + 1; second < count; second++)
            {
                if (second == first + 1 || (first == 0 && second == count - 1)) continue;
                (SvgDevicePoint secondStart, SvgDevicePoint secondEnd) = EdgeAt(points, start, count, second);
                if (secondStart == secondEnd) continue;
                flags |= TestEdgePair(firstStart, firstEnd, secondStart, secondEnd, ref pairs, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return flags;
    }

    /// <summary>Classifies every contour and every contact between distinct contours as one union.</summary>
    internal static SvgContourFlags ClassifySet(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, contourEnds);
        SvgContourFlags flags = SvgContourFlags.None;
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            int start = contour == 0 ? 0 : contourEnds[contour - 1];
            flags |= ClassifyContour(points, start, contourEnds[contour], cancellationToken);
        }

        int pairs = 0;
        for (int first = 0; first < contourEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : contourEnds[first - 1];
            int firstCount = contourEnds[first] - firstStart;
            for (int second = first + 1; second < contourEnds.Length; second++)
            {
                int secondStart = contourEnds[second - 1];
                int secondCount = contourEnds[second] - secondStart;
                for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
                {
                    (SvgDevicePoint firstEdgeStart, SvgDevicePoint firstEdgeEnd) = EdgeAt(points, firstStart, firstCount, firstEdge);
                    if (firstEdgeStart == firstEdgeEnd) continue;
                    for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
                    {
                        (SvgDevicePoint secondEdgeStart, SvgDevicePoint secondEdgeEnd) = EdgeAt(points, secondStart, secondCount, secondEdge);
                        if (secondEdgeStart == secondEdgeEnd) continue;
                        SvgContourFlags hit = TestEdgePair(firstEdgeStart, firstEdgeEnd, secondEdgeStart, secondEdgeEnd, ref pairs, cancellationToken);
                        if (hit == SvgContourFlags.ProperCrossing || hit == SvgContourFlags.SelfTouch) hit = SvgContourFlags.TouchesOtherContour;
                        flags |= hit;
                    }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return flags;
    }

    /// <summary>Finds the first interior crossing between non-adjacent edges of one closed contour slice.</summary>
    /// <remarks>Uses the same adjacency exclusions as classification: neighboring edges and the closing pair never count. Strict opposite orientation signs on both sides guarantee an interior intersection, which is verified in unit coordinates before reporting. Observes cancellation at the pair checkpoint; allocates nothing and holds no budget.</remarks>
    internal static bool TryFindProperCrossing(ReadOnlySpan<SvgDevicePoint> points, int start, int end, CancellationToken cancellationToken, out int firstEdge, out int secondEdge, out SvgDevicePoint crossing)
    {
        return TryFindProperCrossingCore(points, start, end, -1, -1, cancellationToken, out firstEdge, out secondEdge, out crossing);
    }

    /// <summary>Reports whether another interior crossing exists apart from one excluded edge pair.</summary>
    /// <remarks>Shares the core scan with the finder; edge indices below zero never match, so negative sentinels disable the exclusion. Observes cancellation at the pair checkpoint; allocates nothing and holds no budget.</remarks>
    internal static bool HasAnotherProperCrossing(ReadOnlySpan<SvgDevicePoint> points, int start, int end, int excludeFirst, int excludeSecond, CancellationToken cancellationToken)
    {
        return TryFindProperCrossingCore(points, start, end, excludeFirst, excludeSecond, cancellationToken, out _, out _, out _);
    }

    /// <summary>Finds one polygon vertex strictly inside a non-incident edge of any contour slice.</summary>
    /// <remarks>Vertex-on-edge contact mirrors the classifier's touch rule: zero orientation with the point inside the other bounding box, excluding shared endpoints by value so corners and welded vertices never report. Deterministic first-found order over loops, vertices and edges; callers charge one validation work unit per examined pair.</remarks>
    internal static bool TryFindVertexOnEdge(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, out int loop, out int edge, out int vertex, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, contourEnds);
        loop = -1;
        edge = -1;
        vertex = -1;
        int pairs = 0;
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            int start = contour == 0 ? 0 : contourEnds[contour - 1];
            int end = contourEnds[contour];
            for (int vertexIndex = start; vertexIndex < end; vertexIndex++)
            {
                SvgDevicePoint candidate = points[vertexIndex];
                for (int other = 0; other < contourEnds.Length; other++)
                {
                    int otherStart = other == 0 ? 0 : contourEnds[other - 1];
                    int otherEnd = contourEnds[other];
                    int otherCount = otherEnd - otherStart;
                    for (int edgeIndex = 0; edgeIndex < otherCount; edgeIndex++)
                    {
                        if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        (SvgDevicePoint edgeStart, SvgDevicePoint edgeEnd) = EdgeAt(points, otherStart, otherCount, edgeIndex);
                        if (edgeStart == edgeEnd) continue;
                        if (candidate == edgeStart || candidate == edgeEnd) continue;
                        if (Orient(edgeStart, edgeEnd, candidate) != 0 || !WithinBox(candidate, edgeStart, edgeEnd)) continue;
                        loop = other;
                        edge = edgeIndex;
                        vertex = vertexIndex;
                        return true;
                    }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>Finds one shared endpoint value between non-adjacent edges of one closed contour slice.</summary>
    /// <remarks>Mirrors the classifier's adjacency exclusions: neighboring edges and the closing pair never count, and zero-length edges are skipped. Any of the four endpoint combinations may report; callers split the ring at the two occurrences. Deterministic first-found order; callers charge one validation work unit per examined pair.</remarks>
    internal static bool TryFindSelfTouch(ReadOnlySpan<SvgDevicePoint> points, int start, int end, out int firstEdge, out int secondEdge, out SvgDevicePoint touch, CancellationToken cancellationToken)
    {
        if (start < 0 || start > points.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start || end > points.Length) throw new ArgumentOutOfRangeException(nameof(end));
        firstEdge = -1;
        secondEdge = -1;
        touch = default;
        int count = end - start;
        int pairs = 0;
        for (int first = 0; first < count; first++)
        {
            (SvgDevicePoint firstStart, SvgDevicePoint firstEnd) = EdgeAt(points, start, count, first);
            if (firstStart == firstEnd) continue;
            for (int second = first + 1; second < count; second++)
            {
                if (second == first + 1 || (first == 0 && second == count - 1)) continue;
                if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                (SvgDevicePoint secondStart, SvgDevicePoint secondEnd) = EdgeAt(points, start, count, second);
                if (secondStart == secondEnd) continue;
                if (firstEnd == secondStart) { firstEdge = first; secondEdge = second; touch = firstEnd; return true; }
                if (firstEnd == secondEnd) { firstEdge = first; secondEdge = second; touch = firstEnd; return true; }
                if (firstStart == secondStart) { firstEdge = first; secondEdge = second; touch = firstStart; return true; }
                if (firstStart == secondEnd) { firstEdge = first; secondEdge = second; touch = firstStart; return true; }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>Finds one strict-interior crossing between edges of two distinct contour loops.</summary>
    /// <remarks>Loop pairs share the classifier bounding-box precheck, then strict opposite nonzero orientation signs on both sides plus the shared crossing computation, so endpoint contacts, collinear overlaps and touches never report. Deterministic first-found order over loop pairs, first edges and second edges; callers charge one validation work unit per examined pair.</remarks>
    internal static bool TryFindInterLoopProperCrossing(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, out int firstLoop, out int firstEdge, out int secondLoop, out int secondEdge, out SvgDevicePoint crossing, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, contourEnds);
        firstLoop = -1;
        firstEdge = -1;
        secondLoop = -1;
        secondEdge = -1;
        crossing = default;
        int pairs = 0;
        for (int first = 0; first < contourEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : contourEnds[first - 1];
            int firstCount = contourEnds[first] - firstStart;
            for (int second = first + 1; second < contourEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : contourEnds[second - 1];
                int secondCount = contourEnds[second] - secondStart;
                for (int firstIndex = 0; firstIndex < firstCount; firstIndex++)
                {
                    (SvgDevicePoint firstEdgeStart, SvgDevicePoint firstEdgeEnd) = EdgeAt(points, firstStart, firstCount, firstIndex);
                    if (firstEdgeStart == firstEdgeEnd) continue;
                    for (int secondIndex = 0; secondIndex < secondCount; secondIndex++)
                    {
                        if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        (SvgDevicePoint secondEdgeStart, SvgDevicePoint secondEdgeEnd) = EdgeAt(points, secondStart, secondCount, secondIndex);
                        if (secondEdgeStart == secondEdgeEnd) continue;
                        if (Math.Max(firstEdgeStart.X, firstEdgeEnd.X) < Math.Min(secondEdgeStart.X, secondEdgeEnd.X) || Math.Max(secondEdgeStart.X, secondEdgeEnd.X) < Math.Min(firstEdgeStart.X, firstEdgeEnd.X)) continue;
                        if (Math.Max(firstEdgeStart.Y, firstEdgeEnd.Y) < Math.Min(secondEdgeStart.Y, secondEdgeEnd.Y) || Math.Max(secondEdgeStart.Y, secondEdgeEnd.Y) < Math.Min(firstEdgeStart.Y, firstEdgeEnd.Y)) continue;
                        if (Orient(firstEdgeStart, firstEdgeEnd, secondEdgeStart) * Orient(firstEdgeStart, firstEdgeEnd, secondEdgeEnd) < 0 && Orient(secondEdgeStart, secondEdgeEnd, firstEdgeStart) * Orient(secondEdgeStart, secondEdgeEnd, firstEdgeEnd) < 0 && TryCrossingPoint(firstEdgeStart, firstEdgeEnd, secondEdgeStart, secondEdgeEnd, out SvgDevicePoint point))
                        {
                            firstLoop = first;
                            firstEdge = firstIndex;
                            secondLoop = second;
                            secondEdge = secondIndex;
                            crossing = point;
                            return true;
                        }
                    }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>Finds one positive-length collinear overlap between edges of two distinct contour loops that still needs endpoint subdivision.</summary>
    /// <remarks>Loop pairs share the classifier bounding-box precheck then require all-zero orientations with a dominant-axis interval test that separates positive-length coincidence from single-point contact; zero-length edges never report. Each reported overlap value always equals an endpoint of at least one of the two edges, so callers subdivide with bitwise copies and never compute points. Overlaps whose values already subdivide nothing, because every value coincides with the endpoints of any edge it lies strictly inside while occurring as a vertex, are skipped so subdivision sweeps keep progressing. Deterministic first-found order over loop pairs, first edges and second edges; observes cancellation at the pair checkpoint; allocates nothing and holds no budget.</remarks>
    internal static bool TryFindInterLoopCoincidentOverlap(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, out int firstLoop, out int firstEdge, out int secondLoop, out int secondEdge, out SvgDevicePoint overlapFirst, out SvgDevicePoint overlapSecond, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, contourEnds);
        firstLoop = -1;
        firstEdge = -1;
        secondLoop = -1;
        secondEdge = -1;
        overlapFirst = default;
        overlapSecond = default;
        static bool NeedsSubdivisionVertex(ReadOnlySpan<SvgDevicePoint> vertices, SvgDevicePoint edgeStart, SvgDevicePoint edgeEnd, SvgDevicePoint value)
        {
            if (value == edgeStart || value == edgeEnd) return false;
            if (!WithinBox(value, edgeStart, edgeEnd)) return false;
            for (int index = 0; index < vertices.Length; index++)
            {
                if (vertices[index] == value) return true;
            }
            return false;
        }

        int pairs = 0;
        for (int first = 0; first < contourEnds.Length; first++)
        {
            int firstStart = first == 0 ? 0 : contourEnds[first - 1];
            int firstCount = contourEnds[first] - firstStart;
            for (int second = first + 1; second < contourEnds.Length; second++)
            {
                int secondStart = second == 0 ? 0 : contourEnds[second - 1];
                int secondCount = contourEnds[second] - secondStart;
                for (int firstIndex = 0; firstIndex < firstCount; firstIndex++)
                {
                    (SvgDevicePoint firstEdgeStart, SvgDevicePoint firstEdgeEnd) = EdgeAt(points, firstStart, firstCount, firstIndex);
                    if (firstEdgeStart == firstEdgeEnd) continue;
                    for (int secondIndex = 0; secondIndex < secondCount; secondIndex++)
                    {
                        if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        (SvgDevicePoint secondEdgeStart, SvgDevicePoint secondEdgeEnd) = EdgeAt(points, secondStart, secondCount, secondIndex);
                        if (secondEdgeStart == secondEdgeEnd) continue;
                        if (Math.Max(firstEdgeStart.X, firstEdgeEnd.X) < Math.Min(secondEdgeStart.X, secondEdgeEnd.X) || Math.Max(secondEdgeStart.X, secondEdgeEnd.X) < Math.Min(firstEdgeStart.X, firstEdgeEnd.X)) continue;
                        if (Math.Max(firstEdgeStart.Y, firstEdgeEnd.Y) < Math.Min(secondEdgeStart.Y, secondEdgeEnd.Y) || Math.Max(secondEdgeStart.Y, secondEdgeEnd.Y) < Math.Min(firstEdgeStart.Y, firstEdgeEnd.Y)) continue;
                        if (Orient(firstEdgeStart, firstEdgeEnd, secondEdgeStart) != 0 || Orient(firstEdgeStart, firstEdgeEnd, secondEdgeEnd) != 0) continue;
                        if (Orient(secondEdgeStart, secondEdgeEnd, firstEdgeStart) != 0 || Orient(secondEdgeStart, secondEdgeEnd, firstEdgeEnd) != 0) continue;
                        double deltaX = RequireFinite(firstEdgeEnd.X - firstEdgeStart.X);
                        double deltaY = RequireFinite(firstEdgeEnd.Y - firstEdgeStart.Y);
                        double firstLow;
                        double firstHigh;
                        double secondLow;
                        double secondHigh;
                        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
                        {
                            firstLow = Math.Min(firstEdgeStart.X, firstEdgeEnd.X);
                            firstHigh = Math.Max(firstEdgeStart.X, firstEdgeEnd.X);
                            secondLow = Math.Min(secondEdgeStart.X, secondEdgeEnd.X);
                            secondHigh = Math.Max(secondEdgeStart.X, secondEdgeEnd.X);
                        }
                        else
                        {
                            firstLow = Math.Min(firstEdgeStart.Y, firstEdgeEnd.Y);
                            firstHigh = Math.Max(firstEdgeStart.Y, firstEdgeEnd.Y);
                            secondLow = Math.Min(secondEdgeStart.Y, secondEdgeEnd.Y);
                            secondHigh = Math.Max(secondEdgeStart.Y, secondEdgeEnd.Y);
                        }
                        if (Math.Min(firstHigh, secondHigh) <= Math.Max(firstLow, secondLow)) continue;
                        SvgDevicePoint foundFirst = default;
                        SvgDevicePoint foundSecond = default;
                        int found = 0;
                        CollectOverlapEndpoint(firstEdgeStart, secondEdgeStart, secondEdgeEnd, ref foundFirst, ref foundSecond, ref found);
                        CollectOverlapEndpoint(firstEdgeEnd, secondEdgeStart, secondEdgeEnd, ref foundFirst, ref foundSecond, ref found);
                        CollectOverlapEndpoint(secondEdgeStart, firstEdgeStart, firstEdgeEnd, ref foundFirst, ref foundSecond, ref found);
                        CollectOverlapEndpoint(secondEdgeEnd, firstEdgeStart, firstEdgeEnd, ref foundFirst, ref foundSecond, ref found);
                        if (found != 2) continue;
                        if (!NeedsSubdivisionVertex(points, firstEdgeStart, firstEdgeEnd, foundFirst) && !NeedsSubdivisionVertex(points, firstEdgeStart, firstEdgeEnd, foundSecond) && !NeedsSubdivisionVertex(points, secondEdgeStart, secondEdgeEnd, foundFirst) && !NeedsSubdivisionVertex(points, secondEdgeStart, secondEdgeEnd, foundSecond)) continue;
                        firstLoop = first;
                        firstEdge = firstIndex;
                        secondLoop = second;
                        secondEdge = secondIndex;
                        overlapFirst = foundFirst;
                        overlapSecond = foundSecond;
                        return true;
                    }
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>Adds one endpoint value to the overlap pair when it lies inside the other segment.</summary>
    private static void CollectOverlapEndpoint(SvgDevicePoint candidate, SvgDevicePoint segmentStart, SvgDevicePoint segmentEnd, ref SvgDevicePoint foundFirst, ref SvgDevicePoint foundSecond, ref int found)
    {
        if (!WithinBox(candidate, segmentStart, segmentEnd)) return;
        if (found > 0 && foundFirst == candidate) return;
        if (found > 1 && foundSecond == candidate) return;
        if (found == 0) foundFirst = candidate;
        else if (found == 1) foundSecond = candidate;
        found++;
    }

    /// <summary>Reports whether a vertex lies on the boundary of one closed contour slice.</summary>
    /// <remarks>Zero orientation with containment in the edge box reports contact while zero-length edges never do; boundary points are the union drop and splice gate exclusions. Deterministic edge order; allocates nothing and holds no budget.</remarks>
    internal static bool IsPointOnLoopBoundary(ReadOnlySpan<SvgDevicePoint> points, int start, int end, SvgDevicePoint candidate)
    {
        int count = end - start;
        for (int edge = 0; edge < count; edge++)
        {
            (SvgDevicePoint edgeStart, SvgDevicePoint edgeEnd) = EdgeAt(points, start, count, edge);
            if (edgeStart == edgeEnd) continue;
            if (Orient(edgeStart, edgeEnd, candidate) == 0 && WithinBox(candidate, edgeStart, edgeEnd)) return true;
        }
        return false;
    }

    private static bool TryFindProperCrossingCore(ReadOnlySpan<SvgDevicePoint> points, int start, int end, int excludeFirst, int excludeSecond, CancellationToken cancellationToken, out int firstEdge, out int secondEdge, out SvgDevicePoint crossing)
    {
        if (start < 0 || start > points.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start || end > points.Length) throw new ArgumentOutOfRangeException(nameof(end));
        int count = end - start;
        int pairs = 0;
        for (int first = 0; first < count; first++)
        {
            (SvgDevicePoint firstStart, SvgDevicePoint firstEnd) = EdgeAt(points, start, count, first);
            if (firstStart == firstEnd) continue;
            for (int second = first + 1; second < count; second++)
            {
                if (second == first + 1 || (first == 0 && second == count - 1)) continue;
                if (first == excludeFirst && second == excludeSecond) continue;
                (SvgDevicePoint secondStart, SvgDevicePoint secondEnd) = EdgeAt(points, start, count, second);
                if (secondStart == secondEnd) continue;
                if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (Math.Max(firstStart.X, firstEnd.X) < Math.Min(secondStart.X, secondEnd.X) || Math.Max(secondStart.X, secondEnd.X) < Math.Min(firstStart.X, firstEnd.X)) continue;
                if (Math.Max(firstStart.Y, firstEnd.Y) < Math.Min(secondStart.Y, secondEnd.Y) || Math.Max(secondStart.Y, secondEnd.Y) < Math.Min(firstStart.Y, firstEnd.Y)) continue;
                if (Orient(firstStart, firstEnd, secondStart) * Orient(firstStart, firstEnd, secondEnd) < 0 && Orient(secondStart, secondEnd, firstStart) * Orient(secondStart, secondEnd, firstEnd) < 0 && TryCrossingPoint(firstStart, firstEnd, secondStart, secondEnd, out SvgDevicePoint point))
                {
                    firstEdge = first;
                    secondEdge = second;
                    crossing = point;
                    return true;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        firstEdge = -1;
        secondEdge = -1;
        crossing = default;
        return false;
    }

    /// <summary>Computes the interior intersection of two straddling segments, refusing degenerate quotients.</summary>
    private static bool TryCrossingPoint(SvgDevicePoint firstStart, SvgDevicePoint firstEnd, SvgDevicePoint secondStart, SvgDevicePoint secondEnd, out SvgDevicePoint crossing)
    {
        double ux = RequireFinite(firstEnd.X - firstStart.X);
        double uy = RequireFinite(firstEnd.Y - firstStart.Y);
        double vx = RequireFinite(secondEnd.X - secondStart.X);
        double vy = RequireFinite(secondEnd.Y - secondStart.Y);
        double denominator = RequireFinite(ux * vy - uy * vx);
        if (denominator == 0)
        {
            crossing = default;
            return false;
        }

        double firstNumerator = RequireFinite((secondStart.X - firstStart.X) * vy - (secondStart.Y - firstStart.Y) * vx);
        double secondNumerator = RequireFinite((secondStart.X - firstStart.X) * uy - (secondStart.Y - firstStart.Y) * ux);
        double firstPosition = firstNumerator / denominator;
        double secondPosition = secondNumerator / denominator;
        if (!double.IsFinite(firstPosition) || !double.IsFinite(secondPosition) || firstPosition <= 0 || firstPosition >= 1 || secondPosition <= 0 || secondPosition >= 1)
        {
            crossing = default;
            return false;
        }

        crossing = new SvgDevicePoint(RequireFinite(firstStart.X + firstPosition * ux), RequireFinite(firstStart.Y + firstPosition * uy));
        return true;
    }

    private static (SvgDevicePoint From, SvgDevicePoint To) EdgeAt(ReadOnlySpan<SvgDevicePoint> points, int start, int count, int edge)
    {
        return (points[start + edge], points[start + (edge + 1) % count]);
    }

    private static SvgContourFlags TestEdgePair(SvgDevicePoint firstStart, SvgDevicePoint firstEnd, SvgDevicePoint secondStart, SvgDevicePoint secondEnd, ref int pairs, CancellationToken cancellationToken)
    {
        if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
        if (Math.Max(firstStart.X, firstEnd.X) < Math.Min(secondStart.X, secondEnd.X) || Math.Max(secondStart.X, secondEnd.X) < Math.Min(firstStart.X, firstEnd.X)) return SvgContourFlags.None;
        if (Math.Max(firstStart.Y, firstEnd.Y) < Math.Min(secondStart.Y, secondEnd.Y) || Math.Max(secondStart.Y, secondEnd.Y) < Math.Min(firstStart.Y, firstEnd.Y)) return SvgContourFlags.None;
        double firstToSecondStart = Orient(firstStart, firstEnd, secondStart);
        double firstToSecondEnd = Orient(firstStart, firstEnd, secondEnd);
        double secondToFirstStart = Orient(secondStart, secondEnd, firstStart);
        double secondToFirstEnd = Orient(secondStart, secondEnd, firstEnd);
        if (firstToSecondStart != 0 || firstToSecondEnd != 0 || secondToFirstStart != 0 || secondToFirstEnd != 0)
        {
            if (firstToSecondStart * firstToSecondEnd < 0 && secondToFirstStart * secondToFirstEnd < 0) return SvgContourFlags.ProperCrossing;
            if ((firstToSecondStart == 0 && WithinBox(secondStart, firstStart, firstEnd)) || (firstToSecondEnd == 0 && WithinBox(secondEnd, firstStart, firstEnd))) return SvgContourFlags.SelfTouch;
            if ((secondToFirstStart == 0 && WithinBox(firstStart, secondStart, secondEnd)) || (secondToFirstEnd == 0 && WithinBox(firstEnd, secondStart, secondEnd))) return SvgContourFlags.SelfTouch;
            return SvgContourFlags.None;
        }

        double deltaX = RequireFinite(firstEnd.X - firstStart.X);
        double deltaY = RequireFinite(firstEnd.Y - firstStart.Y);
        double firstLow;
        double firstHigh;
        double secondLow;
        double secondHigh;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            firstLow = Math.Min(firstStart.X, firstEnd.X);
            firstHigh = Math.Max(firstStart.X, firstEnd.X);
            secondLow = Math.Min(secondStart.X, secondEnd.X);
            secondHigh = Math.Max(secondStart.X, secondEnd.X);
        }
        else
        {
            firstLow = Math.Min(firstStart.Y, firstEnd.Y);
            firstHigh = Math.Max(firstStart.Y, firstEnd.Y);
            secondLow = Math.Min(secondStart.Y, secondEnd.Y);
            secondHigh = Math.Max(secondStart.Y, secondEnd.Y);
        }

        double overlapLow = Math.Max(firstLow, secondLow);
        double overlapHigh = Math.Min(firstHigh, secondHigh);
        if (overlapHigh > overlapLow) return SvgContourFlags.CoincidentOverlap;
        if (overlapHigh == overlapLow) return SvgContourFlags.SelfTouch;
        return SvgContourFlags.None;
    }

    private static bool WithinBox(SvgDevicePoint point, SvgDevicePoint segmentStart, SvgDevicePoint segmentEnd)
    {
        return point.X >= Math.Min(segmentStart.X, segmentEnd.X) && point.X <= Math.Max(segmentStart.X, segmentEnd.X) && point.Y >= Math.Min(segmentStart.Y, segmentEnd.Y) && point.Y <= Math.Max(segmentStart.Y, segmentEnd.Y);
    }

    private static double Orient(SvgDevicePoint start, SvgDevicePoint end, SvgDevicePoint point)
    {
        return RequireFinite((end.X - start.X) * (point.Y - start.Y) - (end.Y - start.Y) * (point.X - start.X));
    }

    private static double RequireFinite(double value)
    {
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour structure exceeds the admitted numeric range.");
        return value;
    }

    /// <summary>Reports whether split loops share at most common vertices with no other contact or partial overlap.</summary>
    /// <remarks>
    /// <para>Every loop pair shares a bounding-box precheck per edge pair, then orientation predicates: a proper crossing rejects outright, an endpoint lying strictly inside the other segment rejects as a touch beyond shared vertices, and a positive-length collinear overlap rejects as coincidence. Endpoint coincidences are admissible only for bitwise-equal shared split vertices, and the closing edge of each loop counts like any other. Containment runs after the edge scan over non-shared vertices with half-open ray casting: mutual containment rejects as partial overlap, one-sided containment admits nesting for role resolution, and none admits disjoint siblings.</para>
    /// <para>Ordering matters: the edge scan runs first, so a vertex lying exactly on the other loop reaches containment only as a shared vertex, where ray casting would decide arbitrarily. Predicates evaluate in double arithmetic and stay exact for integer-scale geometry; every finite input decides deterministically, and non-finite intermediates fail with LimitExceeded. Loops with fewer than three vertices reject. Observes cancellation at the pair checkpoint, allocates nothing, and holds no budget; callers charge the quadratic pair estimate up front.</para>
    /// </remarks>
    internal static bool AreSplitLoopsAdmissible(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, ends);
        for (int loop = 0; loop < ends.Length; loop++)
        {
            int loopStart = loop == 0 ? 0 : ends[loop - 1];
            if (ends[loop] - loopStart < 3) return false;
        }

        int pairs = 0;
        for (int first = 0; first < ends.Length; first++)
        {
            int firstStart = first == 0 ? 0 : ends[first - 1];
            int firstCount = ends[first] - firstStart;
            for (int second = first + 1; second < ends.Length; second++)
            {
                int secondStart = second == 0 ? 0 : ends[second - 1];
                int secondCount = ends[second] - secondStart;
                for (int firstEdge = 0; firstEdge < firstCount; firstEdge++)
                {
                    (SvgDevicePoint firstEdgeStart, SvgDevicePoint firstEdgeEnd) = EdgeAt(points, firstStart, firstCount, firstEdge);
                    if (firstEdgeStart == firstEdgeEnd) return false;
                    for (int secondEdge = 0; secondEdge < secondCount; secondEdge++)
                    {
                        if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        (SvgDevicePoint secondEdgeStart, SvgDevicePoint secondEdgeEnd) = EdgeAt(points, secondStart, secondCount, secondEdge);
                        if (secondEdgeStart == secondEdgeEnd) return false;
                        if (LoopEdgesOverlapBeyondSharedVertices(firstEdgeStart, firstEdgeEnd, secondEdgeStart, secondEdgeEnd))
                        {
                            if (EdgesShareExactSpan(firstEdgeStart, firstEdgeEnd, secondEdgeStart, secondEdgeEnd)
                                && (LoopVerticesWithinLoop(points, firstStart, firstCount, secondStart, secondCount, ref pairs, cancellationToken)
                                    || LoopVerticesWithinLoop(points, secondStart, secondCount, firstStart, firstCount, ref pairs, cancellationToken))) continue;
                            return false;
                        }
                    }
                }

                if (LoopPairOverlapsPartially(points, firstStart, firstCount, secondStart, secondCount, ref pairs, cancellationToken)) return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    /// <summary>Reports whether two edges span the same segment in either direction.</summary>
    private static bool EdgesShareExactSpan(SvgDevicePoint firstStart, SvgDevicePoint firstEnd, SvgDevicePoint secondStart, SvgDevicePoint secondEnd)
    {
        return (firstStart == secondStart && firstEnd == secondEnd) || (firstStart == secondEnd && firstEnd == secondStart);
    }

    /// <summary>Reports whether every vertex of one loop is shared with, inside, or on the boundary of another.</summary>
    /// <remarks>Nested span-sharing loops keep hole and island roles through the fan path, so the guard admits them while adjacent sharing stays rejected for union surgery. Deterministic vertex order; observes cancellation through the shared pair counter.</remarks>
    private static bool LoopVerticesWithinLoop(ReadOnlySpan<SvgDevicePoint> points, int innerStart, int innerCount, int outerStart, int outerCount, ref int pairs, CancellationToken cancellationToken)
    {
        for (int index = 0; index < innerCount; index++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint candidate = points[innerStart + index];
            if (IsSharedVertex(candidate, points, outerStart, outerStart + outerCount)) continue;
            if (IsPointOnLoopBoundary(points, outerStart, outerStart + outerCount, candidate)) continue;
            if (SvgContourNesting.ContainsPoint(points, outerStart, outerStart + outerCount, candidate.X, candidate.Y)) continue;
            return false;
        }
        return true;
    }

    /// <summary>Reports whether two split-loop edges overlap beyond shared vertices.</summary>
    private static bool LoopEdgesOverlapBeyondSharedVertices(SvgDevicePoint firstStart, SvgDevicePoint firstEnd, SvgDevicePoint secondStart, SvgDevicePoint secondEnd)
    {
        if (Math.Max(firstStart.X, firstEnd.X) < Math.Min(secondStart.X, secondEnd.X) || Math.Max(secondStart.X, secondEnd.X) < Math.Min(firstStart.X, firstEnd.X)) return false;
        if (Math.Max(firstStart.Y, firstEnd.Y) < Math.Min(secondStart.Y, secondEnd.Y) || Math.Max(secondStart.Y, secondEnd.Y) < Math.Min(firstStart.Y, firstEnd.Y)) return false;
        double firstToSecondStart = Orient(firstStart, firstEnd, secondStart);
        double firstToSecondEnd = Orient(firstStart, firstEnd, secondEnd);
        double secondToFirstStart = Orient(secondStart, secondEnd, firstStart);
        double secondToFirstEnd = Orient(secondStart, secondEnd, firstEnd);
        if (firstToSecondStart != 0 && firstToSecondEnd != 0 && secondToFirstStart != 0 && secondToFirstEnd != 0)
        {
            return firstToSecondStart * firstToSecondEnd < 0 && secondToFirstStart * secondToFirstEnd < 0;
        }

        if (firstToSecondStart == 0 && WithinBox(secondStart, firstStart, firstEnd) && secondStart != firstStart && secondStart != firstEnd) return true;
        if (firstToSecondEnd == 0 && WithinBox(secondEnd, firstStart, firstEnd) && secondEnd != firstStart && secondEnd != firstEnd) return true;
        if (secondToFirstStart == 0 && WithinBox(firstStart, secondStart, secondEnd) && firstStart != secondStart && firstStart != secondEnd) return true;
        if (secondToFirstEnd == 0 && WithinBox(firstEnd, secondStart, secondEnd) && firstEnd != secondStart && firstEnd != secondEnd) return true;
        if (firstToSecondStart != 0 || firstToSecondEnd != 0 || secondToFirstStart != 0 || secondToFirstEnd != 0) return false;
        double deltaX = RequireFinite(firstEnd.X - firstStart.X);
        double deltaY = RequireFinite(firstEnd.Y - firstStart.Y);
        double firstLow;
        double firstHigh;
        double secondLow;
        double secondHigh;
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            firstLow = Math.Min(firstStart.X, firstEnd.X);
            firstHigh = Math.Max(firstStart.X, firstEnd.X);
            secondLow = Math.Min(secondStart.X, secondEnd.X);
            secondHigh = Math.Max(secondStart.X, secondEnd.X);
        }
        else
        {
            firstLow = Math.Min(firstStart.Y, firstEnd.Y);
            firstHigh = Math.Max(firstStart.Y, firstEnd.Y);
            secondLow = Math.Min(secondStart.Y, secondEnd.Y);
            secondHigh = Math.Max(secondStart.Y, secondEnd.Y);
        }

        double overlapLow = Math.Max(firstLow, secondLow);
        double overlapHigh = Math.Min(firstHigh, secondHigh);
        if (overlapHigh > overlapLow) return true;
        return overlapHigh == overlapLow && firstStart != secondStart && firstStart != secondEnd && firstEnd != secondStart && firstEnd != secondEnd;
    }

    /// <summary>Reports whether either loop contains the other only partially, ignoring shared or boundary vertices.</summary>
    private static bool LoopPairOverlapsPartially(ReadOnlySpan<SvgDevicePoint> points, int firstStart, int firstCount, int secondStart, int secondCount, ref int pairs, CancellationToken cancellationToken)
    {
        bool firstInSecond = false;
        bool secondInFirst = false;
        int firstEnd = firstStart + firstCount;
        int secondEnd = secondStart + secondCount;
        for (int index = firstStart; index < firstEnd && !(firstInSecond && secondInFirst); index++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (IsSharedVertex(points[index], points, secondStart, secondEnd)) continue;
            if (IsPointOnLoopBoundary(points, secondStart, secondEnd, points[index])) continue;
            if (SvgContourNesting.ContainsPoint(points, secondStart, secondEnd, points[index].X, points[index].Y)) firstInSecond = true;
        }

        for (int index = secondStart; index < secondEnd && !(firstInSecond && secondInFirst); index++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (IsSharedVertex(points[index], points, firstStart, firstEnd)) continue;
            if (IsPointOnLoopBoundary(points, firstStart, firstEnd, points[index])) continue;
            if (SvgContourNesting.ContainsPoint(points, firstStart, firstEnd, points[index].X, points[index].Y)) secondInFirst = true;
        }

        return firstInSecond && secondInFirst;
    }

    /// <summary>Reports whether either loop covers area strictly inside the other loop.</summary>
    /// <remarks>Strict vertices and edge midpoints test inside-or-outside with shared and boundary points excluded, so shared spans, endpoint contacts and span-touching nested loops never report by themselves; any report means positive-area overlap. Contained loops report too, so callers apply boundary-inclusive containment gating first. Deterministic vertex, edge and midpoint order; observes cancellation at the pair checkpoint; allocates nothing and holds no budget, with callers charging the quadratic pair estimate.</remarks>
    internal static bool LoopsOverlapStrictly(ReadOnlySpan<SvgDevicePoint> points, int firstStart, int firstCount, int secondStart, int secondCount, CancellationToken cancellationToken)
    {
        static bool StrictlyInside(ReadOnlySpan<SvgDevicePoint> vertices, int outerStart, int outerCount, SvgDevicePoint candidate)
        {
            if (IsSharedVertex(candidate, vertices, outerStart, outerStart + outerCount)) return false;
            if (IsPointOnLoopBoundary(vertices, outerStart, outerStart + outerCount, candidate)) return false;
            return SvgContourNesting.ContainsPoint(vertices, outerStart, outerStart + outerCount, candidate.X, candidate.Y);
        }

        int pairs = 0;
        for (int index = firstStart; index < firstStart + firstCount; index++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (StrictlyInside(points, secondStart, secondCount, points[index])) return true;
        }

        for (int index = secondStart; index < secondStart + secondCount; index++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (StrictlyInside(points, firstStart, firstCount, points[index])) return true;
        }

        for (int edge = 0; edge < firstCount; edge++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint edgeStart = points[firstStart + edge];
            SvgDevicePoint edgeEnd = points[firstStart + ((edge + 1) % firstCount)];
            if (edgeStart == edgeEnd) continue;
            SvgDevicePoint middle = new((edgeStart.X + edgeEnd.X) / 2, (edgeStart.Y + edgeEnd.Y) / 2);
            if (StrictlyInside(points, secondStart, secondCount, middle)) return true;
        }

        for (int edge = 0; edge < secondCount; edge++)
        {
            if ((pairs++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint edgeStart = points[secondStart + edge];
            SvgDevicePoint edgeEnd = points[secondStart + ((edge + 1) % secondCount)];
            if (edgeStart == edgeEnd) continue;
            SvgDevicePoint middle = new((edgeStart.X + edgeEnd.X) / 2, (edgeStart.Y + edgeEnd.Y) / 2);
            if (StrictlyInside(points, firstStart, firstCount, middle)) return true;
        }

        return false;
    }

    /// <summary>Reports whether a vertex matches any vertex of another loop by value.</summary>
    private static bool IsSharedVertex(SvgDevicePoint candidate, ReadOnlySpan<SvgDevicePoint> points, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (points[index] == candidate) return true;
        }

        return false;
    }


}
