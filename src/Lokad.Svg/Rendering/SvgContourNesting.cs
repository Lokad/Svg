namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Resolves nesting parity signs for even-odd filling of explicit contours.</summary>
/// <remarks>
/// <para>Each contour scores plus one when an even number of other contours strictly contain its first
/// vertex and minus one otherwise, so nested holes alternate while siblings stay positive. Containment
/// uses half-open ray casting, which is deterministic but meaningless for touching or coincident
/// contours; callers outside touch handling keep those outside the admitted family, while touch-resolved sets skip shared test vertices for strictly interior samples. Degenerate contours under three points never count
/// as containers yet still receive a sign; they contribute no coverage either way.</para>
/// <para>This runs once per contour set, not per pixel: the wiring level resolves signs, then the
/// coverage kernel applies them. Callers charge one nesting work unit per contour pair; this resolver
/// holds no budget itself and allocates nothing.</para>
/// </remarks>
internal static class SvgContourNesting
{
    /// <summary>Writes plus or minus one per contour into the caller signs span.</summary>
    internal static void ComputeParitySigns(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, Span<int> signs, bool skipSharedVertices, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, contourEnds);
        if (signs.Length != contourEnds.Length) throw new ArgumentOutOfRangeException(nameof(signs));
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int start = contour == 0 ? 0 : contourEnds[contour - 1];
            int depth = 0;
            for (int other = 0; other < contourEnds.Length; other++)
            {
                if (other == contour) continue;
                int otherStart = other == 0 ? 0 : contourEnds[other - 1];
                int otherEnd = contourEnds[other];
                if (otherEnd - otherStart < 3) continue;
                double testX = points[start].X;
                double testY = points[start].Y;
                if (skipSharedVertices && !TryFindUnsharedTestVertex(points, start, contourEnds[contour], otherStart, otherEnd, out testX, out testY)) continue;
                if (ContainsPoint(points, otherStart, otherEnd, testX, testY)) depth++;
            }

