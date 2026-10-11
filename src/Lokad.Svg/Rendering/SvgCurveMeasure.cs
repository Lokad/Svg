// Adapts SkContourMeasure.cpp and SkGeometry.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2006 The Android Open Source Project; 2018 Google LLC.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

internal enum SvgMeasuredCurveKind { Line, Quadratic, Conic, Cubic }
internal readonly record struct SvgCurveMeasureStep(float Distance, int Parameter);

/// <summary>Measures and cuts original float curves using the native contour-distance rules.</summary>
/// <remarks>The caller owns all input/output buffers. Measurement has at most eight recursive levels
/// and 256 distance records per curve. Polynomial subdivision and conic evaluation allocate no heap
/// storage. Work is charged before each recursive visit, lookup and bounded cut phase, with cancellation
/// checks throughout. Outputs may be partial on denial and must remain private until success. Calls are independent.</remarks>
internal static class SvgCurveMeasure
{
    internal const int MaximumStepCount = 256;
    internal const int MaximumParameter = 0x3fffffff;

    internal static int Measure(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> points, float weight,
        float resolutionScale, float initialDistance, Span<SvgCurveMeasureStep> output, out float distance,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        Validate(kind, points, weight);
        if (kind == SvgMeasuredCurveKind.Conic && weight == 0) weight = 1;
        if (!(resolutionScale > 0) || !float.IsFinite(resolutionScale)) throw new ArgumentOutOfRangeException(nameof(resolutionScale));
        if (initialDistance < 0 || !float.IsFinite(initialDistance)) throw new ArgumentOutOfRangeException(nameof(initialDistance));
        int count = 0;
        distance = initialDistance;
        float tolerance = .5f * (1f / resolutionScale);
        if (kind == SvgMeasuredCurveKind.Line) Append(points[0], points[1], MaximumParameter, output, ref distance, ref count, work, token);
        else if (kind == SvgMeasuredCurveKind.Conic)
            WalkConic(points, weight, points[0], points[2], 0, MaximumParameter, 0, tolerance, output, ref distance, ref count, work, token);
        else WalkPolynomial(kind, points, 0, MaximumParameter, 0, tolerance, output, ref distance, ref count, work, token);
        return count;
    }

    /// <summary>Finds the native float parameter within one curve's cumulative distance records.</summary>
    internal static float ParameterAt(ReadOnlySpan<SvgCurveMeasureStep> steps, float initialDistance, float distance,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (steps.IsEmpty || !float.IsFinite(distance) || distance < initialDistance || distance > steps[^1].Distance)
            throw new ArgumentOutOfRangeException(nameof(distance));
        int low = 0, high = steps.Length - 1;
        while (low < high)
        {
            work.Consume(token);
            int middle = (low + high) >> 1;
            if (steps[middle].Distance < distance) low = middle + 1; else high = middle;
        }
        float startT = low == 0 ? 0 : ScalarParameter(steps[low - 1].Parameter);
        float startD = low == 0 ? initialDistance : steps[low - 1].Distance;
        float endT = ScalarParameter(steps[low].Parameter);
        return startT + (endT - startT) * (distance - startD) / (steps[low].Distance - startD);
    }

