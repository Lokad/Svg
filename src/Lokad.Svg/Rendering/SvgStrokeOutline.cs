namespace Lokad.Svg.Rendering;

/// <summary>Expands device-space polylines into bounded butt-capped stroke outlines.</summary>
/// <remarks>
/// <para>Joins miter within the caller limit and fall back to bevel, exactly the admitted miter-linejoin
/// semantics including its limit; reversals always bevel. Open ends close butt across the tips. Curves,
/// round caps and joins, dashes and non-solid strokes stay outside this kernel: flatten curves first if
/// desired, with the fidelity of flattened-then-offset curves owned as an S11 concern.</para>
/// <para>Chains use the math-convention perpendicular. Open loops seal one forward and one mirrored
/// backward chain into the stroked band. Closed loops emit one contour per side with opposite windings
/// (inner reversed), except degenerate centerlines: fewer than two distinct points draw nothing, and exactly two emit
/// the single boundary that fills under either fill rule. Spike reversals retrace both chains, which
/// nonzero winding still fills; route spike loops through contour-structure validation before even-odd
/// filling since coincident retracing cancels there. Acute inner turns may self-intersect with the same
/// nonzero caveat. Zero-length segments are skipped, and all-degenerate butt input draws nothing per the
/// zero-length rule. Callers charge one stroke-expansion work unit per appended point; this kernel holds
/// no budget itself and allocates nothing beyond the caller buffer it fills through.</para>
/// </remarks>
internal static class SvgStrokeOutline
{
    /// <summary>Appends one sealed butt-capped outline loop for an open polyline, or nothing when degenerate.</summary>
    internal static void StrokeOpenPolyline(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWidth(width);
        ValidateMiterLimit(miterLimit);
        int first = SkipDegenerate(points, 0);
        if (first >= points.Length - 1) return;
        int last = SkipDegenerateBackward(points, points.Length - 1);
        double halfWidth = width / 2;
        (double firstX, double firstY) = UnitDirection(points[first], points[SkipDegenerate(points, first + 1)]);
        (double lastX, double lastY) = UnitDirection(points[SkipDegenerateBackward(points, last - 1)], points[last]);
        builder.BeginContour(OffsetPoint(points[first], (firstX, firstY), halfWidth));
        EmitForwardChain(builder, points, first, last, halfWidth, miterLimit, cancellationToken);
        builder.AddLine(OffsetPoint(points[last], (lastX, lastY), halfWidth));
        builder.AddLine(OffsetPoint(points[last], (lastX, lastY), -halfWidth));
        EmitBackwardChain(builder, points, first, last, halfWidth, miterLimit, cancellationToken);
        builder.AddLine(OffsetPoint(points[first], (-firstX, -firstY), halfWidth));
        builder.EndContour();
    }

    /// <summary>Appends outer and inner sealed loops for a closed polyline, or nothing when degenerate.</summary>
    internal static void StrokeClosedPolyline(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWidth(width);
        ValidateMiterLimit(miterLimit);
        int distinct = CountDistinct(points);
        if (distinct < 2) return;
        double halfWidth = width / 2;
        EmitClosedChain(builder, points, halfWidth, miterLimit, false, distinct, cancellationToken);
        if (distinct > 2) EmitClosedChain(builder, points, halfWidth, miterLimit, true, distinct, cancellationToken);
    }

