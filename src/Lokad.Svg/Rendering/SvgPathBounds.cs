// Tight curve bounds adapt SkPathPriv.cpp and SkGeometry.cpp at the revision
// recorded in THIRD_PARTY_NOTICES.txt. Copyright 2006 The Android Open Source Project.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Computes native float path bounds from retained source verbs, independently of flattening.</summary>
internal static class SvgPathBounds
{
    /// <summary>Includes endpoints, movetos and original curve extrema in the source-coordinate paint box.</summary>
    /// <remarks>The caller supplies validated immutable verbs and owns their storage. Each verb,
    /// bounded curve-root phase, evaluated extremum and arc normalization is charged before work.
    /// Fixed stack buffers hold four roots and at most three endpoint conics; no heap storage is
    /// allocated or retained. Cancellation preserves its token. Calls have independent local state.</remarks>
    internal static SvgRectangle Compute(ReadOnlySpan<SvgAuthoredPathCommand> commands, ParserWorkCounter work, CancellationToken token)
        => Compute(commands, SvgAffineTransform.Identity, work, token);

    /// <summary>Computes tight bounds after mapping native controls into a referencing object's coordinate system.</summary>
    /// <remarks>Source commands remain borrowed. Mapping and extrema share bounded work/token and fixed scratch;
    /// mapping precedes root evaluation so a rotated curve is not replaced by its enclosing rectangle.</remarks>
    internal static SvgRectangle Compute(ReadOnlySpan<SvgAuthoredPathCommand> commands, SvgAffineTransform map, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        SvgDevicePoint current = default, first = default;
        Span<float> roots = stackalloc float[4];
        Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        foreach (SvgAuthoredPathCommand command in commands)
        {
            work.Consume(token);
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move:
                    current = first = command.First;
                    Include(Map(current));
                    break;
                case SvgAuthoredPathVerb.Line:
                    current = command.First;
                    Include(Map(current));
                    break;
                case SvgAuthoredPathVerb.Quadratic:
                    Quad(current, command.First, command.Second, 1, false, roots, work, token);
                    current = command.Second;
                    break;
                case SvgAuthoredPathVerb.Conic:
                    Quad(current, command.First, command.Second, (float)command.Third.X, true, roots, work, token);
                    current = command.Second;
                    break;
                case SvgAuthoredPathVerb.Cubic:
                    work.Consume(token);
                    SvgDevicePoint a = Map(current), b = Map(command.First), c = Map(command.Second), d = Map(command.Third);
                    int count = CubicRoots((float)a.X, (float)b.X, (float)c.X, (float)d.X, roots);
                    count += CubicRoots((float)a.Y, (float)b.Y, (float)c.Y, (float)d.Y, roots[count..]);
                    for (int index = 0; index < count; index++)
                    {
                        work.Consume(token);
                        Include(new(
                            EvalCubic((float)a.X, (float)b.X, (float)c.X, (float)d.X, roots[index]),
                            EvalCubic((float)a.Y, (float)b.Y, (float)c.Y, (float)d.Y, roots[index])));
                    }
                    current = command.Third;
                    Include(d);
                    break;
                case SvgAuthoredPathVerb.Arc:
                    for (int phase = 0; phase <= SvgEndpointArc.MaximumConicCount; phase++) work.Consume(token);
                    int conics = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X, (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0, command.Third, arcs, token);
                    for (int index = 0; index < conics; index++)
                    {
                        Quad(current, arcs[index].Control, arcs[index].End, arcs[index].Weight, true, roots, work, token);
                        current = arcs[index].End;
                    }
                    current = command.Third;
                    Include(Map(current));
                    break;
                case SvgAuthoredPathVerb.End:
                case SvgAuthoredPathVerb.Close:
                    current = first;
                    break;
                default: throw new InvalidOperationException("Invalid authored path verb.");
            }
        }
        if (commands.IsEmpty) return default;
        float width = right - left, height = bottom - top;
        if (!float.IsFinite(width) || !float.IsFinite(height)) throw new SvgException(SvgErrorCode.LimitExceeded, "Path bounds exceed the finite float range.");
        return new(left, top, width, height, default, true, null);