    /// <summary>Writes the move point and one native curve verb for a unit parameter interval.</summary>
    /// <remarks>Returns false only when native conic normalization rejects nonfinite pieces. Equal parameters
    /// return a zero-length line, preserving cap-determined dash dots. The move point uses original polynomial
    /// evaluation; it need not equal the chopped polynomial's intermediate endpoint after float rounding.</remarks>
    internal static bool Cut(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> points, float weight,
        float start, float stop, Span<SvgDevicePoint> output, out SvgMeasuredCurveKind resultKind, out float resultWeight,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        Validate(kind, points, weight);
        if (kind == SvgMeasuredCurveKind.Conic && weight == 0) weight = 1;
        if (!float.IsFinite(start) || !float.IsFinite(stop) || start < 0 || stop > 1 || start > stop)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (output.Length < 4) throw new ArgumentException("A curve cut requires four caller-owned point slots.", nameof(output));
        for (int phase = 0; phase < 16; phase++) work.Consume(token);
        resultKind = kind; resultWeight = weight;
        SvgDevicePoint move = Evaluate(kind, points, weight, start);
        if (start == stop) { resultKind = SvgMeasuredCurveKind.Line; output[0] = output[1] = move; return true; }
        if (kind == SvgMeasuredCurveKind.Line)
        {
            output[0] = move;
            output[1] = stop == 1 ? points[1] : Mix(points[0], points[1], stop);
            return true;
        }
        if (kind == SvgMeasuredCurveKind.Conic)
        {
            bool valid = CutConic(points, weight, start, stop, output, out resultWeight);
            output[0] = move;
            return valid;
        }
        int pointCount = kind == SvgMeasuredCurveKind.Quadratic ? 3 : 4;
        Span<SvgDevicePoint> first = stackalloc SvgDevicePoint[7], second = stackalloc SvgDevicePoint[7];
        if (start == 0)
        {
            if (stop == 1) points[..pointCount].CopyTo(output);
            else { ChopPolynomial(kind, points, stop, first); first[..pointCount].CopyTo(output); }
        }
        else
        {
            ChopPolynomial(kind, points, start, first);
            ReadOnlySpan<SvgDevicePoint> tail = first.Slice(pointCount - 1, pointCount);
            if (stop == 1) tail.CopyTo(output);
            else { ChopPolynomial(kind, tail, (stop - start) / (1 - start), second); second[..pointCount].CopyTo(output); }
        }
        output[0] = move;
        return true;

        static bool CutConic(ReadOnlySpan<SvgDevicePoint> p, float weight, float start, float stop,
            Span<SvgDevicePoint> result, out float newWeight)
        {
            newWeight = weight;
            if (start == 0 && stop == 1) { p[..3].CopyTo(result); return true; }
            if (start == 0 || stop == 1)
            {
                float t = start == 0 ? stop : start;
                Homogeneous a = new((float)p[0].X, (float)p[0].Y, 1);
                Homogeneous b = new((float)p[1].X * weight, (float)p[1].Y * weight, weight);
                Homogeneous c = new((float)p[2].X, (float)p[2].Y, 1);
                Homogeneous ab = Blend(a, b, t), bc = Blend(b, c, t), middle = Blend(ab, bc, t);
                float root = MathF.Sqrt(middle.Z);
                SvgDevicePoint left = ab.Project(), center = middle.Project(), right = bc.Project();
                float leftWeight = ab.Z / root, rightWeight = bc.Z / root;
                // The native two-piece chop rejects either nonfinite half, even when the other is requested.
                if (!Finite(left) || !Finite(center) || !Finite(right) || !float.IsFinite(leftWeight) || !float.IsFinite(rightWeight)) return false;
                if (start == 0) { result[0] = p[0]; result[1] = left; result[2] = center; newWeight = leftWeight; }
                else { result[0] = center; result[1] = right; result[2] = p[2]; newWeight = rightWeight; }
                return true;
            }
            Homogeneous aValue = ConicPolynomial(p, weight, start), cValue = ConicPolynomial(p, weight, stop);
            Homogeneous midpoint = ConicPolynomial(p, weight, (start + stop) / 2);
            Homogeneous control = new(2 * midpoint.X - (aValue.X + cValue.X) * .5f,
                2 * midpoint.Y - (aValue.Y + cValue.Y) * .5f, 2 * midpoint.Z - (aValue.Z + cValue.Z) * .5f);
            result[0] = aValue.Project(); result[1] = control.Project(); result[2] = cValue.Project();
            newWeight = control.Z / MathF.Sqrt(aValue.Z * cValue.Z);
            // The native interior-interval overload publishes its direct coefficient result.
            return true;

            static Homogeneous Blend(Homogeneous a, Homogeneous b, float t) => new(a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
            static bool Finite(SvgDevicePoint p) => float.IsFinite((float)p.X) && float.IsFinite((float)p.Y);
        }
    }

    internal static SvgDevicePoint Evaluate(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> p, float weight, float t)
    {
        if (kind == SvgMeasuredCurveKind.Line) return Mix(p[0], p[1], t);
        if (kind == SvgMeasuredCurveKind.Conic) return ConicPolynomial(p, weight, t).Project();
        float x0 = (float)p[0].X, y0 = (float)p[0].Y, x1 = (float)p[1].X, y1 = (float)p[1].Y;
        float x2 = (float)p[2].X, y2 = (float)p[2].Y;
        if (kind == SvgMeasuredCurveKind.Quadratic)
            return new(((x2 - 2 * x1 + x0) * t + 2 * (x1 - x0)) * t + x0,
                ((y2 - 2 * y1 + y0) * t + 2 * (y1 - y0)) * t + y0);
        float x3 = (float)p[3].X, y3 = (float)p[3].Y;
        return new((((x3 + 3 * (x1 - x2) - x0) * t + 3 * (x2 - 2 * x1 + x0)) * t + 3 * (x1 - x0)) * t + x0,
            (((y3 + 3 * (y1 - y2) - y0) * t + 3 * (y2 - 2 * y1 + y0)) * t + 3 * (y1 - y0)) * t + y0);
    }

    /// <summary>Evaluates the native curve tangent vector, including coincident endpoint controls.</summary>
    /// <remarks>The caller normalizes the returned vector. Uses bounded scalar work and caller-owned input;
    /// no allocation or retained storage. A fully degenerate curve returns a zero vector.</remarks>
    internal static SvgDevicePoint Tangent(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> p, float weight,
        float t, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        Validate(kind, p, weight);
        if (kind == SvgMeasuredCurveKind.Conic && weight == 0) weight = 1;
        if (!float.IsFinite(t) || t < 0 || t > 1) throw new ArgumentOutOfRangeException(nameof(t));
        if (kind == SvgMeasuredCurveKind.Line) return Difference(p[1], p[0]);
        int last = kind == SvgMeasuredCurveKind.Cubic ? 3 : 2;
        if ((t == 0 && p[0] == p[1]) || (t == 1 && p[last - 1] == p[last]))
        {
            SvgDevicePoint result = kind == SvgMeasuredCurveKind.Cubic
                ? t == 0 ? Difference(p[2], p[0]) : Difference(p[3], p[1])
                : Difference(p[2], p[0]);
            return result.X == 0 && result.Y == 0 ? Difference(p[last], p[0]) : result;
        }
        return new(Component(kind, (float)p[0].X, (float)p[1].X, (float)p[2].X,
                last == 3 ? (float)p[3].X : 0, weight, t),
            Component(kind, (float)p[0].Y, (float)p[1].Y, (float)p[2].Y,
                last == 3 ? (float)p[3].Y : 0, weight, t));

        static float Component(SvgMeasuredCurveKind kind, float p0, float p1, float p2, float p3, float weight, float t)
        {
            if (kind == SvgMeasuredCurveKind.Quadratic)
            {
                float b = p1 - p0, a = p2 - p1 - b;
                float value = a * t + b;
                return value + value;
            }
            if (kind == SvgMeasuredCurveKind.Conic)
            {
                float delta = p2 - p0, c = weight * (p1 - p0);
                return ((weight * delta - delta) * t + (delta - c - c)) * t + c;
            }
            float a3 = p3 + 3 * (p1 - p2) - p0;
            float b3 = 2 * (p2 - 2 * p1 + p0), c3 = p1 - p0;
            return (a3 * t + b3) * t + c3;
        }

        static SvgDevicePoint Difference(SvgDevicePoint a, SvgDevicePoint b) => new((float)a.X - (float)b.X, (float)a.Y - (float)b.Y);
    }

    private static void WalkPolynomial(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> p, int minimum, int maximum,
        int depth, float tolerance, Span<SvgCurveMeasureStep> output, ref float distance, ref int count,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        int n = kind == SvgMeasuredCurveKind.Quadratic ? 3 : 4;
        bool curvy;
        if (n == 3)
        {
            float dx = (float)p[1].X / 2 - (((float)p[0].X + (float)p[2].X) / 2) / 2;
            float dy = (float)p[1].Y / 2 - (((float)p[0].Y + (float)p[2].Y) / 2) / 2;
            curvy = MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) > tolerance;
        }
        else curvy = Curvy(p[1], Mix(p[0], p[3], 1f / 3), tolerance)
            || Curvy(p[2], Mix(p[0], p[3], 2f / 3), tolerance);
        if (depth < 8 && ((maximum - minimum) >> 10) != 0 && curvy)
        {
            Span<SvgDevicePoint> halves = stackalloc SvgDevicePoint[7];
            ChopPolynomial(kind, p, .5f, halves);
            int middle = (minimum + maximum) >> 1;
            WalkPolynomial(kind, halves[..n], minimum, middle, depth + 1, tolerance, output, ref distance, ref count, work, token);
            WalkPolynomial(kind, halves.Slice(n - 1, n), middle, maximum, depth + 1, tolerance, output, ref distance, ref count, work, token);
        }
        else Append(p[0], p[n - 1], maximum, output, ref distance, ref count, work, token);

        static bool Curvy(SvgDevicePoint control, SvgDevicePoint chord, float tolerance) =>
            MathF.Max(MathF.Abs((float)control.X - (float)chord.X), MathF.Abs((float)control.Y - (float)chord.Y)) > tolerance;
    }

