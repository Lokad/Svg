namespace Lokad.Svg.Rendering;

/// <summary>Expands device-space polylines into bounded stroke outlines with butt/round/square caps and miter/round joins.</summary>
/// <remarks>
/// <para>Joins miter within the caller limit and fall back to bevel, exactly the admitted miter-linejoin
/// semantics including its limit; reversals always bevel. Round joins arc the outer side through a flatness-bounded
/// fan while the inner side keeps the plain corner. Open ends close butt across the tips or arc a semicircular
/// cap. Curves, dashes and non-solid strokes stay outside this kernel. Callers passing curve-derived
/// polylines must bound flattening error before stroke expansion.</para>
/// <para>Chains use the math-convention perpendicular. Open loops seal one forward and one mirrored
/// backward chain into the stroked band. Closed loops emit one contour per side with opposite windings
/// (inner reversed), except degenerate centerlines: fewer than two distinct points draw nothing, and exactly two emit
/// the single boundary that fills under either fill rule. Spike reversals retrace both chains, which
/// nonzero winding still fills; route spike loops through contour-structure validation before even-odd
/// filling since coincident retracing cancels there. Acute inner turns may self-intersect with the same
/// nonzero caveat. Zero-length segments are skipped; all-degenerate solid open input draws nothing with butt caps,
/// a flatness-bounded disc with round caps, and an axis-aligned square with square caps per the zero-length subpath rule, while dashed outlining discards zero-length runs before stroking. This kernel holds
/// no budget itself and allocates nothing beyond the caller buffer it fills through.</para>
/// </remarks>
/// <summary>Selects the admitted open-end cap style for one stroke.</summary>
internal enum SvgStrokeCap
{
    /// <summary>Flat caps ending exactly at the end vertices.</summary>
    Butt,

    /// <summary>Semicircular caps extending half a width past the end vertices.</summary>
    Round,

    /// <summary>Flat caps extended half a width past the end vertices.</summary>
    Square,
}

/// <summary>Selects the admitted corner join style for one stroke.</summary>
internal enum SvgStrokeJoin
{
    /// <summary>Spiked joins within the miter limit with bevel fallback.</summary>
    Miter,

    /// <summary>Arc joins on the outer side with a plain inner corner.</summary>
    Round,

    /// <summary>Flat joins cutting the outer corner between the two offset points.</summary>
    Bevel,
}

