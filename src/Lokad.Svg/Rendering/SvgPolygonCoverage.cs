using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Computes exact geometric coverage of simple closed polygons over device pixels.</summary>
/// <remarks>
/// <para>For one pixel square, returns the exact fractional intersection area (0 through 1) with the polygon,
/// triangulated as a fan from its first vertex with signed accumulation. The absolute value is taken at the
/// end, so clockwise and counter-clockwise orientations agree.</para>
/// <para>Contours with fewer than three points enclose no area and return 0. Self-intersecting contours are
/// outside this contract; contour validation rejects them before coverage runs. Integer vertices convert to
/// device coordinates exactly.</para>
/// <para>This is pure geometry. Coverage-to-alpha quantization, opacity products and compositing remain
/// separate calibration concerns and must not be folded into these values.</para>
/// <para>Callers charge one raster work unit per evaluated pixel; this kernel holds no budget itself and
/// allocates nothing, clipping each fan triangle in fixed scratch spans.</para>
/// </remarks>
internal static class SvgPolygonCoverage
{
    /// <summary>Maximum vertices after clipping one triangle against four pixel half-planes (3 + 4).</summary>
    private const int ClippedVertexCapacity = 8;

    /// <summary>Returns the exact fractional area of the pixel square covered by the simple integer polygon.</summary>
    internal static double GetExactCoverage(ReadOnlySpan<SvgPoint> contour, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        if (contour.Length < 3) return 0;
        double left = pixelX;
        double top = pixelY;
        double signedArea = 0;
        double originX = contour[0].X;
        double originY = contour[0].Y;
        for (int index = 1; index + 1 < contour.Length; index++)
        {
            if ((index & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            signedArea += ClipTriangleArea(originX, originY, contour[index].X, contour[index].Y, contour[index + 1].X, contour[index + 1].Y, left, top);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Math.Clamp(Math.Abs(signedArea), 0, 1);
    }

    /// <summary>Returns the exact fractional area of the pixel square covered by the simple device-space polygon.</summary>
    internal static double GetExactCoverage(ReadOnlySpan<SvgDevicePoint> contour, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        if (contour.Length < 3) return 0;
        double signedArea = FanSignedArea(contour, 0, contour.Length, pixelX, pixelY, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Math.Clamp(Math.Abs(signedArea), 0, 1);
    }

    /// <summary>Returns the signed winding coverage of the pixel square across explicit contours.</summary>
    /// <remarks>
    /// <para>Each contour fans from its own first vertex with signed accumulation and no absolute value,
    /// so authored orientation is preserved: opposite-wound holes subtract, same-wound overlaps add, and
    /// nonzero fill tests the total against zero downstream. The ends partition the flat points exactly;
    /// empty and short contours contribute nothing. Exactness needs separated contour boundaries within
    /// the pixel; even-odd nesting resolution stays a wiring concern.</para>
    /// </remarks>
    internal static double GetWindingCoverage(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        ValidateContourEnds(points, contourEnds);
        double winding = 0;
        int start = 0;
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int end = contourEnds[contour];
            if (end - start >= 3) winding += FanSignedArea(points, start, end, pixelX, pixelY, cancellationToken);
            start = end;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return winding;
    }

    /// <summary>Validates that the ends ascend and partition the flat points exactly.</summary>
    internal static void ValidateContourEnds(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds)
    {
        int previous = 0;
        for (int index = 0; index < contourEnds.Length; index++)
        {
            if (contourEnds[index] < previous || contourEnds[index] > points.Length) throw new ArgumentOutOfRangeException(nameof(contourEnds));
            previous = contourEnds[index];
        }

        if (contourEnds.Length > 0 && previous != points.Length) throw new ArgumentOutOfRangeException(nameof(contourEnds));
    }

    /// <summary>Returns the fractional even-odd coverage of the pixel square across signed contours.</summary>
    /// <remarks>
    /// <para>Each contour contributes its absolute fan area times its caller parity sign, so orientation
    /// never matters and nested holes subtract under either winding. Signs come from nesting depth (see
    /// SvgContourNesting); zero masks a contour out, and any other value is rejected. Exactness needs
    /// separated contour boundaries within the pixel, matching the winding contract.</para>
    /// </remarks>
    /// <summary>Returns the nonzero-fill coverage of the pixel square across spatially resolved contour roles.</summary>
    /// <remarks>
    /// <para>Each contour contributes its unsigned fan area times its caller role, so top-level contours paint regardless of orientation, opposite-wound holes subtract and same-wound islands add nothing: disjoint opposite contours sharing a pixel add instead of cancelling. Roles come from SvgContourNesting in one wiring pass; zero masks a contour out, and any other value is rejected. Exactness needs separated contour boundaries within the pixel, matching the signed contract.</para>
    /// </remarks>
    internal static double GetWindingCoverage(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, ReadOnlySpan<int> roles, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        ValidateContourEnds(points, contourEnds);
        if (roles.Length != contourEnds.Length) throw new ArgumentOutOfRangeException(nameof(roles));
        double total = 0;
        int start = 0;
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int end = contourEnds[contour];
            int role = roles[contour];
            if (role is not (-1 or 0 or 1)) throw new ArgumentOutOfRangeException(nameof(roles));
            if (end - start >= 3 && role != 0) total += role * Math.Abs(FanSignedArea(points, start, end, pixelX, pixelY, cancellationToken));
            start = end;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Math.Clamp(total, 0, 1);
    }

    internal static double GetEvenOddCoverage(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> contourEnds, ReadOnlySpan<int> signs, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        ValidateContourEnds(points, contourEnds);
        if (signs.Length != contourEnds.Length) throw new ArgumentOutOfRangeException(nameof(signs));
        double total = 0;
        int start = 0;
        for (int contour = 0; contour < contourEnds.Length; contour++)
        {
            if ((contour & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int sign = signs[contour];
            if (sign is not (-1 or 0 or 1)) throw new ArgumentOutOfRangeException(nameof(signs));
            int end = contourEnds[contour];
            if (end - start >= 3 && sign != 0) total += sign * Math.Abs(FanSignedArea(points, start, end, pixelX, pixelY, cancellationToken));
            start = end;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Math.Clamp(Math.Abs(total), 0, 1);
    }

    private static double FanSignedArea(ReadOnlySpan<SvgDevicePoint> contour, int start, int end, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        double left = pixelX;
        double top = pixelY;
        double signedArea = 0;
        double originX = contour[start].X;
        double originY = contour[start].Y;
        for (int index = start + 1; index + 1 < end; index++)
        {
            if ((index & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            SvgDevicePoint second = contour[index];
            SvgDevicePoint third = contour[index + 1];
            signedArea += ClipTriangleArea(originX, originY, second.X, second.Y, third.X, third.Y, left, top);
        }

        return signedArea;
    }

    private static double ClipTriangleArea(double firstX, double firstY, double secondX, double secondY, double thirdX, double thirdY, double left, double top)
    {
        double right = left + 1;
        double bottom = top + 1;
        Span<double> ping = stackalloc double[ClippedVertexCapacity * 2];
        Span<double> pong = stackalloc double[ClippedVertexCapacity * 2];
        ping[0] = firstX;
        ping[1] = firstY;
        ping[2] = secondX;
        ping[3] = secondY;
        ping[4] = thirdX;
        ping[5] = thirdY;
        int count = ClipHalfPlane(ping[..6], pong, 0, left, true);
        count = ClipHalfPlane(pong[..(count * 2)], ping, 0, right, false);
        count = ClipHalfPlane(ping[..(count * 2)], pong, 1, top, true);
        count = ClipHalfPlane(pong[..(count * 2)], ping, 1, bottom, false);
        double twice = 0;
        for (int index = 0; index < count; index++)
        {
            double x0 = ping[index * 2];
            double y0 = ping[index * 2 + 1];
            double x1 = ping[((index + 1) % count) * 2];
            double y1 = ping[((index + 1) % count) * 2 + 1];
            twice += x0 * y1 - x1 * y0;
        }
        return twice / 2;
    }

    private static int ClipHalfPlane(ReadOnlySpan<double> input, Span<double> output, int axis, double edge, bool keepGreater)
    {
        int count = input.Length / 2;
        int kept = 0;
        for (int index = 0; index < count; index++)
        {
            double currentX = input[index * 2];
            double currentY = input[index * 2 + 1];
            double nextX = input[((index + 1) % count) * 2];
            double nextY = input[((index + 1) % count) * 2 + 1];
            double current = axis == 0 ? currentX : currentY;
            double next = axis == 0 ? nextX : nextY;
            bool currentInside = keepGreater ? current >= edge : current <= edge;
            bool nextInside = keepGreater ? next >= edge : next <= edge;
            if (nextInside)
            {
                if (!currentInside) AppendIntersection(output, ref kept, currentX, currentY, nextX, nextY, axis, edge);
                AppendPoint(output, ref kept, nextX, nextY);
            }
            else if (currentInside)
            {
                AppendIntersection(output, ref kept, currentX, currentY, nextX, nextY, axis, edge);
            }
        }
        return kept;
    }

    private static void AppendPoint(Span<double> output, ref int kept, double x, double y)
    {
        if (kept >= ClippedVertexCapacity) throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon coverage clipping exceeds the supported vertex range.");
        output[kept * 2] = x;
        output[kept * 2 + 1] = y;
        kept++;
    }

    private static void AppendIntersection(Span<double> output, ref int kept, double currentX, double currentY, double nextX, double nextY, int axis, double edge)
    {
        double current = axis == 0 ? currentX : currentY;
        double next = axis == 0 ? nextX : nextY;
        double t = (edge - current) / (next - current);
        AppendPoint(output, ref kept, currentX + t * (nextX - currentX), currentY + t * (nextY - currentY));
    }
}