    private static void WalkConic(ReadOnlySpan<SvgDevicePoint> points, float weight, SvgDevicePoint first, SvgDevicePoint last,
        int minimum, int maximum, int depth, float tolerance, Span<SvgCurveMeasureStep> output,
        ref float distance, ref int count, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        int middle = (minimum + maximum) >> 1;
        SvgDevicePoint half = Evaluate(SvgMeasuredCurveKind.Conic, points, weight, ScalarParameter(middle));
        if (!float.IsFinite((float)half.X) || !float.IsFinite((float)half.Y)) return;
        float dx = (float)half.X - ((float)first.X + (float)last.X) * .5f;
        float dy = (float)half.Y - ((float)first.Y + (float)last.Y) * .5f;
        if (depth < 8 && ((maximum - minimum) >> 10) != 0 && MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) > tolerance)
        {
            WalkConic(points, weight, first, half, minimum, middle, depth + 1, tolerance, output, ref distance, ref count, work, token);
            WalkConic(points, weight, half, last, middle, maximum, depth + 1, tolerance, output, ref distance, ref count, work, token);
        }
        else Append(first, last, maximum, output, ref distance, ref count, work, token);
    }

    private static void Append(SvgDevicePoint first, SvgDevicePoint last, int parameter, Span<SvgCurveMeasureStep> output,
        ref float distance, ref int count, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        float dx = (float)last.X - (float)first.X, dy = (float)last.Y - (float)first.Y;
        float squared = dx * dx + dy * dy;
        float length = float.IsFinite(squared) ? MathF.Sqrt(squared) : (float)Math.Sqrt((double)dx * dx + (double)dy * dy);
        float previous = distance;
        distance += length;
        if (!(distance > previous)) return;
        if (count >= output.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "Curve measurement exceeds the caller's distance buffer.");
        output[count++] = new(distance, parameter);
    }

    private readonly record struct Homogeneous(float X, float Y, float Z)
    {
        internal SvgDevicePoint Project() => new(X / Z, Y / Z);
    }

    private static Homogeneous ConicPolynomial(ReadOnlySpan<SvgDevicePoint> p, float weight, float t)
    {
        float x0 = (float)p[0].X, y0 = (float)p[0].Y, x1 = (float)p[1].X * weight, y1 = (float)p[1].Y * weight;
        float denominatorB = 2 * (weight - 1), denominatorA = 0 - denominatorB;
        return new((((float)p[2].X - 2 * x1 + x0) * t + 2 * (x1 - x0)) * t + x0,
            (((float)p[2].Y - 2 * y1 + y0) * t + 2 * (y1 - y0)) * t + y0,
            (denominatorA * t + denominatorB) * t + 1);
    }

    private static void ChopPolynomial(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> p, float t, Span<SvgDevicePoint> output)
    {
        SvgDevicePoint ab = Mix(p[0], p[1], t), bc = Mix(p[1], p[2], t);
        if (kind == SvgMeasuredCurveKind.Quadratic)
        {
            output[0] = p[0]; output[1] = ab; output[2] = Mix(ab, bc, t); output[3] = bc; output[4] = p[2];
        }
        else
        {
            SvgDevicePoint cd = Mix(p[2], p[3], t), abc = Mix(ab, bc, t), bcd = Mix(bc, cd, t);
            output[0] = p[0]; output[1] = ab; output[2] = abc; output[3] = Mix(abc, bcd, t);
            output[4] = bcd; output[5] = cd; output[6] = p[3];
        }
    }

    private static SvgDevicePoint Mix(SvgDevicePoint a, SvgDevicePoint b, float t) => new(
        (float)a.X + ((float)b.X - (float)a.X) * t, (float)a.Y + ((float)b.Y - (float)a.Y) * t);
    private static float ScalarParameter(int value) => value * (1f / MaximumParameter);

    private static void Validate(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> points, float weight)
    {
        int count = kind switch { SvgMeasuredCurveKind.Line => 2, SvgMeasuredCurveKind.Quadratic or SvgMeasuredCurveKind.Conic => 3,
            SvgMeasuredCurveKind.Cubic => 4, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        if (points.Length < count) throw new ArgumentException("Missing curve points.", nameof(points));
        if (kind == SvgMeasuredCurveKind.Conic && (!float.IsFinite(weight) || weight < 0)) throw new ArgumentOutOfRangeException(nameof(weight));
        foreach (SvgDevicePoint point in points[..count])
            if (!float.IsFinite((float)point.X) || !float.IsFinite((float)point.Y)) throw new ArgumentOutOfRangeException(nameof(points));
    }
}
