// Managed adaptation of Skia raw control-point convexity classification.
// Copyright 2025 Google LLC.
// See THIRD_PARTY_NOTICES.txt for the BSD license and source revision.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Classifies the raw control polygon represented by one admitted retained contour.</summary>
internal static class SvgRawContourConvexity
{
    /// <summary>Checks float direction signs, turns and reversals including implicit closure.</summary>
    /// <remarks>The caller borrows immutable points and validated ordered, non-overlapping retained spans for one contour, and charges the complete traversal before calling. No storage is allocated or retained. State is local to this call; cancellation is checked at entry and every 1,024 raw points. This reproduces the pinned single-contour Convexicator, not native transformed-cache or multi-contour admission.</remarks>
    internal static bool IsConvex(ReadOnlySpan<SvgDevicePoint> points, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, CancellationToken cancellationToken)
        => IsConvex(points, quadSpans, cubicSpans, null, new(0, 0), cancellationToken);

    /// <summary>Classifies retained source controls after their bitmap-local float placement.</summary>
    /// <remarks>The caller charges traversal and point mapping before entry and retains immutable
    /// source storage. Mapping allocates nothing; cancellation and direction checks share the source classifier.</remarks>
    internal static bool IsConvexAfterPlacement(SvgPreparedPathContours source, SvgAffineTransform map, SvgCpuBitmapOrigin origin, CancellationToken token)
        => IsConvex(source.Points, source.QuadSpans, source.CubicSpans, map, origin, token);

    private static bool IsConvex(ReadOnlySpan<SvgDevicePoint> points, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans,
        SvgAffineTransform? map, SvgCpuBitmapOrigin origin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (points.IsEmpty) return true;
        TurnState state = new(Map(points[0]), cancellationToken);
        int quad = 0;
        int cubic = 0;
        for (int index = 0; index < points.Length; index++)
        {
            if (!state.AddPoint(Map(points[index]))) return false;
            if (quad < quadSpans.Length && quadSpans[quad].StartIndex == index)
            {
                SvgQuadSpan span = quadSpans[quad++];
                if (!state.AddPoint(Map(span.Control))) return false;
                index = span.EndIndex - 1;
            }
            else if (cubic < cubicSpans.Length && cubicSpans[cubic].StartIndex == index)
            {
                SvgCubicSpan span = cubicSpans[cubic++];
                if (!state.AddPoint(Map(span.FirstControl)) || !state.AddPoint(Map(span.SecondControl))) return false;
                index = span.EndIndex - 1;
            }
        }
        return state.Close();

        SvgDevicePoint Map(SvgDevicePoint point) => map is {} placement ? SvgAuthoredPath.MapPoint(placement, point, origin) : point;
    }

    private readonly record struct ControlPoint(float X, float Y);

    private struct TurnState
    {
        private readonly ControlPoint firstPoint;
        private readonly CancellationToken cancellationToken;
        private ControlPoint lastPoint;
        private ControlPoint firstVector;
        private ControlPoint lastVector;
        private int expectedTurn;
        private int reversals;
        private int lastSignX = 2;
        private int lastSignY = 2;
        private int changesX;
        private int changesY;
        private int visits;

        internal TurnState(SvgDevicePoint first, CancellationToken cancellationToken)
        {
            firstPoint = lastPoint = new((float)first.X, (float)first.Y);
            this.cancellationToken = cancellationToken;
        }

        internal bool AddPoint(SvgDevicePoint point)
        {
            if ((visits++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            ControlPoint current = new((float)point.X, (float)point.Y);
            if (!float.IsFinite(current.X) || !float.IsFinite(current.Y)) return false;
            if (current == lastPoint) return true;
            ControlPoint vector = new(current.X - lastPoint.X, current.Y - lastPoint.Y);
            if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y)) return false;
            int signX = vector.X < 0 ? 1 : 0;
            int signY = vector.Y < 0 ? 1 : 0;
            if (signX != lastSignX) changesX++;
            if (signY != lastSignY) changesY++;
            if (changesX > 3 || changesY > 3) return false;
            lastSignX = signX;
            lastSignY = signY;
            if (lastPoint == firstPoint && expectedTurn == 0 && lastVector == default)
                firstVector = lastVector = vector;
            else if (!AddVector(vector)) return false;
            lastPoint = current;
            return true;
        }

        internal bool Close() => AddPoint(new(firstPoint.X, firstPoint.Y)) && AddVector(firstVector);

        private bool AddVector(ControlPoint vector)
        {
            float firstProduct = lastVector.X * vector.Y;
            float secondProduct = lastVector.Y * vector.X;
            float cross = firstProduct - secondProduct;
            if (!float.IsFinite(cross)) return false;
            if (cross == 0)
            {
                float xProduct = lastVector.X * vector.X;
                float yProduct = lastVector.Y * vector.Y;
                float dot = xProduct + yProduct;
                // Straight vectors retain the previous direction; backwards vectors replace it.
                if (dot >= 0 || float.IsNaN(dot)) return true;
                lastVector = vector;
                return ++reversals < 3;
            }
            int turn = cross > 0 ? 1 : -1;
            if (expectedTurn == 0) expectedTurn = turn;
            else if (expectedTurn != turn) return false;
            lastVector = vector;
            return true;
        }
    }
}
