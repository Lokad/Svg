using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Computes exact geometric coverage of simple closed integer polygons over device pixels.</summary>
/// <remarks>
/// <para>For one pixel square, returns the exact fractional intersection area (0 through 1) with the polygon,
/// triangulated as a fan from its first vertex with signed accumulation. The absolute value is taken at the
/// end, so clockwise and counter-clockwise orientations agree.</para>
/// <para>Contours with fewer than three points enclose no area and return 0. Self-intersecting contours are
/// outside this contract; contour validation rejects them before coverage runs.</para>
/// <para>This is pure geometry. Coverage-to-alpha quantization, opacity products and compositing remain
/// separate calibration concerns and must not be folded into these values.</para>
/// <para>Callers charge one raster work unit per evaluated pixel; this kernel holds no budget itself and
/// allocates nothing, clipping each fan triangle in fixed scratch spans.</para>
/// </remarks>
internal static class SvgPolygonCoverage
{
    /// <summary>Maximum vertices after clipping one triangle against four pixel half-planes (3 + 4).</summary>
    private const int ClippedVertexCapacity = 8;

    /// <summary>Returns the exact fractional area of the pixel square covered by the simple polygon.</summary>
    internal static double GetExactCoverage(ReadOnlySpan<SvgPoint> contour, int pixelX, int pixelY, CancellationToken cancellationToken)
    {
        if (contour.Length < 3) return 0;
        double left = pixelX;
        double top = pixelY;
        double right = left + 1;
        double bottom = top + 1;
        SvgPoint origin = contour[0];
        double signedArea = 0;
        for (int index = 1; index + 1 < contour.Length; index++)
        {
            if ((index & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            signedArea += ClipTriangleArea(origin, contour[index], contour[index + 1], left, top, right, bottom);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Math.Clamp(Math.Abs(signedArea), 0, 1);
    }

    private static double ClipTriangleArea(SvgPoint first, SvgPoint second, SvgPoint third, double left, double top, double right, double bottom)
    {
        Span<double> ping = stackalloc double[ClippedVertexCapacity * 2];
        Span<double> pong = stackalloc double[ClippedVertexCapacity * 2];
        ping[0] = first.X;
        ping[1] = first.Y;
        ping[2] = second.X;
        ping[3] = second.Y;
        ping[4] = third.X;
        ping[5] = third.Y;
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
