// Conic subdivision adapts SkGeometry.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2006 The Android Open Source Project.
namespace Lokad.Svg.Rendering;

/// <summary>Converts finite nonnegative rational quadratics with native float subdivision.</summary>
/// <remarks>Buffers belong to the caller. Conversion allocates no storage, preserves endpoints and
/// checks cancellation at every subdivision. The caller charges returned work before publishing any
/// geometry and reserves the bounded numerical phase before entering it. Concurrent calls are independent.</remarks>
internal static class SvgRationalQuadratic
{
    internal const int MaximumQuadCount = 32;
    internal const int MaximumPointCount = 2 * MaximumQuadCount + 1;
    internal const int MaximumConversionWork = 72;
    internal const float DeviceTolerance = 0.25f;
    internal const float QuarterCircleWeight = 0.707106781f;

    /// <summary>Writes interleaved start/control/end points and reports the actual subdivision work.</summary>
    internal static int Convert(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end,
        float weight, float tolerance, Span<SvgDevicePoint> output, out int work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!float.IsFinite(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!float.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        start = Narrow(start); control = Narrow(control); end = Narrow(end);
        float a = weight - 1f;
        float k = a / (4f * (2f + a));
        float x = k * ((float)start.X - 2f * (float)control.X + (float)end.X);
        float y = k * ((float)start.Y - 2f * (float)control.Y + (float)end.Y);
        float error = MathF.Sqrt(x * x + y * y);
        int depth = 0;
        work = 1;
        while (depth < 5 && !(error <= tolerance)) { error *= 0.25f; depth++; work++; }
        int count = 1 << depth;
        if (output.Length < 2 * count + 1) throw new SvgException(SvgErrorCode.LimitExceeded, "Conic conversion exceeds the caller point buffer.");
        output[0] = start;
        int next = 1;
        bool collapsed = false;
        if (depth == 5)
        {
            Split(start, control, end, weight, out SvgDevicePoint first, out SvgDevicePoint middle, out SvgDevicePoint second, out float _);
            work++;
            if (CannotNormalize(first, middle) && CannotNormalize(middle, second))
            {
                output[1] = output[2] = output[3] = first;
                output[4] = end;
                count = 2;
                next = 5;
                collapsed = true;
            }
        }
        if (!collapsed) Subdivide(start, control, end, weight, depth, output, ref next, ref work, token);
        // These bounded passes mirror the native final finite-point check.
        work++;
        for (int index = 0; index < next; index++)
        {
            if (float.IsFinite((float)output[index].X) && float.IsFinite((float)output[index].Y)) continue;
            work++;
            for (int interior = 1; interior < next - 1; interior++) output[interior] = control;
            break;
        }
        return count;

        static bool CannotNormalize(SvgDevicePoint first, SvgDevicePoint second)
        {
            float dx = (float)first.X - (float)second.X, dy = (float)first.Y - (float)second.Y;
            return !float.IsFinite(dx) || !float.IsFinite(dy) || dx == 0 && dy == 0;
        }

        static SvgDevicePoint Narrow(SvgDevicePoint point)
        {
            float x = (float)point.X, y = (float)point.Y;
            if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Conic coordinates exceed the finite float range.");
            return new(x, y);
        }
    }

    private static void Subdivide(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end,
        float weight, int depth, Span<SvgDevicePoint> output, ref int next, ref int work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        work++;
        if (depth == 0) { output[next++] = control; output[next++] = end; return; }
        Split(start, control, end, weight, out SvgDevicePoint first, out SvgDevicePoint middle, out SvgDevicePoint second, out float newWeight);
        // Keep native monotonic Y ordering after float cancellation.
        if (Between((float)start.Y, (float)control.Y, (float)end.Y))
        {
            if (!Between((float)start.Y, (float)middle.Y, (float)end.Y))
                middle = new(middle.X, MathF.Abs((float)middle.Y - (float)start.Y) < MathF.Abs((float)middle.Y - (float)end.Y) ? start.Y : end.Y);
            if (!Between((float)start.Y, (float)first.Y, (float)middle.Y)) first = new(first.X, start.Y);
            if (!Between((float)middle.Y, (float)second.Y, (float)end.Y)) second = new(second.X, end.Y);
        }
        Subdivide(start, first, middle, newWeight, depth - 1, output, ref next, ref work, token);
        Subdivide(middle, second, end, newWeight, depth - 1, output, ref next, ref work, token);

        static bool Between(float first, float value, float last) => (first - value) * (last - value) <= 0;
    }

    private static void Split(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end, float weight,
        out SvgDevicePoint first, out SvgDevicePoint middle, out SvgDevicePoint second, out float newWeight)
    {
        float scale = 1f / (1f + weight);
        // The legacy native expression matches the pinned browser's conic
        // coverage. Scaling each term before summing changes its float rounding.
        float bx = weight * (float)control.X, by = weight * (float)control.Y;
        first = new(((float)start.X + bx) * scale, ((float)start.Y + by) * scale);
        second = new((bx + (float)end.X) * scale, (by + (float)end.Y) * scale);
        middle = new(((float)start.X + 2f * bx + (float)end.X) * scale * 0.5f,
            ((float)start.Y + 2f * by + (float)end.Y) * scale * 0.5f);
        if (!float.IsFinite((float)middle.X) || !float.IsFinite((float)middle.Y))
        {
            double twiceWeight = (double)weight * 2;
            double halfScale = 0.5 / (1 + (double)weight);
            middle = new((float)((start.X + twiceWeight * control.X + end.X) * halfScale),
                (float)((start.Y + twiceWeight * control.Y + end.Y) * halfScale));
        }
        newWeight = MathF.Sqrt(0.5f + weight * 0.5f);
    }

}
