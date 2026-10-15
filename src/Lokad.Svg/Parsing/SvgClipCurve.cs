// Rational subinterval construction adapts pinned SkPathOpsConic.cpp.
// Copyright 2015 Google Inc. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one mapped native clip segment before boolean boundary construction.</summary>
/// <remarks>Values borrow no storage. Double parameter evaluation belongs to path operations;
/// output controls/weights narrow once before the existing float raster replay. Callers charge
/// numerical visits and observe cancellation. No shared mutable state or allocation is used.</remarks>
internal readonly record struct SvgClipCurve(SvgMeasuredCurveKind Kind, SvgDevicePoint Start,
    SvgDevicePoint First, SvgDevicePoint Second, SvgDevicePoint End, float Weight)
{
    internal void Coefficients(Span<double> x, Span<double> y, Span<double> z)
    {
        x.Clear(); y.Clear(); z.Clear(); z[0] = 1;
        x[0] = Start.X; y[0] = Start.Y;
        if (Kind == SvgMeasuredCurveKind.Line) { x[1] = End.X - Start.X; y[1] = End.Y - Start.Y; return; }
        if (Kind == SvgMeasuredCurveKind.Cubic)
        {
            x[1] = 3 * (First.X - Start.X); y[1] = 3 * (First.Y - Start.Y);
            x[2] = 3 * (Second.X - 2 * First.X + Start.X); y[2] = 3 * (Second.Y - 2 * First.Y + Start.Y);
            x[3] = End.X + 3 * (First.X - Second.X) - Start.X;
            y[3] = End.Y + 3 * (First.Y - Second.Y) - Start.Y;
            return;
        }
        double weight = Kind == SvgMeasuredCurveKind.Conic ? Weight : 1;
        double cx = First.X * weight, cy = First.Y * weight;
        x[1] = 2 * (cx - Start.X); y[1] = 2 * (cy - Start.Y);
        x[2] = End.X - 2 * cx + Start.X; y[2] = End.Y - 2 * cy + Start.Y;
        z[1] = 2 * (weight - 1); z[2] = -z[1];
    }

    internal SvgDevicePoint Evaluate(double t)
    {
        if (t == 0) return Start;
        if (t == 1) return End;
        Span<double> x = stackalloc double[4], y = stackalloc double[4], z = stackalloc double[4];
        Coefficients(x, y, z);
        double denominator = Polynomial(z, t);
        return new(Polynomial(x, t) / denominator, Polynomial(y, t) / denominator);
    }

    internal SvgDevicePoint Tangent(double t)
    {
        Span<double> x = stackalloc double[4], y = stackalloc double[4], z = stackalloc double[4];
        Coefficients(x, y, z);
        double d = Polynomial(z, t), dd = Derivative(z, t);
        return new((Derivative(x, t) * d - Polynomial(x, t) * dd) / (d * d),
            (Derivative(y, t) * d - Polynomial(y, t) * dd) / (d * d));

        static double Derivative(ReadOnlySpan<double> c, double value) => (3 * c[3] * value + 2 * c[2]) * value + c[1];
    }

    internal SvgAuthoredPathCommand Cut(double first, double last, SvgDevicePoint end)
    {
        if (Kind == SvgMeasuredCurveKind.Line) return new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, end, default, default);
        if ((first == 0 && last == 1) || (first == 1 && last == 0))
            return Kind == SvgMeasuredCurveKind.Cubic
                ? new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, first == 0 ? First : Second, first == 0 ? Second : First, end)
                : new(Kind == SvgMeasuredCurveKind.Conic ? SvgAuthoredPathVerb.Conic : SvgAuthoredPathVerb.Quadratic,
                    SvgAuthoredArcFlags.None, First, end, new(Weight, 0));
        if (Kind == SvgMeasuredCurveKind.Cubic)
        {
            SvgDevicePoint a = Evaluate(first), b = Evaluate(last), da = Tangent(first), db = Tangent(last);
            double scale = (last - first) / 3;
            return new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None,
                Narrow(new(a.X + da.X * scale, a.Y + da.Y * scale)),
                Narrow(new(b.X - db.X * scale, b.Y - db.Y * scale)), end);
        }
        // Path-ops uses double homogeneous coefficients even for endpoint cuts;
        // native dash cuts retain their separate float construction policy.
        Span<double> x = stackalloc double[4], y = stackalloc double[4], z = stackalloc double[4];
        Coefficients(x, y, z);
        double ax = first == 0 ? Start.X : first == 1 ? End.X : Polynomial(x, first);
        double ay = first == 0 ? Start.Y : first == 1 ? End.Y : Polynomial(y, first);
        double az = first is 0 or 1 ? 1 : Polynomial(z, first);
        double cx = last == 0 ? Start.X : last == 1 ? End.X : Polynomial(x, last);
        double cy = last == 0 ? Start.Y : last == 1 ? End.Y : Polynomial(y, last);
        double cz = last is 0 or 1 ? 1 : Polynomial(z, last);
        double middle = (first + last) / 2;
        double bx = 2 * Polynomial(x, middle) - (ax + cx) / 2;
        double by = 2 * Polynomial(y, middle) - (ay + cy) / 2;
        double bz = 2 * Polynomial(z, middle) - (az + cz) / 2;
        if (bz == 0) bz = 1;
        float weight = (float)(bz / Math.Sqrt(az * cz));
        return new(Kind == SvgMeasuredCurveKind.Conic ? SvgAuthoredPathVerb.Conic : SvgAuthoredPathVerb.Quadratic,
            SvgAuthoredArcFlags.None, Narrow(new(bx / bz, by / bz)), end, new(weight, 0));
    }

    internal static SvgDevicePoint Narrow(SvgDevicePoint point)
    {
        float x = (float)point.X, y = (float)point.Y;
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Clip union coordinates exceed the finite float range.");
        return new(x, y);
    }

    internal static double Polynomial(ReadOnlySpan<double> coefficients, double t)
    {
        double value = coefficients[^1];
        for (int index = coefficients.Length - 2; index >= 0; index--) value = value * t + coefficients[index];
        return value;
    }

    /// <summary>Isolates finite real roots on the unit interval using derivative partitions.</summary>
    /// <remarks>Degree is bounded by nine for cubic implicit intersections. Scratch is fixed per
    /// recursion level; each derivative, partition and bisection visit consumes caller work/token.</remarks>
    internal static int Roots(ReadOnlySpan<double> coefficients, Span<double> roots, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        int degree = coefficients.Length - 1;
        while (degree > 0 && coefficients[degree] == 0) degree--;
        if (degree == 0) return 0;
        if (degree > 9 || roots.Length < degree) throw new ArgumentOutOfRangeException(nameof(coefficients));
        if (degree == 1)
        {
            double root = -coefficients[0] / coefficients[1];
            if (root < 0 || root > 1 || !double.IsFinite(root)) return 0;
            roots[0] = root; return 1;
        }
        Span<double> derivative = stackalloc double[9], critical = stackalloc double[9], divisions = stackalloc double[11];
        for (int index = 1; index <= degree; index++) { work.Consume(token); derivative[index - 1] = coefficients[index] * index; }
        int criticalCount = Roots(derivative[..degree], critical, work, token), divisionCount = 1;
        divisions[0] = 0;
        for (int index = 0; index < criticalCount; index++) if (critical[index] > 0 && critical[index] < 1) divisions[divisionCount++] = critical[index];
        divisions[divisionCount++] = 1;
        int count = 0;
        double magnitude = 0;
        for (int index = 0; index <= degree; index++) magnitude += Math.Abs(coefficients[index]);
        double error = magnitude * 16 * 2.2204460492503131e-16;
        for (int index = 0; index < divisionCount; index++)
        {
            work.Consume(token);
            double a = divisions[index], fa = Polynomial(coefficients[..(degree + 1)], a);
            if (Math.Abs(fa) <= error && (count == 0 || Math.Abs(a - roots[count - 1]) > 1e-13))
            {
                if (count < roots.Length) roots[count++] = a;
            }
            if (index + 1 == divisionCount) break;
            double b = divisions[index + 1], fb = Polynomial(coefficients[..(degree + 1)], b);
            if (Math.Abs(fa) <= error || Math.Abs(fb) <= error || Math.Sign(fa) == Math.Sign(fb)) continue;
            for (int iteration = 0; iteration < 60; iteration++)
            {
                work.Consume(token);
                double middle = (a + b) / 2;
                if (middle == a || middle == b) break;
                double fm = Polynomial(coefficients[..(degree + 1)], middle);
                if (fm == 0) { a = b = middle; break; }
                if (Math.Sign(fm) == Math.Sign(fa)) { a = middle; fa = fm; } else b = middle;
            }
            double result = (a + b) / 2;
            if (count == 0 || Math.Abs(result - roots[count - 1]) > 1e-13)
                if (count < roots.Length) roots[count++] = result;
        }
        return count;
    }
}
