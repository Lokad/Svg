namespace Lokad.Svg.Rendering;

/// <summary>Resolves nesting parity signs for even-odd filling of explicit contours.</summary>
/// <remarks>
/// <para>Each contour scores plus one when an even number of other contours strictly contain its first
/// vertex and minus one otherwise, so nested holes alternate while siblings stay positive. Containment
/// uses half-open ray casting, which is deterministic but meaningless for touching or coincident
/// contours; those stay outside the admitted family. Degenerate contours under three points never count
/// as containers yet still receive a sign; they contribute no coverage either way.</para>
/// <para>This runs once per contour set, not per pixel: the wiring level resolves signs, then the
/// coverage kernel applies them. Callers charge one nesting work unit per contour pair; this resolver
/// holds no budget itself and allocates nothing.</para>
/// </remarks>
internal static class SvgContourNesting
{
    /// <summary>Writes plus or minus one per contour into the caller signs span.</summary>
    internal static void ComputeParitySigns(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, Span<int> signs, CancellationToken cancellationToken)
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
                if (ContainsPoint(points, otherStart, otherEnd, points[start].X, points[start].Y)) depth++;
            }

            signs[contour] = depth % 2 == 0 ? 1 : -1;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool ContainsPoint(ReadOnlySpan<SvgDevicePoint> points, int start, int end, double x, double y)
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