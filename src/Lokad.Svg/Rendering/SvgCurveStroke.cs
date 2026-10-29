// Adapts curve fitting from SkStroke.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2008 The Android Open Source Project.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Chooses one directed side of the original centerline.</summary>
internal enum SvgStrokeSide { Outer = 1, Inner = -1 }

/// <summary>One native offset segment; a missing control identifies a line.</summary>
internal readonly record struct SvgStrokeCurveSegment(SvgDevicePoint Start, SvgDevicePoint? Control, SvgDevicePoint End);

/// <summary>Fits normal-offset curves with the pinned native quadratic subdivision decisions.</summary>
/// <remarks>The caller owns the budgeted output list and publishes it only after success. Recursive
/// numerical state occupies bounded stack storage; every node observes work limits and cancellation.
/// Output growth is charged before allocation and capped independently of the native recursion limit.
/// Separate calls share no mutable state. This fits individual curves; path joins and caps belong to
/// the contour constructor.</remarks>
internal static class SvgCurveStroke
{
    internal const int MaximumDepth = 33;
    internal const int MaximumSegments = 65536;

    internal static void Append(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end,
        float weight, float radius, float resolutionScale, SvgStrokeSide side,
        BudgetedParserList<SvgStrokeCurveSegment> output, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output);
        token.ThrowIfCancellationRequested();
        if (!float.IsFinite(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(resolutionScale) || resolutionScale <= 0) throw new ArgumentOutOfRangeException(nameof(resolutionScale));
        if (side is not SvgStrokeSide.Outer and not SvgStrokeSide.Inner) throw new ArgumentOutOfRangeException(nameof(side));
        Curve conic = new(SvgMeasuredCurveKind.Conic, Point.Narrow(start), Point.Narrow(control), default, Point.Narrow(end), weight == 0 ? 1 : weight);
        float tolerance = 1 / (resolutionScale * 4);
        Construction root = Construction.Create(0, 1);
        int depth = 0;
        Fit(conic, radius, tolerance, (int)side, ref root, ref depth, output, token);
    }