internal static class SvgStrokeOutline
{
    /// <summary>Appends native float joins for a straight open centerline, preserving its directed winding overlaps.</summary>
    /// <remarks>Normalization, miter replacement and pivot inner joins follow SkPoint/SkStrokerPriv at the pinned source documented in THIRD_PARTY_NOTICES. Round caps, dots and joins retain rational conics. Inner arcs preserve the forward native quadrant splits before their verbs reverse. Callers retain this outline as NativeStrokeEdges rather than splitting it into touching filled bands. Curved, closed and dashed source contours keep their existing construction. The caller owns and bounds builder storage and charges appended points; this method adds no heap storage. Traversal and arc subdivision observe cancellation, and concurrent use of the builder is unsupported.</remarks>
    internal static void StrokeNativeOpenPolyline(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWidth(width);
        ValidateMiterLimit(miterLimit);
        float radius = (float)width * 0.5f;
        int first = 0;
        int last = points.Length - 1;
        while (first < last && Point(points[first]) == Point(points[first + 1]))
        {
            if ((first & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            first++;
        }
        while (last > first && Point(points[last]) == Point(points[last - 1]))
        {
            if ((last & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            last--;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (first >= last)
        {
            if (points.Length == 0 || cap == SvgStrokeCap.Butt) return;
            if (cap == SvgStrokeCap.Round)
            {
                SvgDevicePoint pivot = Point(points[first]);
                SvgDevicePoint right = new(RequireFinite((float)pivot.X + radius), pivot.Y);
                SvgDevicePoint left = new(RequireFinite((float)pivot.X - radius), pivot.Y);
                builder.BeginContour(right);
                Cap(builder, pivot, new(radius, 0), left, radius, cancellationToken);
                Cap(builder, pivot, new(-radius, 0), right, radius, cancellationToken);
                builder.EndContour();
                return;
            }
            Span<SvgDevicePoint> dot = stackalloc SvgDevicePoint[1];
            dot[0] = Point(points[first]);
            StrokeOpenPolyline(dot, radius * 2.0, miterLimit, cap, join, builder, cancellationToken);
            return;
        }
        SvgDevicePoint start = Point(points[first]);
        SvgDevicePoint end = Point(points[last]);
        SvgDevicePoint startDirection = Direction(start, Point(points[first + 1]));
        SvgDevicePoint endDirection = Direction(Point(points[last - 1]), end);
        SvgDevicePoint firstRight = Offset(start, startDirection, radius);
        SvgDevicePoint firstLeft = Offset(start, startDirection, -radius);
        SvgDevicePoint lastRight = Offset(end, endDirection, radius);
        SvgDevicePoint lastLeft = Offset(end, endDirection, -radius);
        if (cap == SvgStrokeCap.Square)
        {
            // Native square caps offset along the normal before extending the
            // tangent; exchanging these float additions changes the corners.
            firstRight = Shift(firstRight, startDirection, -radius);
            firstLeft = Shift(firstLeft, startDirection, -radius);
            lastRight = Shift(lastRight, endDirection, radius);
            lastLeft = Shift(lastLeft, endDirection, radius);
        }
        builder.BeginContour(firstRight);
        Chain(points, first, last, 1, radius, miterLimit, join, builder, cancellationToken);
        builder.AddLine(lastRight);
        if (cap == SvgStrokeCap.Round)
            Cap(builder, end, new(radius * (float)endDirection.Y, -radius * (float)endDirection.X), lastLeft, radius, cancellationToken);
        else builder.AddLine(lastLeft);
        Chain(points, last, first, -1, radius, miterLimit, join, builder, cancellationToken);
        builder.AddLine(firstLeft);
        if (cap == SvgStrokeCap.Round)
            Cap(builder, start, new(-radius * (float)startDirection.Y, radius * (float)startDirection.X), firstRight, radius, cancellationToken);
        builder.EndContour();

        static void Cap(SvgContourBuilder builder, SvgDevicePoint pivot, SvgDevicePoint normal, SvgDevicePoint stop, float radius, CancellationToken token)
        {
            // Original RoundCapper adds the parallel offset before the normal.
            float projectedX = (float)pivot.X - (float)normal.Y;
            float projectedY = (float)pivot.Y + (float)normal.X;
            SvgDevicePoint projected = new(RequireFinite(projectedX), RequireFinite(projectedY));
            int segments = ConicArcSegments(radius);
            builder.AddConic(new(RequireFinite(projectedX + (float)normal.X), RequireFinite(projectedY + (float)normal.Y)), projected,
                SvgRationalQuadratic.QuarterCircleWeight, segments, token);
            builder.AddConic(new(RequireFinite(projectedX - (float)normal.X), RequireFinite(projectedY - (float)normal.Y)), stop,
                SvgRationalQuadratic.QuarterCircleWeight, segments, token);
        }
        static SvgDevicePoint Point(SvgDevicePoint point) => new(RequireFinite((float)point.X), RequireFinite((float)point.Y));
        static SvgDevicePoint Direction(SvgDevicePoint before, SvgDevicePoint after)
        {
            float x = (float)after.X - (float)before.X;
            float y = (float)after.Y - (float)before.Y;
            double factor = 1 / Math.Sqrt((double)x * x + (double)y * y);
            return new(RequireFinite((float)(x * factor)), RequireFinite((float)(y * factor)));
        }
        static SvgDevicePoint Shift(SvgDevicePoint point, SvgDevicePoint direction, float radius)
        {
            float x = radius * (float)direction.X;
            float y = radius * (float)direction.Y;
            return new(RequireFinite((float)point.X + x), RequireFinite((float)point.Y + y));
        }
        static SvgDevicePoint Offset(SvgDevicePoint point, SvgDevicePoint direction, float radius)
            => Shift(point, new(direction.Y, -direction.X), radius);
        static void Chain(ReadOnlySpan<SvgDevicePoint> points, int first, int last, int step, float radius, double limit, SvgStrokeJoin join, SvgContourBuilder builder, CancellationToken token)
        {
            int previous = first;
            int vertex = first + step;
            while (vertex != last)
            {
                if ((vertex & 63) == 0) token.ThrowIfCancellationRequested();
                SvgDevicePoint pivot = Point(points[vertex]);
                int after = vertex + step;
                while (after != last && Point(points[after]) == pivot)
                {
                    if ((after & 63) == 0) token.ThrowIfCancellationRequested();
                    after += step;
                }
                if (Point(points[previous]) != pivot && Point(points[after]) != pivot)
                    Join(pivot, Direction(Point(points[previous]), pivot), Direction(pivot, Point(points[after])), radius, limit, join, step < 0, builder, token);
                previous = vertex;
                vertex = after;
            }
        }
        static void Join(SvgDevicePoint pivot, SvgDevicePoint incoming, SvgDevicePoint outgoing, float radius, double limit, SvgStrokeJoin join, bool reverse, SvgContourBuilder builder, CancellationToken token)
        {
            float beforeX = (float)incoming.Y, beforeY = -(float)incoming.X;
            float afterX = (float)outgoing.Y, afterY = -(float)outgoing.X;
            float dot = beforeX * afterX + beforeY * afterY;
            bool bevel = join == SvgStrokeJoin.Bevel || join == SvgStrokeJoin.Miter && (float)limit <= 1;
            if (!bevel && MathF.Abs(1 - dot) <= 1f / 4096) return;
            bool reversal = MathF.Abs(1 + dot) <= 1f / 4096;
            float cross = (float)incoming.X * (float)outgoing.Y - (float)incoming.Y * (float)outgoing.X;
            if (join == SvgStrokeJoin.Round ? (reverse ? cross < 0 : cross <= 0) : !reversal && cross < 0)
            {
                builder.AddLine(Offset(pivot, incoming, radius));
                builder.AddLine(pivot);
                builder.AddLine(Offset(pivot, outgoing, radius));
                return;
            }
            if (join == SvgStrokeJoin.Round)
            {
                Arc(builder, pivot, radius, new(beforeX, beforeY), new(afterX, afterY), reverse, token);
                return;
            }
            float inverseLimit = 1 / (float)limit;
            float sinHalf = MathF.Sqrt((1 + dot) / 2);
            if (bevel || reversal || sinHalf < inverseLimit)
            {
                builder.AddLine(Offset(pivot, incoming, radius));
                builder.AddLine(Offset(pivot, outgoing, radius));
                return;
            }
            float midX, midY;
            if (dot == 0 && inverseLimit <= 0.707106781f)
            {
                midX = (beforeX + afterX) * radius;
                midY = (beforeY + afterY) * radius;
            }
            else
            {
                float x = dot < 0 ? afterY - beforeY : beforeX + afterX;
                float y = dot < 0 ? beforeX - afterX : beforeY + afterY;
                float length = radius / sinHalf;
                double factor = length / Math.Sqrt((double)x * x + (double)y * y);
                midX = (float)(x * factor);
                midY = (float)(y * factor);
            }
            builder.AddLine(new(RequireFinite((float)pivot.X + midX), RequireFinite((float)pivot.Y + midY)));

            static void Arc(SvgContourBuilder builder, SvgDevicePoint pivot, float radius, SvgDevicePoint from, SvgDevicePoint to, bool reverse, CancellationToken token)
            {
                // Build the original forward inner arc before reversing its verbs.
                // Rebuilding from its other end moves the quadrant split and changes
                // native subdivision; reversing preserves both weights and controls.
                SvgDevicePoint unitStart = reverse ? to : from, unitStop = reverse ? from : to;
                float x = (float)unitStart.X * (float)unitStop.X + (float)unitStart.Y * (float)unitStop.Y;
                float y = (float)unitStart.X * (float)unitStop.Y - (float)unitStart.Y * (float)unitStop.X;
                if (MathF.Abs(y) <= 1f / 4096 && x > 0 && (reverse ? y <= 0 : y >= 0)) return;
                if (reverse) y = -y;
                int quadrant = 0;
                if (y == 0) quadrant = 2;
                else if (x == 0) quadrant = y > 0 ? 1 : 3;
                else
                {
                    if (y < 0) quadrant += 2;
                    if ((x < 0) != (y < 0)) quadrant++;
                }
                ReadOnlySpan<SvgDevicePoint> unit = stackalloc SvgDevicePoint[]
                {
                    new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0),
                    new(-1, -1), new(0, -1), new(1, -1), new(1, 0)
                };
                Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[9];
                Span<float> weights = stackalloc float[4];
                float scaleX = radius * (float)unitStart.X, scaleY = radius * (float)unitStart.Y;
                points[0] = Map(unit[0], pivot, scaleX, scaleY, reverse);
                int count = quadrant;
                for (int index = 0; index < count; index++)
                {
                    weights[index] = SvgRationalQuadratic.QuarterCircleWeight;
                    points[2 * index + 1] = Map(unit[2 * index + 1], pivot, scaleX, scaleY, reverse);
                    points[2 * index + 2] = Map(unit[2 * index + 2], pivot, scaleX, scaleY, reverse);
                }
                SvgDevicePoint last = unit[2 * quadrant];
                float dot = (float)last.X * x + (float)last.Y * y;
                if (float.IsNaN(dot)) return;
                if (dot < 1)
                {
                    float weight = MathF.Sqrt((1 + dot) * 0.5f);
                    float controlX = (float)last.X + x, controlY = (float)last.Y + y;
                    double factor = (1f / weight) / Math.Sqrt((double)controlX * controlX + (double)controlY * controlY);
                    controlX = (float)(controlX * factor); controlY = (float)(controlY * factor);
                    // The native two-argument tolerance predicate tests whether
                    // the difference can normalize, so finite differences use exact equality.
                    if ((float)last.X != controlX || (float)last.Y != controlY)
                    {
                        weights[count] = weight;
                        points[2 * count + 1] = Map(new(controlX, controlY), pivot, scaleX, scaleY, reverse);
                        points[2 * count + 2] = Map(new(x, y), pivot, scaleX, scaleY, reverse);
                        count++;
                    }
                }
                if (count == 0) return;
                int segments = ConicArcSegments(radius);
                builder.AddLine(points[reverse ? 2 * count : 0]);
                for (int index = reverse ? count - 1 : 0; reverse ? index >= 0 : index < count; index += reverse ? -1 : 1)
                    builder.AddConic(points[2 * index + 1], points[reverse ? 2 * index : 2 * index + 2], weights[index], segments, token);

                static SvgDevicePoint Map(SvgDevicePoint point, SvgDevicePoint pivot, float scaleX, float scaleY, bool reverse)
                    => new(RequireFinite(scaleX * (float)point.X + (reverse ? scaleY : -scaleY) * (float)point.Y + (float)pivot.X),
                        RequireFinite(scaleY * (float)point.X + (reverse ? -scaleX : scaleX) * (float)point.Y + (float)pivot.Y));
            }
        }

    }

    /// <summary>Reports whether a centerline holds fewer than two distinct points.</summary>
    /// <remarks>Dashed outlining discards zero-length runs before stroking while solid zero-length subpaths lower to cap-determined dots.</remarks>
    internal static bool IsAllIdentical(ReadOnlySpan<SvgDevicePoint> points)
    {
        for (int index = 1; index < points.Length; index++)
        {
            if (points[index] != points[index - 1]) return false;
        }

        return true;
    }

    /// <summary>Appends one sealed outline loop for an open polyline, a cap-determined dot when fully degenerate, or nothing for butt dots.</summary>
    internal static void StrokeOpenPolyline(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        int chordFirst = SkipDegenerate(points, 0);
        int chordLast = points.Length == 0 ? -1 : SkipDegenerateBackward(points, points.Length - 1);
        (double firstX, double firstY) = chordFirst >= chordLast ? (1, 0) : UnitDirection(points[chordFirst], points[SkipDegenerate(points, chordFirst + 1)]);
        (double lastX, double lastY) = chordFirst >= chordLast ? (1, 0) : UnitDirection(points[SkipDegenerateBackward(points, chordLast - 1)], points[chordLast]);
        StrokeOpenPolylineCore(points, width, miterLimit, cap, join, (firstX, firstY), (lastX, lastY), builder, cancellationToken);
    }

    /// <summary>Appends one sealed outline loop for an open polyline with caller-supplied end tangents for dash-cut caps.</summary>
    internal static void StrokeOpenPolyline(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, (double X, double Y) startTangent, (double X, double Y) endTangent, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        StrokeOpenPolylineCore(points, width, miterLimit, cap, join, startTangent, endTangent, builder, cancellationToken);
    }

    private static void StrokeOpenPolylineCore(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgStrokeCap cap, SvgStrokeJoin join, (double X, double Y) startTangent, (double X, double Y) endTangent, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWidth(width);
        ValidateMiterLimit(miterLimit);
        int first = SkipDegenerate(points, 0);
        if (first >= points.Length - 1)
        {
            if (points.Length == 0 || cap == SvgStrokeCap.Butt) return;
            SvgDevicePoint center = points[first];
            double dotHalfWidth = width / 2;
            if (cap == SvgStrokeCap.Round)
            {
                builder.BeginContour(new SvgDevicePoint(RequireFinite(center.X + dotHalfWidth), RequireFinite(center.Y)));
                EmitArcFan(builder, center, dotHalfWidth, (1, 0), (0, 1), (-1, 0), cancellationToken);
                EmitArcFan(builder, center, dotHalfWidth, (-1, 0), (0, -1), (1, 0), cancellationToken);
                builder.EndContour();
                return;
            }
            SvgDevicePoint squareMin = new SvgDevicePoint(RequireFinite(center.X - dotHalfWidth), RequireFinite(center.Y - dotHalfWidth));
            SvgDevicePoint squareMax = new SvgDevicePoint(RequireFinite(center.X + dotHalfWidth), RequireFinite(center.Y + dotHalfWidth));
            builder.BeginContour(new SvgDevicePoint(squareMin.X, squareMin.Y));
            builder.AddLine(new SvgDevicePoint(squareMax.X, squareMin.Y));
            builder.AddLine(new SvgDevicePoint(squareMax.X, squareMax.Y));
            builder.AddLine(new SvgDevicePoint(squareMin.X, squareMax.Y));
            builder.EndContour();
            return;
        }
        int last = SkipDegenerateBackward(points, points.Length - 1);
        double halfWidth = width / 2;
                SvgDevicePoint firstTip = points[first];
        SvgDevicePoint lastTip = points[last];
        if (cap == SvgStrokeCap.Square)
        {
            firstTip = new SvgDevicePoint(RequireFinite(points[first].X - startTangent.X * halfWidth), RequireFinite(points[first].Y - startTangent.Y * halfWidth));
            lastTip = new SvgDevicePoint(RequireFinite(points[last].X + endTangent.X * halfWidth), RequireFinite(points[last].Y + endTangent.Y * halfWidth));
        }
        builder.BeginContour(OffsetPoint(firstTip, (startTangent.X, startTangent.Y), halfWidth));
        EmitForwardChain(builder, points, first, last, halfWidth, miterLimit, join, cancellationToken);
        if (cap == SvgStrokeCap.Round) EmitArcFan(builder, points[last], halfWidth, (-endTangent.Y, endTangent.X), (endTangent.X, endTangent.Y), (endTangent.Y, -endTangent.X), cancellationToken);
        else
        {
            builder.AddLine(OffsetPoint(lastTip, (endTangent.X, endTangent.Y), halfWidth));
            builder.AddLine(OffsetPoint(lastTip, (endTangent.X, endTangent.Y), -halfWidth));
        }
        EmitBackwardChain(builder, points, first, last, halfWidth, miterLimit, join, cancellationToken);
        if (cap == SvgStrokeCap.Round) EmitArcFan(builder, points[first], halfWidth, (startTangent.Y, -startTangent.X), (-startTangent.X, -startTangent.Y), (-startTangent.Y, startTangent.X), cancellationToken);
        else builder.AddLine(OffsetPoint(firstTip, (-startTangent.X, -startTangent.Y), halfWidth));
        builder.EndContour();
    }

    /// <summary>Appends outer and inner sealed loops for a closed polyline, or nothing when degenerate.</summary>
    internal static void StrokeClosedPolyline(ReadOnlySpan<SvgDevicePoint> points, double width, double miterLimit, SvgStrokeJoin join, SvgContourBuilder builder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateWidth(width);
        ValidateMiterLimit(miterLimit);
        int distinct = CountDistinct(points);
        if (distinct < 2) return;
        double halfWidth = width / 2;
        EmitClosedChain(builder, points, halfWidth, miterLimit, join, false, distinct, cancellationToken);
        if (distinct > 2) EmitClosedChain(builder, points, halfWidth, miterLimit, join, true, distinct, cancellationToken);
    }

    private static void EmitForwardChain(SvgContourBuilder builder, ReadOnlySpan<SvgDevicePoint> points, int first, int last, double halfWidth, double miterLimit, SvgStrokeJoin join, CancellationToken cancellationToken)
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
            if ((directionX, directionY) != (nextX, nextY)) EmitJoin(builder, points[vertex], (directionX, directionY), (nextX, nextY), halfWidth, miterLimit, join, true, cancellationToken);
            directionX = nextX;
            directionY = nextY;
            segment = vertex;
        }
    }

    private static void EmitBackwardChain(SvgContourBuilder builder, ReadOnlySpan<SvgDevicePoint> points, int first, int last, double halfWidth, double miterLimit, SvgStrokeJoin join, CancellationToken cancellationToken)
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
            if ((directionX, directionY) != (nextX, nextY)) EmitJoin(builder, points[vertex], (directionX, directionY), (nextX, nextY), halfWidth, miterLimit, join, true, cancellationToken);
            directionX = nextX;
            directionY = nextY;
            segment = vertex;
        }
    }