        SvgDevicePoint Map(SvgDevicePoint point)
        {
            if (map == SvgAffineTransform.Identity) return point;
            work.Consume(token);
            return SvgAuthoredPath.MapPoint(map, point, new(0, 0));
        }

        void Include(SvgDevicePoint point)
        {
            float x = (float)point.X, y = (float)point.Y;
            if (!float.IsFinite(x) || !float.IsFinite(y)) throw new SvgException(SvgErrorCode.LimitExceeded, "Path bounds exceed the finite float range.");
            left = MathF.Min(left, x); right = MathF.Max(right, x);
            top = MathF.Min(top, y); bottom = MathF.Max(bottom, y);
        }
        void Quad(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end, float weight, bool conic, Span<float> candidates, ParserWorkCounter counter, CancellationToken cancellation)
        {
            counter.Consume(cancellation);
            start = Map(start); control = Map(control); end = Map(end);
            // SkConic::set substitutes one for nonpositive or nonfinite weights.
            if (conic && (!(weight > 0) || !float.IsFinite(weight))) weight = 1;
            int count = QuadRoots((float)start.X, (float)control.X, (float)end.X, weight, conic, candidates);
            count += QuadRoots((float)start.Y, (float)control.Y, (float)end.Y, weight, conic, candidates[count..]);
            for (int index = 0; index < count; index++)
            {
                counter.Consume(cancellation);
                Include(new(
                    EvalQuad((float)start.X, (float)control.X, (float)end.X, weight, candidates[index]),
                    EvalQuad((float)start.Y, (float)control.Y, (float)end.Y, weight, candidates[index])));
            }
            Include(end);
        }

        static int QuadRoots(float a, float b, float c, float weight, bool conic, Span<float> roots)
        {
            if (!conic) return UnitDivide(a - b, a - b - b + c, out roots[0]) ? 1 : 0;
            float p20 = c - a, p10 = b - a, wp10 = weight * p10;
            int count = UnitRoots(weight * p20 - p20, p20 - 2 * wp10, wp10, roots);
            return count == 1 ? 1 : 0;
        }
        static int CubicRoots(float a, float b, float c, float d, Span<float> roots)
            => UnitRoots(d - a + 3 * (b - c), 2 * (a - b - b + c), b - a, roots);
        static int UnitRoots(float a, float b, float c, Span<float> roots)
        {
            if (a == 0) return UnitDivide(-c, b, out roots[0]) ? 1 : 0;
            double discriminant = (double)b * b - 4 * (double)a * c;
            if (discriminant < 0) return 0;
            float root = (float)Math.Sqrt(discriminant);
            if (!float.IsFinite(root)) return 0;
            float q = b < 0 ? -(b - root) / 2 : -(b + root) / 2;
            int count = 0;
            if (UnitDivide(q, a, out float first)) roots[count++] = first;
            if (UnitDivide(c, q, out float second)) roots[count++] = second;
            if (count == 2)
            {
                if (roots[0] > roots[1]) (roots[0], roots[1]) = (roots[1], roots[0]);
                else if (roots[0] == roots[1]) count = 1;
            }
            return count;
        }
        static bool UnitDivide(float numerator, float denominator, out float ratio)
        {
            ratio = 0;
            if (numerator < 0) { numerator = -numerator; denominator = -denominator; }
            if (denominator == 0 || numerator == 0 || numerator >= denominator) return false;
            float value = numerator / denominator;
            if (float.IsNaN(value) || value == 0) return false;
            ratio = value; return true;
        }
        static float EvalQuad(float a, float b, float c, float weight, float t)
        {
            float weighted = weight * b;
            float numerator = ((c - 2 * weighted + a) * t + 2 * (weighted - a)) * t + a;
            float denominatorB = 2 * (weight - 1);
            return numerator / ((-denominatorB * t + denominatorB) * t + 1);
        }
        static float EvalCubic(float a, float b, float c, float d, float t)
            => (((d + 3 * (b - c) - a) * t + 3 * (c - 2 * b + a)) * t + 3 * (b - a)) * t + a;
    }
}
