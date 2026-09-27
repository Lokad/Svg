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
}