    private static void EmitClosedChain(SvgContourBuilder builder, ReadOnlySpan<SvgDevicePoint> points, double halfWidth, double miterLimit, SvgStrokeJoin join, bool reverse, int distinct, CancellationToken cancellationToken)
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
            if ((directionX, directionY) != (nextX, nextY)) EmitJoin(builder, points[vertex], (directionX, directionY), (nextX, nextY), halfWidth, miterLimit, join, false, cancellationToken);
            directionX = nextX;
            directionY = nextY;
            segment = vertex;
        }

        if ((directionX, directionY) != (initialX, initialY)) EmitJoin(builder, points[start], (directionX, directionY), (initialX, initialY), halfWidth, miterLimit, join, false, cancellationToken);
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

    /// <summary>Bounds the chord deviation of one round arc fan segment, matching the curve flattening discipline.</summary>
    private const double ArcFlatness = 0.25;

    /// <summary>Returns the flatness-bounded segment count for one arc fan sweep, used for outline capacity planning. No chord spans more than thirty degrees, so small radii keep arc fidelity instead of collapsing to coarse chords.</summary>
    internal static int MaxArcFanSegments(double radius, double sweep)
    {
        double step = 2 * Math.Acos(Math.Clamp(1 - ArcFlatness / radius, -1, 1));
        step = System.Math.Min(step, System.Math.PI / 6);
        if (!double.IsFinite(step) || step <= 0) return 1;
        int segments = (int)Math.Ceiling(Math.Abs(sweep) / step);
        return Math.Max(1, segments);
    }

    /// <summary>Bounds fallback sampling of circular conics under uniform rational parameter steps.</summary>
    /// <remarks>A quarter circle turns at most 4*tan(pi/8) radians per unit parameter.
    /// Dividing by its pi/2 sweep gives less than 1.06, so this factor preserves the
    /// angular chord bound without doubling every fallback contour. Shorter arcs turn less.</remarks>
    private static int ConicArcSegments(double radius) => (int)Math.Ceiling(1.06 * MaxArcFanSegments(radius, Math.PI / 2));

    /// <summary>Emits the polygonal fan from one unit offset through another around a stroke vertex.</summary>
    private static void EmitArcFan(SvgContourBuilder builder, SvgDevicePoint center, double radius, (double X, double Y) from, (double X, double Y) thru, (double X, double Y) to, CancellationToken cancellationToken)
    {
        double start = Math.Atan2(from.Y, from.X);
        double end = Math.Atan2(to.Y, to.X);
        double mid = Math.Atan2(thru.Y, thru.X);
        double sweep = NormalizeAngle(end - start);
        double relative = NormalizeAngle(mid - start);
        if ((sweep > 0 && (relative < 0 || relative > sweep)) || (sweep < 0 && (relative > 0 || relative < sweep))) sweep += sweep > 0 ? -2 * Math.PI : 2 * Math.PI;
        int segments = MaxArcFanSegments(radius, sweep);
        for (int index = 0; index <= segments; index++)
        {
            if ((index & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            double angle = start + sweep * index / segments;
            builder.AddLine(new SvgDevicePoint(RequireFinite(center.X + radius * Math.Cos(angle)), RequireFinite(center.Y + radius * Math.Sin(angle))));
        }
    }

    private static double NormalizeAngle(double value)
    {
        while (value <= -Math.PI) value += 2 * Math.PI;
        while (value > Math.PI) value -= 2 * Math.PI;
        return value;
    }

    private static void EmitJoin(SvgContourBuilder builder, SvgDevicePoint vertex, (double X, double Y) incoming, (double X, double Y) outgoing, double halfWidth, double miterLimit, SvgStrokeJoin join, bool openContour, CancellationToken cancellationToken)
    {
        double dot = incoming.X * outgoing.X + incoming.Y * outgoing.Y;
        double denominator = 1 + dot;
        double outgoingNormalX = -outgoing.Y;
        double outgoingNormalY = outgoing.X;
        double cross = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
        if (join == SvgStrokeJoin.Bevel)
        {
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * (-incoming.Y)), RequireFinite(vertex.Y + halfWidth * incoming.X)));
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * outgoingNormalX), RequireFinite(vertex.Y + halfWidth * outgoingNormalY)));
            return;
        }
        if (join == SvgStrokeJoin.Round && denominator > 0 && cross < 0)
        {
            EmitArcFan(builder, vertex, halfWidth, (-incoming.Y, incoming.X), (-incoming.Y + outgoingNormalX, incoming.X + outgoingNormalY), (outgoingNormalX, outgoingNormalY), cancellationToken);
            return;
        }
        if (join == SvgStrokeJoin.Round && denominator <= 0)
        {
            EmitArcFan(builder, vertex, halfWidth, (-incoming.Y, incoming.X), incoming, (incoming.Y, -incoming.X), cancellationToken);
            return;
        }
        if (denominator <= 0)
        {
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * (-incoming.Y)), RequireFinite(vertex.Y + halfWidth * incoming.X)));
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * outgoingNormalX), RequireFinite(vertex.Y + halfWidth * outgoingNormalY)));
            return;
        }

        double normalSumX = -incoming.Y + outgoingNormalX;
        double normalSumY = incoming.X + outgoingNormalY;
        // The emitted miter tip sits at halfWidth * (n1 + n2) / (1 + dot), so the tested ratio is the full displacement-to-half-width length, compared directly to the caller limit.
        double ratio = RequireFinite(System.Math.Sqrt(normalSumX * normalSumX + normalSumY * normalSumY) / denominator);
        if (openContour && (join == SvgStrokeJoin.Miter || join == SvgStrokeJoin.Round))
        {
            // Open bands union per-segment quads with outer join wedges on right turns within ninety degrees: both chains close butt at the vertex, and the outer chain additionally spikes through the miter tip, tracing the wedge ear. Wider inner turns keep the classic miter vertex, which the acute oracle matches, and closed loops keep single-vertex joins on both sides.
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * (-incoming.Y)), RequireFinite(vertex.Y + halfWidth * incoming.X)));
            if ((cross > 0 && dot >= 0) || ratio > miterLimit)
            {
                builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * outgoingNormalX), RequireFinite(vertex.Y + halfWidth * outgoingNormalY)));
                return;
            }

            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * normalSumX / denominator), RequireFinite(vertex.Y + halfWidth * normalSumY / denominator)));
            builder.AddLine(new SvgDevicePoint(RequireFinite(vertex.X + halfWidth * outgoingNormalX), RequireFinite(vertex.Y + halfWidth * outgoingNormalY)));
            return;
        }
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

    internal static (double X, double Y) UnitDirection(SvgDevicePoint from, SvgDevicePoint to)
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