            signs[contour] = depth % 2 == 0 ? 1 : -1;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Writes nonzero-fill roles per contour into the caller roles span.</summary>
    /// <remarks>
    /// <para>Each contour scores plus one when its interior newly paints, minus one when it newly erases, and zero when the enclosing winding already decides the region: top-level contours always paint regardless of orientation, opposite-wound holes subtract, same-wound islands add nothing, and deeper stacks resolve through accumulated container orientations. Roles therefore make contour reversal unobservable while preserving holes, unlike raw signed accumulation, which cancels disjoint opposite contours sharing a pixel.</para>
    /// <para>Like parity signs this runs once per contour set, not per pixel: the wiring level resolves roles, then the coverage kernel applies them to unsigned fan areas. Containment reuses the same first-vertex test; when skipSharedVertices is set, test vertices shared with the tested contour are skipped for strictly interior samples, so point-touching sets resolved by touch handling combine correctly while all other inputs behave exactly as before. Degenerate contours score zero. Callers charge one nesting work unit per contour pair; this resolver holds no budget itself and allocates nothing.</para>
    /// </remarks>
    internal static void ComputeWindingRoles(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, Span<int> roles, bool skipSharedVertices, CancellationToken cancellationToken)
    {
        SvgPolygonCoverage.ValidateContourEnds(points, contourEnds);
        if (roles.Length != contourEnds.Length) throw new ArgumentOutOfRangeException(nameof(roles));
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int start = contour == 0 ? 0 : contourEnds[contour - 1];
            int end = contourEnds[contour];
            if (start >= points.Length)
            {
                roles[contour] = 0;
                continue;
            }

            int orientation = FullOrientation(points, start, end);
            int winding = 0;
            for (int other = 0; other < contourEnds.Length; other++)
            {
                if (other == contour) continue;
                int otherStart = other == 0 ? 0 : contourEnds[other - 1];
                int otherEnd = contourEnds[other];
                if (otherEnd - otherStart < 3) continue;
                double testX = points[start].X;
                double testY = points[start].Y;
                if (skipSharedVertices && !TryFindUnsharedTestVertex(points, start, end, otherStart, otherEnd, out testX, out testY)) continue;
                if (ContainsPoint(points, otherStart, otherEnd, testX, testY)) winding += FullOrientation(points, otherStart, otherEnd);
            }

            roles[contour] = Painted(winding + orientation) - Painted(winding);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Drops bitwise-identical duplicate contours beyond their first occurrence, reporting survivors.</summary>
    /// <remarks>
    /// <para>Coincident contours union exactly, so keeping one copy is coverage-identical while summing would double shared fringes. Touching, nested and crossing contours pass through untouched with their existing combination, as do rotated or reversed identicals (this comparison checks stored vertex order) and degenerate contours under three points (they contribute no coverage either way). Bitwise equality keeps NaN vertices unmerged. Callers charge one nesting work unit per contour pair; kept-vertex copies charge one work unit each.</para>
    /// </remarks>
    internal static bool MergeIdenticalContours(SvgDevicePoint[] points, int[] ends, ParserWorkCounter workCounter, out SvgDevicePoint[] mergedPoints, out int[] mergedEnds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(ends);
        ArgumentNullException.ThrowIfNull(workCounter);
        SvgPolygonCoverage.ValidateContourEnds(points, ends);
        bool[] keep = new bool[ends.Length];
        for (int contour = 0; contour < ends.Length; contour++) keep[contour] = true;
        bool merged = false;
        long pairs = 0;
        for (int first = 0; first < ends.Length; first++)
        {
            if (!keep[first]) continue;
            int firstStart = first == 0 ? 0 : ends[first - 1];
            int firstCount = ends[first] - firstStart;
            if (firstCount < 3) continue;
            for (int second = first + 1; second < ends.Length; second++)
            {
                if ((pairs++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!keep[second]) continue;
                int secondStart = second == 0 ? 0 : ends[second - 1];
                if (ends[second] - secondStart != firstCount) continue;
                bool identical = true;
                for (int vertex = 0; vertex < firstCount; vertex++)
                {
                    if (points[firstStart + vertex] != points[secondStart + vertex]) { identical = false; break; }
                }

                if (identical) { keep[second] = false; merged = true; }
            }
        }

        if (!merged)
        {
            mergedPoints = points;
            mergedEnds = ends;
            return false;
        }

        int keptPoints = 0;
        int keptEnds = 0;
        for (int contour = 0; contour < ends.Length; contour++)
        {
            if (!keep[contour]) continue;
            keptEnds++;
            keptPoints += ends[contour] - (contour == 0 ? 0 : ends[contour - 1]);
        }

        SvgDevicePoint[] compactedPoints = new SvgDevicePoint[keptPoints];
        int[] compactedEnds = new int[keptEnds];
        int target = 0;
        int targetEnd = 0;
        for (int contour = 0; contour < ends.Length; contour++)
        {
            if (!keep[contour]) continue;
            int start = contour == 0 ? 0 : ends[contour - 1];
            int count = ends[contour] - start;
            for (int vertex = 0; vertex < count; vertex++)
            {
                workCounter.Consume(cancellationToken);
                compactedPoints[target++] = points[start + vertex];
            }

            compactedEnds[targetEnd++] = target;
        }

        mergedPoints = compactedPoints;
        mergedEnds = compactedEnds;
        return true;
    }

    private static int Painted(int winding) => winding != 0 ? 1 : 0;

    private static int FullOrientation(ReadOnlySpan<SvgDevicePoint> points, int start, int end)
    {
        if (end - start < 3) return 0;
        double twice = 0;
        for (int index = start; index < end; index++)
        {
            SvgDevicePoint current = points[index];
            SvgDevicePoint next = index + 1 < end ? points[index + 1] : points[start];
            twice += current.X * next.Y - next.X * current.Y;
        }

        if (twice > 0) return 1;
        if (twice < 0) return -1;
        return 0;
    }

    /// <summary>Finds the first contour vertex that is neither bitwise-shared with nor lying on the tested contour range.</summary>
    /// <remarks>Touch-resolved sets share only vertices, so a vertex that is neither shared nor lying on the other loop boundary lies strictly inside or outside that loop once the split-loop guard passes; fully-shared loops have no robust test vertex and report false so callers treat them as uncontained. Boundary exclusion uses exact orientation plus box containment, mirroring the classifier touch rule.</remarks>
    private static bool TryFindUnsharedTestVertex(ReadOnlySpan<SvgDevicePoint> points, int start, int end, int otherStart, int otherEnd, out double x, out double y)
    {
        for (int index = start; index < end; index++)
        {
            bool shared = false;
            for (int other = otherStart; other < otherEnd; other++)
            {
                if (points[index] == points[other]) { shared = true; break; }
            }

            if (!shared && SvgContourStructure.IsPointOnLoopBoundary(points, otherStart, otherEnd, points[index]))
            {
                shared = true;
            }

            if (!shared)
            {
                x = points[index].X;
                y = points[index].Y;
                return true;
            }
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>Reports whether a point lies strictly inside one closed contour slice under half-open ray casting.</summary>
    /// <remarks>Boundary points decide arbitrarily, so callers exclude shared vertices before asking.</remarks>
    internal static bool ContainsPoint(ReadOnlySpan<SvgDevicePoint> points, int start, int end, double x, double y)
    {
        bool inside = false;
        for (int index = start; index < end; index++)
        {
            SvgDevicePoint first = points[index];
            SvgDevicePoint second = index + 1 < end ? points[index + 1] : points[start];
            if ((first.Y > y) != (second.Y > y))
            {
                double intersectX = RequireFinite(first.X + (second.X - first.X) * (y - first.Y) / (second.Y - first.Y));
                if (x < intersectX) inside = !inside;
            }
        }

        return inside;
    }

    private static double RequireFinite(double value)
    {
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Contour nesting exceeds the admitted numeric range.");
        return value;
    }
}