    private static void EmitForwardChain(SvgContourBuilder builder, ReadOnlySpan<SvgDevicePoint> points, int first, int last, double halfWidth, double miterLimit, CancellationToken cancellationToken)
    {
        int segment = first;
        (double directionX, double directionY) = UnitDirection(points[segment], points[SkipDegenerate(points, segment + 1)]);
        int operations = 0;
        while (true)
        {
            if ((operations++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int vertex = SkipDegenerate(points, segment + 1);
            if (vertex >= last) break;
            int after = SkipDegenerate(points, vertex + 1);
            (double nextX, double nextY) = UnitDirection(points[vertex], points[after]);
            if ((directionX, directionY) != (nextX, nextY)) EmitJoin(builder, points[vertex], (directionX, directionY), (nextX, nextY), halfWidth, miterLimit);
            directionX = nextX;
            directionY = nextY;
            segment = vertex;
        }
    }

    private static void EmitBackwardChain(SvgContourBuilder builder, ReadOnlySpan<SvgDevicePoint> points, int first, int last, double halfWidth, double miterLimit, CancellationToken cancellationToken)
    {
        int segment = last;
        (double directionX, double directionY) = UnitDirection(points[last], points[SkipDegenerateBackward(points, last - 1)]);
        int operations = 0;
        while (true)
        {
            if ((operations++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int vertex = SkipDegenerateBackward(points, segment - 1);
            if (vertex <= first) break;
            int before = SkipDegenerateBackward(points, vertex - 1);
            (double nextX, double nextY) = UnitDirection(points[vertex], points[before]);
            if ((directionX, directionY) != (nextX, nextY)) EmitJoin(builder, points[vertex], (directionX, directionY), (nextX, nextY), halfWidth, miterLimit);
            directionX = nextX;
            directionY = nextY;
            segment = vertex;
        }
    }

    private static void EmitClosedChain(SvgContourBuilder builder, ReadOnlySpan<SvgDevicePoint> points, double halfWidth, double miterLimit, bool reverse, int distinct, CancellationToken cancellationToken)
    {
        int start = SkipDegenerate(points, 0);
        (double initialX, double initialY) = ClosedDirection(points, start, reverse);
        builder.BeginContour(OffsetPoint(points[start], (initialX, initialY), halfWidth));
        double directionX = initialX;
        double directionY = initialY;
        int segment = start;
        int remaining = distinct;
        int operations = 0;
        while (true)
        {
            if ((operations++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            int vertex = reverse ? SkipDegenerateBackwardWrap(points, segment) : SkipDegenerateForwardWrap(points, segment);
            if (vertex == start || vertex == segment || remaining-- <= 0) break;
            (double nextX, double nextY) = ClosedDirection(points, vertex, reverse);
            if ((directionX, directionY) != (nextX, nextY)) EmitJoin(builder, points[vertex], (directionX, directionY), (nextX, nextY), halfWidth, miterLimit);
            directionX = nextX;
            directionY = nextY;
            segment = vertex;
        }

        if ((directionX, directionY) != (initialX, initialY)) EmitJoin(builder, points[start], (directionX, directionY), (initialX, initialY), halfWidth, miterLimit);
        cancellationToken.ThrowIfCancellationRequested();
        builder.EndContour();
    }

    private static (double X, double Y) ClosedDirection(ReadOnlySpan<SvgDevicePoint> points, int vertex, bool reverse)
    {
        if (!reverse)
        {
            int after = vertex + 1 < points.Length ? SkipDegenerate(points, vertex + 1) : points.Length;
            if (after >= points.Length) after = SkipDegenerate(points, 0);
            return UnitDirection(points[vertex], points[after]);
        }

        int before = vertex - 1;
        if (before < 0) before = points.Length - 1;
        for (int scanned = 0; scanned < points.Length - 1 && points[before] == points[vertex]; scanned++)
        {
            before--;
            if (before < 0) before = points.Length - 1;
        }

        return UnitDirection(points[vertex], points[before]);
    }

    private static int SkipDegenerateForwardWrap(ReadOnlySpan<SvgDevicePoint> points, int edge)
    {
        int vertex = edge + 1;
        if (vertex >= points.Length) vertex = 0;
        while (true)
        {
            int next = vertex + 1 < points.Length ? vertex + 1 : 0;
            if (points[vertex] != points[next]) return vertex;
            if (next == edge) return edge;
            vertex = next;
        }
    }

    private static int SkipDegenerateBackwardWrap(ReadOnlySpan<SvgDevicePoint> points, int edge)
    {
        int vertex = edge - 1;
        if (vertex < 0) vertex = points.Length - 1;
        while (true)
        {
            int next = vertex - 1 >= 0 ? vertex - 1 : points.Length - 1;
            if (points[vertex] != points[next]) return vertex;
            if (next == edge) return edge;
            vertex = next;
        }
    }

    private static int CountDistinct(ReadOnlySpan<SvgDevicePoint> points)
    {
        if (points.Length == 0) return 0;
        int count = 0;
        for (int index = 0; index < points.Length; index++)
        {
            SvgDevicePoint current = points[index];
            SvgDevicePoint next = points[(index + 1) % points.Length];
            if (current != next) count++;
        }

        return count;
    }

    private static void EmitJoin(SvgContourBuilder builder, SvgDevicePoint vertex, (double X, double Y) incoming, (double X, double Y) outgoing, double halfWidth, double miterLimit)
    {
        double dot = incoming.X * outgoing.X + incoming.Y * outgoing.Y;
        double denominator = 1 + dot;
        double outgoingNormalX = -outgoing.Y;
        double outgoingNormalY = outgoing.X;
        if (denominator <= 0)
        {
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * (-incoming.Y)), RequireFinite(vertex.Y + halfWidth * incoming.X)));
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * outgoingNormalX), RequireFinite(vertex.Y + halfWidth * outgoingNormalY)));
            return;
        }

        double normalSumX = -incoming.Y + outgoingNormalX;
        double normalSumY = incoming.X + outgoingNormalY;
        double ratio = RequireFinite(System.Math.Sqrt(normalSumX * normalSumX + normalSumY * normalSumY) / (2 * denominator));
        if (ratio > miterLimit)
        {
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * (-incoming.Y)), RequireFinite(vertex.Y + halfWidth * incoming.X)));
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * outgoingNormalX), RequireFinite(vertex.Y + halfWidth * outgoingNormalY)));
            return;
        }

        builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * normalSumX / denominator), RequireFinite(vertex.Y + halfWidth * normalSumY / denominator)));
    }

    private static int SkipDegenerate(ReadOnlySpan<SvgDevicePoint> points, int index)
    {
        while (index + 1 < points.Length && points[index] == points[index + 1]) index++;
        return index;
    }

    private static int SkipDegenerateBackward(ReadOnlySpan<SvgDevicePoint> points, int index)
    {
        while (index - 1 >= 0 && points[index] == points[index - 1]) index--;
        return index;
    }

    private static (double X, double Y) UnitDirection(SvgDevicePoint from, SvgDevicePoint to)
    {
        double deltaX = RequireFinite(to.X - from.X);
        double deltaY = RequireFinite(to.Y - from.Y);
        double length = RequireFinite(System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
        return (deltaX / length, deltaY / length);
    }

    private static SvgDevicePoint OffsetPoint(SvgDevicePoint point, (double X, double Y) direction, double halfWidth)
    {
        return new SvgDevicePoint(RequireFinite(point.X - halfWidth * direction.Y), RequireFinite(point.Y + halfWidth * direction.X));
    }

    private static void ValidateWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    }

    private static void ValidateMiterLimit(double miterLimit)
    {
        if (!double.IsFinite(miterLimit) || miterLimit < 1) throw new ArgumentOutOfRangeException(nameof(miterLimit));
    }

    private static double RequireFinite(double value)
    {
        if (!double.IsFinite(value)) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke outline exceeds the admitted numeric range.");
        return value;
    }
}