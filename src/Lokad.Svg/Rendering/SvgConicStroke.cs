// Adapts conic fitting from SkStroke.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2008 The Android Open Source Project.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Chooses one directed side of the original centerline.</summary>
internal enum SvgStrokeSide { Outer = 1, Inner = -1 }

/// <summary>One native offset segment; a missing control identifies a line.</summary>
internal readonly record struct SvgStrokeCurveSegment(SvgDevicePoint Start, SvgDevicePoint? Control, SvgDevicePoint End);

/// <summary>Fits normal-offset conics with the pinned native quadratic subdivision decisions.</summary>
/// <remarks>The caller owns the budgeted output list and publishes it only after success. Recursive
/// numerical state occupies bounded stack storage; every node observes work limits and cancellation.
/// Output growth is charged before allocation and capped independently of the native recursion limit.
/// Separate calls share no mutable state. This fits individual conics; path joins and caps belong to
/// the contour constructor.</remarks>
internal static class SvgConicStroke
{
    internal const int MaximumDepth = 33;
    internal const int MaximumSegments = 65536;

    internal static void Append(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end,
        float weight, float radius, float resolutionScale, SvgStrokeSide side,
        BudgetedParserList<SvgStrokeCurveSegment> output, ParserWorkCounter work, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(work);
        token.ThrowIfCancellationRequested();
        if (!float.IsFinite(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(resolutionScale) || resolutionScale <= 0) throw new ArgumentOutOfRangeException(nameof(resolutionScale));
        if (side is not SvgStrokeSide.Outer and not SvgStrokeSide.Inner) throw new ArgumentOutOfRangeException(nameof(side));
        Conic conic = new(Point.Narrow(start), Point.Narrow(control), Point.Narrow(end), weight == 0 ? 1 : weight);
        float tolerance = 1 / (resolutionScale * 4);
        Construction root = Construction.Create(0, 1);
        int depth = 0;
        Fit(conic, radius, tolerance, (int)side, ref root, ref depth, output, work, token);
    }

    private enum Result { Split, Line, Quadratic }
    private readonly record struct Point(float X, float Y)
    {
        internal static Point Narrow(SvgDevicePoint p)
        {
            float x = (float)p.X, y = (float)p.Y;
            if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Conic stroke coordinates exceed the finite float range.");
            return new(x, y);
        }
        internal SvgDevicePoint Device()
        {
            if (!float.IsFinite(X) || !float.IsFinite(Y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Conic stroke offsets exceed the finite float range.");
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

    private readonly record struct Conic(Point Start, Point Control, Point End, float Weight)
    {
        internal Point Evaluate(float t)
        {
            Point weighted = Control * Weight;
            Point a = End - weighted * 2 + Start, b = (weighted - Start) * 2;
            Point numerator = (a * t + b) * t + Start;
            float db = 2 * (Weight - 1), da = 0 - db;
            float denominator = (da * t + db) * t + 1;
            return new(numerator.X / denominator, numerator.Y / denominator);
        }
        internal Point Tangent(float t)
        {
            if (t == 0 && Start == Control || t == 1 && Control == End) return End - Start;
            Point p20 = End - Start, p10 = Control - Start;
            Point c = p10 * Weight, a = p20 * Weight - p20, b = p20 - c - c;
            return (a * t + b) * t + c;
        }
    }

    private struct Construction
    {
        internal Point Start, Control, End, StartTangent, EndTangent;
        internal float StartT, MidT, EndT;
        internal bool StartSet, EndSet;
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

    private static void Fit(Conic conic, float radius, float tolerance, int side,
        ref Construction q, ref int depth, BudgetedParserList<SvgStrokeCurveSegment> output,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        Result result = Compare(conic, radius, tolerance, side, ref q);
        if (result != Result.Split || ++depth > MaximumDepth)
        {
            if (output.Count >= MaximumSegments) throw new SvgException(SvgErrorCode.LimitExceeded, "Conic stroke segment count exceeds the supported expansion range.");
            output.Add(new(q.Start.Device(), result == Result.Quadratic ? q.Control.Device() : null, q.End.Device()));
            return;
        }
        Construction half = q.FirstHalf();
        Fit(conic, radius, tolerance, side, ref half, ref depth, output, work, token);
        half = q.SecondHalf();
        Fit(conic, radius, tolerance, side, ref half, ref depth, output, work, token);
        depth--;
    }

    private static Result Compare(Conic conic, float radius, float tolerance, int side, ref Construction q)
    {
        if (!q.StartSet)
        { Ray(conic, q.StartT, radius, side, out _, out q.Start, out q.StartTangent); q.StartSet = true; }
        if (!q.EndSet)
        { Ray(conic, q.EndT, radius, side, out _, out q.End, out q.EndTangent); q.EndSet = true; }
        Result result = Intersect(ref q, tolerance);
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
        static int UnitRoots(float a, float b, float c, Span<float> roots)
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
    }

    private static void Ray(Conic conic, float t, float radius, int side, out Point curve, out Point offset, out Point tangent)
    {
        curve = conic.Evaluate(t);
        tangent = conic.Tangent(t);
        if (tangent == default) tangent = conic.End - conic.Start;
        if (!tangent.SetLength(radius, out tangent)) tangent = new(radius, 0);
        offset = new(curve.X + side * tangent.Y, curve.Y - side * tangent.X);
    }

    private static Result Intersect(ref Construction q, float tolerance)
    {
        float denominator = q.StartTangent.Cross(q.EndTangent);
        if (denominator == 0 || !float.IsFinite(denominator)) return Result.Line;
        Point delta = q.Start - q.End;
        float numeratorA = q.EndTangent.Cross(delta), numeratorB = q.StartTangent.Cross(delta);
        if ((numeratorA >= 0) == (numeratorB >= 0))
            return MathF.Max(Distance(q.Start, q.End, q.EndTangent), Distance(q.End, q.Start, q.StartTangent)) <= tolerance * tolerance ? Result.Line : Result.Split;
        numeratorA /= denominator;
        if (!(numeratorA > numeratorA - 1)) return Result.Line;
        q.Control = q.Start + q.StartTangent * numeratorA;
        return Result.Quadratic;

        static float Distance(Point p, Point origin, Point tangent)
        {
            Point delta = p - origin;
            float t = tangent.Dot(delta) / tangent.Dot(tangent);
            if (t >= 0 && t <= 1) delta = origin + tangent * t - p;
            return delta.Dot(delta);
        }
    }
}