    internal static void AppendPolynomial(Curve curve, float radius, float resolutionScale, SvgStrokeSide side,
        BudgetedParserList<SvgStrokeCurveSegment> output, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output);
        token.ThrowIfCancellationRequested();
        if (curve.Kind is not SvgMeasuredCurveKind.Quadratic and not SvgMeasuredCurveKind.Cubic) throw new ArgumentException("A polynomial stroke requires a quadratic or cubic.", nameof(curve));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(resolutionScale) || resolutionScale <= 0) throw new ArgumentOutOfRangeException(nameof(resolutionScale));
        if (side is not SvgStrokeSide.Outer and not SvgStrokeSide.Inner) throw new ArgumentOutOfRangeException(nameof(side));
        float tolerance = 1 / (resolutionScale * 4);
        int depth = 0;
        if (curve.Kind == SvgMeasuredCurveKind.Quadratic)
        {
            Construction root = Construction.Create(0, 1);
            Fit(curve, radius, tolerance, (int)side, ref root, ref depth, output, token);
            return;
        }
        token.ThrowIfCancellationRequested();
        Span<float> inflections = stackalloc float[2];
        Point a = curve.First - curve.Start, b = curve.Second - curve.First * 2 + curve.Start;
        Point c = curve.End + (curve.First - curve.Second) * 3 - curve.Start;
        int count = UnitRoots(b.Cross(c), a.Cross(c), a.Cross(b), inflections);
        float first = 0;
        for (int index = 0; index <= count; index++)
        {
            float last = index < count ? inflections[index] : 1;
            Construction root = Construction.Create(first, last);
            bool foundTangents = false;
            FitCubic(curve, radius, tolerance, (int)side, ref root, ref depth, ref foundTangents, output, token);
            first = last;
        }
    }

    private enum Result { Split, Line, Quadratic }
    internal readonly record struct Point(float X, float Y)
    {
        internal static Point Narrow(SvgDevicePoint p)
        {
            float x = (float)p.X, y = (float)p.Y;
            if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke coordinates exceed the finite float range.");
            return new(x, y);
        }
        internal SvgDevicePoint Device()
        {
            if (!float.IsFinite(X) || !float.IsFinite(Y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Stroke offsets exceed the finite float range.");
            return new(X, Y);
        }
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
        public static Point operator *(Point a, float b) => new(a.X * b, a.Y * b);
        internal float Dot(Point b) => X * b.X + Y * b.Y;
        internal float Cross(Point b) => X * b.Y - Y * b.X;
        internal bool SetLength(float length, out Point result)
        {
            double scale = length / Math.Sqrt((double)X * X + (double)Y * Y);
            float x = (float)(X * scale), y = (float)(Y * scale);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x == 0 && y == 0) { result = default; return false; }
            result = new(x, y); return true;
        }
    }

    internal readonly record struct Curve(SvgMeasuredCurveKind Kind, Point Start, Point First, Point Second, Point End, float Weight)
    {
        internal Point Evaluate(float t)
        {
            if (Kind == SvgMeasuredCurveKind.Quadratic)
                return ((End - First * 2 + Start) * t + (First - Start) * 2) * t + Start;
            if (Kind == SvgMeasuredCurveKind.Cubic)
                return (((End + (First - Second) * 3 - Start) * t + (Second - First * 2 + Start) * 3) * t + (First - Start) * 3) * t + Start;
            Point weighted = First * Weight;
            Point a = End - weighted * 2 + Start, b = (weighted - Start) * 2;
            Point numerator = (a * t + b) * t + Start;
            float db = 2 * (Weight - 1), da = 0 - db;
            float denominator = (da * t + db) * t + 1;
            return new(numerator.X / denominator, numerator.Y / denominator);
        }
        internal Point Tangent(float t)
        {
            if (Kind == SvgMeasuredCurveKind.Quadratic)
            {
                if (t == 0 && Start == First || t == 1 && First == End) return End - Start;
                Point quadB = First - Start, quadA = End - First - quadB, tangent = quadA * t + quadB;
                return tangent + tangent;
            }
            if (Kind == SvgMeasuredCurveKind.Cubic)
            {
                if (t == 0 && Start == First || t == 1 && Second == End)
                {
                    Point tangent = t == 0 ? Second - Start : End - First;
                    return tangent == default ? End - Start : tangent;
                }
                return ((End + (First - Second) * 3 - Start) * t + (Second - First * 2 + Start) * 2) * t + (First - Start);
            }
            if (t == 0 && Start == First || t == 1 && First == End) return End - Start;
            Point p20 = End - Start, p10 = First - Start;
            Point c = p10 * Weight, a = p20 * Weight - p20, b = p20 - c - c;
            return (a * t + b) * t + c;
        }

        internal int CurvatureParameters(Span<float> roots)
        {
            Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[] { Start.Device(), First.Device(), Second.Device(), End.Device() };
            return SvgCurveGeometry.CubicCurvatureRoots(points, roots);
        }

        internal float Cusp()
        {
            if (Start == First || Second == End || SameSide(Start, First, Second, End) || SameSide(Second, End, Start, First)) return -1;
            Span<float> roots = stackalloc float[3];
            int count = CurvatureParameters(roots);
            Point a = First - Start, b = Second - First, c = End - Second;
            float precision = (a.Dot(a) + b.Dot(b) + c.Dot(c)) * 1e-8f;
            for (int index = 0; index < count; index++)
            {
                if (roots[index] <= 0 || roots[index] >= 1) continue;
                Point tangent = Tangent(roots[index]);
                if (tangent.Dot(tangent) < precision) return roots[index];
            }
            return -1;

            static bool SameSide(Point a, Point b, Point origin, Point stop)
            { Point line = stop - origin; return line.Cross(a - origin) * line.Cross(b - origin) >= 0; }
        }
    }

    private struct Construction
    {
        internal Point Start, Control, End, StartTangent, EndTangent;
        internal float StartT, MidT, EndT;
        internal bool StartSet, EndSet, OppositeTangents;
        internal static Construction Create(float start, float end) => new() { StartT = start, MidT = (start + end) * 0.5f, EndT = end };
        internal readonly Construction FirstHalf()
        {
            Construction half = Create(StartT, MidT);
            if (half.StartT < half.MidT && half.MidT < half.EndT)
            { half.Start = Start; half.StartTangent = StartTangent; half.StartSet = true; }
            return half;
        }
        internal readonly Construction SecondHalf()
        {
            Construction half = Create(MidT, EndT);
            if (half.StartT < half.MidT && half.MidT < half.EndT)
            { half.End = End; half.EndTangent = EndTangent; half.EndSet = true; }
            return half;
        }
    }

    private static void Fit(Curve conic, float radius, float tolerance, int side,
        ref Construction q, ref int depth, BudgetedParserList<SvgStrokeCurveSegment> output,
CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Result result = Compare(conic, radius, tolerance, side, ref q);
        if (result != Result.Split || ++depth > MaximumDepth)
        {
            if (output.Count >= MaximumSegments) throw new SvgException(SvgErrorCode.LimitExceeded, "Conic stroke segment count exceeds the supported expansion range.");
            output.Add(new(q.Start.Device(), result == Result.Quadratic ? q.Control.Device() : null, q.End.Device()));
            return;
        }
        Construction half = q.FirstHalf();
        Fit(conic, radius, tolerance, side, ref half, ref depth, output, token);
        half = q.SecondHalf();
        Fit(conic, radius, tolerance, side, ref half, ref depth, output, token);
        depth--;
    }

    private static Result Compare(Curve conic, float radius, float tolerance, int side, ref Construction q)
    {
        if (!q.StartSet)
        { Ray(conic, q.StartT, radius, side, out _, out q.Start, out q.StartTangent); q.StartSet = true; }
        if (!q.EndSet)
        { Ray(conic, q.EndT, radius, side, out _, out q.End, out q.EndTangent); q.EndSet = true; }
        Result result = Intersect(ref q, tolerance, true);
        if (result != Result.Quadratic) return result;
        Ray(conic, q.MidT, radius, side, out Point curve, out Point offset, out _);
        Point strokeMid = EvaluateQuad(q.Start, q.Control, q.End, 0.5f);
        if (Within(offset, strokeMid, tolerance)) return Sharp(q) ? Result.Split : Result.Quadratic;
        if (offset.X + tolerance < MathF.Min(q.Start.X, MathF.Min(q.Control.X, q.End.X))
            || offset.X - tolerance > MathF.Max(q.Start.X, MathF.Max(q.Control.X, q.End.X))
            || offset.Y + tolerance < MathF.Min(q.Start.Y, MathF.Min(q.Control.Y, q.End.Y))
            || offset.Y - tolerance > MathF.Max(q.Start.Y, MathF.Max(q.Control.Y, q.End.Y))) return Result.Split;
        Point direction = curve - offset;
        float c = direction.Cross(q.Start - offset), b = direction.Cross(q.Control - offset), a = direction.Cross(q.End - offset);
        a += c - 2 * b; b -= c;
        Span<float> roots = stackalloc float[2];
        int count = UnitRoots(a, 2 * b, c, roots);
        if (count != 1) return Result.Split;
        float error = tolerance * (1 - MathF.Abs(roots[0] - 0.5f) * 2);
        return Within(offset, EvaluateQuad(q.Start, q.Control, q.End, roots[0]), error) && !Sharp(q) ? Result.Quadratic : Result.Split;

        static bool Within(Point a, Point b, float limit) { Point d = a - b; return d.Dot(d) <= limit * limit; }
        static bool Sharp(Construction q)
        {
            Point smaller = q.Control - q.Start, larger = q.Control - q.End;
            float smallLength = smaller.Dot(smaller), largeLength = larger.Dot(larger);
            if (smallLength > largeLength) { (smaller, larger) = (larger, smaller); largeLength = smallLength; }
            return smaller.SetLength(largeLength, out Point scaled) && scaled.Dot(larger) > 0;
        }
        static Point EvaluateQuad(Point a, Point b, Point c, float t)
            => ((c - b * 2 + a) * t + (b - a) * 2) * t + a;
    }

    internal static int UnitRoots(float a, float b, float c, Span<float> roots)
    {
        if (a == 0) return Divide(-c, b, out roots[0]) ? 1 : 0;
        double discriminant = (double)b * b - 4 * (double)a * c;
        if (discriminant < 0) return 0;
        float r = (float)Math.Sqrt(discriminant);
        if (!float.IsFinite(r)) return 0;
        float q = b < 0 ? -(b - r) / 2 : -(b + r) / 2;
        int count = 0;
        if (Divide(q, a, out float first)) roots[count++] = first;
        if (Divide(c, q, out float second)) roots[count++] = second;
        if (count == 2) { if (roots[0] > roots[1]) (roots[0], roots[1]) = (roots[1], roots[0]); else if (roots[0] == roots[1]) count--; }
        return count;

        static bool Divide(float numerator, float denominator, out float ratio)
        {
            if (numerator < 0) { numerator = -numerator; denominator = -denominator; }
            ratio = numerator / denominator;
            return denominator != 0 && numerator != 0 && numerator < denominator && !float.IsNaN(ratio) && ratio != 0;
        }
    }

    private static void Ray(Curve conic, float t, float radius, int side, out Point curve, out Point offset, out Point tangent)
    {
        curve = conic.Evaluate(t);
        tangent = conic.Tangent(t);
        if (tangent == default && conic.Kind == SvgMeasuredCurveKind.Cubic)
        {
            if (MathF.Abs(t) <= 1f / 4096) tangent = conic.Second - conic.Start;
            else if (MathF.Abs(1 - t) <= 1f / 4096) tangent = conic.End - conic.First;
            else
            {
                Point ab = Mix(conic.Start, conic.First, t), bc = Mix(conic.First, conic.Second, t), cd = Mix(conic.Second, conic.End, t);
                Point abc = Mix(ab, bc, t), bcd = Mix(bc, cd, t), midpoint = Mix(abc, bcd, t);
                tangent = midpoint - abc;
                if (tangent == default) { tangent = midpoint - ab; if (tangent == default) tangent = midpoint - conic.Start; }
            }
        }
        if (tangent == default) tangent = conic.End - conic.Start;
        if (!tangent.SetLength(radius, out tangent)) tangent = new(radius, 0);
        offset = new(curve.X + side * tangent.Y, curve.Y - side * tangent.X);

        static Point Mix(Point a, Point b, float t) => a + (b - a) * t;
    }

    private static Result Intersect(ref Construction q, float tolerance, bool control)
    {
        float denominator = q.StartTangent.Cross(q.EndTangent);
        if (denominator == 0 || !float.IsFinite(denominator)) { q.OppositeTangents = q.StartTangent.Dot(q.EndTangent) < 0; return Result.Line; }
        q.OppositeTangents = false;
        Point delta = q.Start - q.End;
        float numeratorA = q.EndTangent.Cross(delta), numeratorB = q.StartTangent.Cross(delta);
        if ((numeratorA >= 0) == (numeratorB >= 0))
            return MathF.Max(Distance(q.Start, q.End, q.EndTangent), Distance(q.End, q.Start, q.StartTangent)) <= tolerance * tolerance ? Result.Line : Result.Split;
        numeratorA /= denominator;
        if (!(numeratorA > numeratorA - 1)) { q.OppositeTangents = q.StartTangent.Dot(q.EndTangent) < 0; return Result.Line; }
        if (control) q.Control = q.Start + q.StartTangent * numeratorA;
        return Result.Quadratic;

        static float Distance(Point p, Point origin, Point tangent)
        {
            Point delta = p - origin;
            float t = tangent.Dot(delta) / tangent.Dot(tangent);
            if (t >= 0 && t <= 1) delta = origin + tangent * t - p;
            return delta.Dot(delta);
        }
    }

    private static void FitCubic(Curve curve, float radius, float tolerance, int side,
        ref Construction q, ref int depth, ref bool foundTangents, BudgetedParserList<SvgStrokeCurveSegment> output,
CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Result result = Result.Split;
        if (!foundTangents)
        {
            if (!q.StartSet) { Ray(curve, q.StartT, radius, side, out _, out q.Start, out q.StartTangent); q.StartSet = true; }
            if (!q.EndSet) { Ray(curve, q.EndT, radius, side, out _, out q.End, out q.EndTangent); q.EndSet = true; }
            result = Intersect(ref q, tolerance, false);
            if (result == Result.Quadratic) foundTangents = true;
            else
            {
                Point ends = q.Start - q.End;
                if (result == Result.Line || ends.Dot(ends) <= tolerance * tolerance)
                {
                    Ray(curve, q.MidT, radius, side, out _, out Point midpoint, out _);
                    if (LineDistance(midpoint, q.Start, q.End) < tolerance * tolerance) { Emit(q, false, output); return; }
                }
            }
        }
        if (foundTangents)
        {
            result = Compare(curve, radius, tolerance, side, ref q);
            if (result == Result.Quadratic) { Emit(q, true, output); return; }
            if (result == Result.Line && !q.OppositeTangents) { Emit(q, false, output); return; }
        }
        if (!float.IsFinite(q.End.X) || !float.IsFinite(q.End.Y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Cubic stroke offsets exceed the finite float range.");
        if (++depth > (foundTangents ? 24 : 15)) { Emit(q, false, output); return; }
        Construction half = q.FirstHalf();
        if (!(half.StartT < half.MidT && half.MidT < half.EndT)) { Emit(q, false, output); depth--; return; }
        FitCubic(curve, radius, tolerance, side, ref half, ref depth, ref foundTangents, output, token);
        half = q.SecondHalf();
        if (!(half.StartT < half.MidT && half.MidT < half.EndT)) { Emit(q, false, output); depth--; return; }
        FitCubic(curve, radius, tolerance, side, ref half, ref depth, ref foundTangents, output, token);
        depth--;

        static void Emit(Construction value, bool quadratic, BudgetedParserList<SvgStrokeCurveSegment> sink)
        {
            if (sink.Count >= MaximumSegments) throw new SvgException(SvgErrorCode.LimitExceeded, "Cubic stroke segment count exceeds the supported expansion range.");
            sink.Add(new(value.Start.Device(), quadratic ? value.Control.Device() : null, value.End.Device()));
        }
    }

    internal static float LineDistance(Point p, Point start, Point end)
    {
        Point d = end - start, a = p - start;
        float t = d.Dot(a) / d.Dot(d);
        if (t >= 0 && t <= 1) { Point delta = start * (1 - t) + end * t - p; return delta.Dot(delta); }
        return a.Dot(a);
    }
}
