// Native pre-intersection conic splitting adapts pinned SkOpEdgeBuilder.cpp / SkGeometry.cpp.
// Copyright 2012 Google Inc.; 2006 The Android Open Source Project. See THIRD_PARTY_NOTICES.txt.
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal static partial class SvgClipUnion
{
    private readonly record struct CurveEdge(SvgClipCurve Curve, int Child);
    private readonly record struct CurveCut(int Edge, double T, SvgDevicePoint Point) : IComparable<CurveCut>
    {
        public int CompareTo(CurveCut other) => Edge != other.Edge ? Edge.CompareTo(other.Edge) : T.CompareTo(other.T);
    }
    private readonly record struct CurveBoundary(int Edge, double First, double Last, SvgDevicePoint Start, SvgDevicePoint End, bool Emitted);
    private readonly record struct Homogeneous(double X, double Y, double Z)
    {
        internal Homogeneous Cross(Homogeneous other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
        internal double Dot(double x, double y, double z) => X * x + Y * y + Z * z;
    }

    /// <summary>Resolves ordered source curves with each child's independent winding rule.</summary>
    /// <remarks>Sources remain borrowed. Native float mapping/curvature splitting precedes double
    /// intersections; only the resolved controls narrow back to raster operands. Temporary lists reserve
    /// before growth. Pair, root, winding and linking visits charge shared work/token. The returned
    /// command owner transfers on success; failure leaves source owners intact and releases scratch.</remarks>
    internal static SvgAuthoredPath ResolveNativeUnion(SvgAuthoredPath first, bool firstEvenOdd,
        SvgAuthoredPath second, bool secondEvenOdd, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        using RenderBudget.BudgetReservation allowance = budget.Reserve(512, SvgMemoryCategory.Geometry);
        using BudgetedParserList<CurveEdge> edges = new(budget, SvgMemoryCategory.Geometry, work, token);
        AppendSource(first, 0, edges, work, token); AppendSource(second, 1, edges, work, token);
        using BudgetedParserList<CurveCut> cuts = new(budget, SvgMemoryCategory.Geometry, work, token);
        for (int index = 0; index < edges.Count; index++)
        {
            work.Consume(token);
            cuts.Add(new(index, 0, edges[index].Curve.Start)); cuts.Add(new(index, 1, edges[index].Curve.End));
        }
        for (int left = 0; left < edges.Count; left++)
            for (int right = left + 1; right < edges.Count; right++)
            {
                work.Consume(token);
                Intersections(edges[left].Curve, edges[right].Curve, left, right, cuts, work, token);
            }
        cuts.Sort(work, token);
        using BudgetedParserList<CurveBoundary> boundary = new(budget, SvgMemoryCategory.Geometry, work, token);
        Span<double> x = stackalloc double[4], y = stackalloc double[4], z = stackalloc double[4], equation = stackalloc double[4], roots = stackalloc double[6];
        for (int index = 1; index < cuts.Count; index++)
        {
            work.Consume(token);
            CurveCut a = cuts[index - 1], b = cuts[index];
            if (a.Edge != b.Edge || a.Point == b.Point || b.T - a.T <= 1e-13) continue;
            SvgClipCurve curve = edges[a.Edge].Curve;
            double middle = (a.T + b.T) / 2;
            SvgDevicePoint point = curve.Evaluate(middle), tangent = curve.Tangent(middle);
            if (tangent.X == 0 && tangent.Y == 0) continue;
            bool rayY = Math.Abs(tangent.X) > Math.Abs(tangent.Y);
            int left0 = 0, right0 = 0, left1 = 0, right1 = 0;
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                work.Consume(token);
                CurveEdge other = edges[edgeIndex]; other.Curve.Coefficients(x, y, z);
                double target = rayY ? point.X : point.Y;
                for (int coefficient = 0; coefficient < 4; coefficient++) equation[coefficient] = (rayY ? x[coefficient] : y[coefficient]) - target * z[coefficient];
                int count = SvgClipCurve.Roots(equation, roots, work, token);
                for (int rootIndex = 0; rootIndex < count; rootIndex++)
                {
                    work.Consume(token);
                    double t = roots[rootIndex]; SvgDevicePoint crossing = other.Curve.Evaluate(t), direction = other.Curve.Tangent(t);
                    double velocity = rayY ? direction.X : direction.Y;
                    if (velocity == 0 || (t == 0 && velocity < 0) || (t == 1 && velocity > 0)) continue;
                    int sign = velocity > 0 ? 1 : -1;
                    if (rayY) sign = -sign;
                    double delta = rayY ? crossing.Y - point.Y : crossing.X - point.X;
                    double error = Math.Max(1, Math.Max(Math.Abs(point.X), Math.Abs(point.Y))) * 128 * 2.2204460492503131e-16;
                    bool leftInside, rightInside;
                    if (Math.Abs(delta) <= error)
                    {
                        double nx = -tangent.Y, ny = tangent.X;
                        double movement = rayY ? (direction.Y * nx - direction.X * ny) / velocity
                            : (direction.X * ny - direction.Y * nx) / velocity;
                        leftInside = movement > 0; rightInside = movement < 0;
                    }
                    else leftInside = rightInside = delta > 0;
                    if (other.Child == 0) { if (leftInside) left0 += sign; if (rightInside) right0 += sign; }
                    else { if (leftInside) left1 += sign; if (rightInside) right1 += sign; }
                }
            }
            bool leftFill = Filled(left0, firstEvenOdd) || Filled(left1, secondEvenOdd);
            bool rightFill = Filled(right0, firstEvenOdd) || Filled(right1, secondEvenOdd);
            if (leftFill == rightFill) continue;
            CurveBoundary part = leftFill ? new(a.Edge, a.T, b.T, a.Point, b.Point, false) : new(a.Edge, b.T, a.T, b.Point, a.Point, false);
            bool duplicate = false;
            foreach (CurveBoundary existing in boundary.AsSpan())
            {
                work.Consume(token);
                if (existing.Start == part.Start && existing.End == part.End && edges[existing.Edge].Curve.Evaluate((existing.First + existing.Last) / 2) == point)
                { duplicate = true; break; }
            }
            if (!duplicate) boundary.Add(part);
        }
        using BudgetedParserList<SvgAuthoredPathCommand> output = new(budget, SvgMemoryCategory.Scene, work, token);
        int remaining = boundary.Count;
        while (remaining > 0)
        {
            int seed = 0;
            while (boundary[seed].Emitted) { work.Consume(token); seed++; }
            SvgDevicePoint start = boundary[seed].Start;
            output.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, start, default, default));
            int current = seed;
            while (true)
            {
                work.Consume(token);
                CurveBoundary part = boundary[current]; boundary[current] = part with { Emitted = true }; remaining--;
                output.Add(edges[part.Edge].Curve.Cut(part.First, part.Last, part.End));
                if (part.End == start) break;
                int next = -1;
                for (int index = 0; index < boundary.Count; index++)
                {
                    work.Consume(token);
                    if (!boundary[index].Emitted && boundary[index].Start == part.End) { next = index; break; }
                }
                if (next < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The native curve clip union boundary could not be linked.");
                current = next;
            }
            output.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
        }
        OwnedParserArray<SvgAuthoredPathCommand> owned = output.Detach();
        try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
        catch { owned.Dispose(); throw; }

        static bool Filled(int winding, bool parity) => parity ? (winding & 1) != 0 : winding != 0;

        static void AppendSource(SvgAuthoredPath source, int child, BudgetedParserList<CurveEdge> edges, ParserWorkCounter work, CancellationToken token)
        {
            SvgDevicePoint current = default, first = default;
            bool active = false;
            Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
            foreach (SvgAuthoredPathCommand command in source.Commands)
            {
                work.Consume(token);
                if (command.Verb == SvgAuthoredPathVerb.Move)
                {
                    if (active && current != first) Add(new(SvgMeasuredCurveKind.Line, current, default, default, first, 1), source.SceneMap, child, edges, work, token);
                    current = first = command.First; active = true; continue;
                }
                if (command.Verb is SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Close)
                {
                    if (active && current != first) Add(new(SvgMeasuredCurveKind.Line, current, default, default, first, 1), source.SceneMap, child, edges, work, token);
                    current = first; active = false; continue;
                }
                if (command.Verb == SvgAuthoredPathVerb.Arc)
                {
                    for (int phase = 0; phase <= SvgEndpointArc.MaximumConicCount; phase++) work.Consume(token);
                    int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                        (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0, command.Third, arcs, token);
                    if (count == 0) Add(new(SvgMeasuredCurveKind.Line, current, default, default, command.Third, 1), source.SceneMap, child, edges, work, token);
                    else for (int index = 0; index < count; index++)
                    {
                        Add(new(SvgMeasuredCurveKind.Conic, current, arcs[index].Control, default, arcs[index].End, arcs[index].Weight), source.SceneMap, child, edges, work, token);
                        current = arcs[index].End;
                    }
                    current = command.Third; continue;
                }
                SvgClipCurve curve = command.Verb switch
                {
                    SvgAuthoredPathVerb.Line => new(SvgMeasuredCurveKind.Line, current, default, default, command.First, 1),
                    SvgAuthoredPathVerb.Quadratic => new(SvgMeasuredCurveKind.Quadratic, current, command.First, default, command.Second, 1),
                    SvgAuthoredPathVerb.Conic => new(SvgMeasuredCurveKind.Conic, current, command.First, default, command.Second, (float)command.Third.X),
                    SvgAuthoredPathVerb.Cubic => new(SvgMeasuredCurveKind.Cubic, current, command.First, command.Second, command.Third, 1),
                    _ => throw new InvalidOperationException("Invalid native clip curve verb.")
                };
                Add(curve, source.SceneMap, child, edges, work, token); current = curve.End;
            }
            if (active && current != first) Add(new(SvgMeasuredCurveKind.Line, current, default, default, first, 1), source.SceneMap, child, edges, work, token);

            // Native path-ops splits a conic at the float quadratic-curvature
            // parameter before intersection construction, even if no other edge cuts it.
            static void Add(SvgClipCurve curve, SvgAffineTransform map, int child, BudgetedParserList<CurveEdge> edges, ParserWorkCounter work, CancellationToken token)
            {
                for (int phase = 0; phase < 4; phase++) work.Consume(token);
                curve = curve with { Start = SvgAuthoredPath.MapPoint(map, curve.Start, new(0, 0)), First = SvgAuthoredPath.MapPoint(map, curve.First, new(0, 0)), Second = SvgAuthoredPath.MapPoint(map, curve.Second, new(0, 0)), End = SvgAuthoredPath.MapPoint(map, curve.End, new(0, 0)) };
                if (curve.Kind == SvgMeasuredCurveKind.Cubic)
                {
                    double dx = curve.End.X - curve.Start.X, dy = curve.End.Y - curve.Start.Y;
                    if ((dx != 0 || dy != 0)
                        && (curve.First.X - curve.Start.X) * dy == (curve.First.Y - curve.Start.Y) * dx
                        && (curve.Second.X - curve.Start.X) * dy == (curve.Second.Y - curve.Start.Y) * dx)
                        curve = new(SvgMeasuredCurveKind.Line, curve.Start, default, default, curve.End, 1);
                    else if (curve.End.X + 3 * (curve.First.X - curve.Second.X) - curve.Start.X == 0
                        && curve.End.Y + 3 * (curve.First.Y - curve.Second.Y) - curve.Start.Y == 0)
                        curve = new(SvgMeasuredCurveKind.Quadratic, curve.Start,
                            SvgClipCurve.Narrow(new(1.5 * curve.First.X - .5 * curve.Start.X, 1.5 * curve.First.Y - .5 * curve.Start.Y)), default, curve.End, 1);
                }
                if (curve.Kind == SvgMeasuredCurveKind.Line && curve.Start == curve.End) return;
                if (curve.Kind is SvgMeasuredCurveKind.Conic or SvgMeasuredCurveKind.Quadratic)
                {
                    float ax = (float)curve.First.X - (float)curve.Start.X, ay = (float)curve.First.Y - (float)curve.Start.Y;
                    float cx = (float)curve.End.X - (float)curve.First.X, cy = (float)curve.End.Y - (float)curve.First.Y;
                    float bx = (float)curve.Start.X - (float)curve.First.X - (float)curve.First.X + (float)curve.End.X;
                    float by = (float)curve.Start.Y - (float)curve.First.Y - (float)curve.First.Y + (float)curve.End.Y;
                    float numerator = -(ax * bx + ay * by), denominator = bx * bx + by * by;
                    if (ax * cx + ay * cy < 0 && numerator > 0 && numerator < denominator)
                    {
                        float t = numerator / denominator;
                        Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[3] { curve.Start, curve.First, curve.End };
                        Span<SvgDevicePoint> left = stackalloc SvgDevicePoint[4], right = stackalloc SvgDevicePoint[4];
                        if (SvgCurveMeasure.Cut(curve.Kind, points, curve.Weight, 0, t, left, out _, out float leftWeight, work, token)
                            && SvgCurveMeasure.Cut(curve.Kind, points, curve.Weight, t, 1, right, out _, out float rightWeight, work, token))
                        {
                            edges.Add(new(new(curve.Kind, left[0], left[1], default, left[2], leftWeight), child));
                            edges.Add(new(new(curve.Kind, left[2], right[1], default, right[2], rightWeight), child));
                            return;
                        }
                    }
                }
                edges.Add(new(curve, child));
            }
        }
    }

    private static void Intersections(SvgClipCurve a, SvgClipCurve b, int left, int right, BudgetedParserList<CurveCut> cuts, ParserWorkCounter work, CancellationToken token)
    {
        Span<double> ax = stackalloc double[4], ay = stackalloc double[4], az = stackalloc double[4], equation = stackalloc double[7], roots = stackalloc double[6];
        a.Coefficients(ax, ay, az);
        if (b.Kind == SvgMeasuredCurveKind.Cubic)
        {
            if (a.Kind == SvgMeasuredCurveKind.Cubic)
            {
                CubicIntersections(a, b, left, right, cuts, work, token);
                return;
            }
            Intersections(b, a, right, left, cuts, work, token); return;
        }
        if (b.Kind == SvgMeasuredCurveKind.Line)
        {
            double dx = b.End.X - b.Start.X, dy = b.End.Y - b.Start.Y;
            for (int index = 0; index < 4; index++) { work.Consume(token); equation[index] = dx * (ay[index] - b.Start.Y * az[index]) - dy * (ax[index] - b.Start.X * az[index]); }
            int count = SvgClipCurve.Roots(equation[..4], roots, work, token);
            for (int index = 0; index < count; index++)
            {
                SvgDevicePoint p = a.Evaluate(roots[index]);
                double u = Math.Abs(dx) >= Math.Abs(dy) ? (p.X - b.Start.X) / dx : (p.Y - b.Start.Y) / dy;
                Add(roots[index], u, a, b, left, right, cuts, work, token);
            }
            return;
        }
        double weight = b.Kind == SvgMeasuredCurveKind.Conic ? b.Weight : 1;
        Homogeneous p0 = new(b.Start.X, b.Start.Y, 1), p1 = new(b.First.X * weight, b.First.Y * weight, weight), p2 = new(b.End.X, b.End.Y, 1);
        Homogeneous row0 = p1.Cross(p2), row1 = p2.Cross(p0), row2 = p0.Cross(p1);
        Span<double> v0 = stackalloc double[4], v1 = stackalloc double[4], v2 = stackalloc double[4];
        equation.Clear();
        for (int index = 0; index < 4; index++)
        {
            work.Consume(token); v0[index] = row0.Dot(ax[index], ay[index], az[index]); v1[index] = row1.Dot(ax[index], ay[index], az[index]); v2[index] = row2.Dot(ax[index], ay[index], az[index]);
        }
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { work.Consume(token); equation[i + j] += v1[i] * v1[j] - 4 * v0[i] * v2[j]; }
        int found = SvgClipCurve.Roots(equation, roots, work, token);
        for (int index = 0; index < found; index++)
        {
            double t = roots[index], v = SvgClipCurve.Polynomial(v1, t), first = SvgClipCurve.Polynomial(v0, t), last = SvgClipCurve.Polynomial(v2, t);
            double u = Math.Abs(first) > Math.Abs(last) ? v / (2 * first + v) : 2 * last / (v + 2 * last);
            Add(t, u, a, b, left, right, cuts, work, token);
        }

        // The symmetric Bezout determinant eliminates the second cubic parameter.
        // Translate and scale by a power of two before polynomial arithmetic, then
        // recover the second parameter and refine against the original source curves.
        static void CubicIntersections(SvgClipCurve a, SvgClipCurve b, int left, int right,
            BudgetedParserList<CurveCut> cuts, ParserWorkCounter work, CancellationToken token)
        {
            double extent = 0;
            foreach (SvgDevicePoint p in (ReadOnlySpan<SvgDevicePoint>)[a.Start, a.First, a.Second, a.End, b.First, b.Second, b.End])
            {
                work.Consume(token);
                extent = Math.Max(extent, Math.Max(Math.Abs(p.X - b.Start.X), Math.Abs(p.Y - b.Start.Y)));
            }
            if (extent == 0) return;
            double scale = Math.ScaleB(1, -Math.ILogB(extent));
            SvgClipCurve na = Normalize(a, b.Start, scale), nb = Normalize(b, b.Start, scale);
            Span<double> ax = stackalloc double[4], ay = stackalloc double[4], bx = stackalloc double[4], by = stackalloc double[4], z = stackalloc double[4];
            na.Coefficients(ax, ay, z); nb.Coefficients(bx, by, z);
            Span<double> b00 = stackalloc double[4], b01 = stackalloc double[4], b02 = stackalloc double[4], b11 = stackalloc double[4];
            for (int i = 0; i < 4; i++)
            {
                work.Consume(token);
                double f0 = (i == 0 ? bx[0] : 0) - ax[i], g0 = (i == 0 ? by[0] : 0) - ay[i];
                b00[i] = bx[1] * g0 - by[1] * f0;
                b01[i] = bx[2] * g0 - by[2] * f0;
                b02[i] = bx[3] * g0 - by[3] * f0;
                b11[i] = b02[i] + (i == 0 ? bx[2] * by[1] - bx[1] * by[2] : 0);
            }
            double b12 = bx[3] * by[1] - bx[1] * by[3], b22 = bx[3] * by[2] - bx[2] * by[3];
            Span<double> equation = stackalloc double[10], roots = stackalloc double[9];
            equation.Clear();
            for (int i = 0; i < 4; i++)
            {
                work.Consume(token); equation[i] -= b00[i] * b12 * b12;
                for (int j = 0; j < 4; j++)
                {
                    work.Consume(token);
                    equation[i + j] += b00[i] * b11[j] * b22 + 2 * b01[i] * b02[j] * b12 - b01[i] * b01[j] * b22;
                    for (int k = 0; k < 4; k++)
                    { work.Consume(token); equation[i + j + k] -= b11[i] * b02[j] * b02[k]; }
                }
            }
            int count = SvgClipCurve.Roots(equation, roots, work, token);
            bool coincident = true;
            foreach (double coefficient in equation) { work.Consume(token); coincident &= coefficient == 0; }
            if (coincident)
            {
                // A shared cubic locus has an identically zero resultant. Both
                // endpoint pairs delimit any partial overlap before winding selection.
                ProjectEndpoints(na, nb, a, b, left, right, cuts, work, token);
                ProjectEndpoints(nb, na, b, a, right, left, cuts, work, token);
                return;
            }
            double xMotion = Math.Abs(bx[1]) + Math.Abs(bx[2]) + Math.Abs(bx[3]);
            double yMotion = Math.Abs(by[1]) + Math.Abs(by[2]) + Math.Abs(by[3]);
            Span<double> parameter = stackalloc double[4], values = stackalloc double[3];
            for (int i = 0; i < count; i++)
            {
                work.Consume(token); SvgDevicePoint p = na.Evaluate(roots[i]);
                (xMotion >= yMotion ? bx : by).CopyTo(parameter);
                parameter[0] -= xMotion >= yMotion ? p.X : p.Y;
                int found = SvgClipCurve.Roots(parameter, values, work, token);
                for (int j = 0; j < found; j++) Add(roots[i], values[j], a, b, left, right, cuts, work, token);
            }

            static SvgClipCurve Normalize(SvgClipCurve curve, SvgDevicePoint origin, double scale)
                => curve with { Start = Point(curve.Start, origin, scale), First = Point(curve.First, origin, scale), Second = Point(curve.Second, origin, scale), End = Point(curve.End, origin, scale) };
            static SvgDevicePoint Point(SvgDevicePoint point, SvgDevicePoint origin, double scale)
                => new((point.X - origin.X) * scale, (point.Y - origin.Y) * scale);

            static void ProjectEndpoints(SvgClipCurve source, SvgClipCurve target, SvgClipCurve originalSource, SvgClipCurve originalTarget,
                int sourceIndex, int targetIndex, BudgetedParserList<CurveCut> cuts, ParserWorkCounter work, CancellationToken token)
            {
                Span<double> x = stackalloc double[4], y = stackalloc double[4], z = stackalloc double[4], parameter = stackalloc double[4], values = stackalloc double[3];
                target.Coefficients(x, y, z);
                bool useX = Math.Abs(x[1]) + Math.Abs(x[2]) + Math.Abs(x[3]) >= Math.Abs(y[1]) + Math.Abs(y[2]) + Math.Abs(y[3]);
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    work.Consume(token); SvgDevicePoint p = source.Evaluate(endpoint);
                    (useX ? x : y).CopyTo(parameter); parameter[0] -= useX ? p.X : p.Y;
                    int found = SvgClipCurve.Roots(parameter, values, work, token);
                    for (int index = 0; index < found; index++)
                        Add(endpoint, values[index], originalSource, originalTarget, sourceIndex, targetIndex, cuts, work, token);
                }
            }
        }

        static void Add(double t, double u, SvgClipCurve a, SvgClipCurve b, int left, int right, BudgetedParserList<CurveCut> cuts, ParserWorkCounter work, CancellationToken token)
        {
            if (!double.IsFinite(u) || u < -1e-10 || u > 1 + 1e-10) return;
            u = Math.Clamp(u, 0, 1);
            for (int iteration = 0; iteration < 12; iteration++)
            {
                work.Consume(token);
                SvgDevicePoint p = a.Evaluate(t), q = b.Evaluate(u), da = a.Tangent(t), db = b.Tangent(u);
                double dx = p.X - q.X, dy = p.Y - q.Y, denominator = da.X * db.Y - da.Y * db.X;
                if (denominator == 0) break;
                double nextT = t - (dx * db.Y - dy * db.X) / denominator;
                double nextU = u - (dx * da.Y - dy * da.X) / denominator;
                if (nextT < -1e-10 || nextT > 1 + 1e-10 || nextU < -1e-10 || nextU > 1 + 1e-10) return;
                nextT = Math.Clamp(nextT, 0, 1); nextU = Math.Clamp(nextU, 0, 1);
                if (nextT == t && nextU == u) break;
                t = nextT; u = nextU;
            }
            SvgDevicePoint pa = a.Evaluate(t), pb = b.Evaluate(u);
            double scale = Math.Max(1, Math.Max(Math.Abs(pa.X), Math.Abs(pa.Y)));
            if (Math.Abs(pa.X - pb.X) + Math.Abs(pa.Y - pb.Y) > scale * 1e-10) return;
            if (t < 1e-12) t = 0; else if (t > 1 - 1e-12) t = 1;
            if (u < 1e-12) u = 0; else if (u > 1 - 1e-12) u = 1;
            SvgDevicePoint point = SvgClipCurve.Narrow(t is 0 or 1 ? a.Evaluate(t) : u is 0 or 1 ? b.Evaluate(u) : pa);
            cuts.Add(new(left, t, point)); cuts.Add(new(right, u, point));
        }
    }
}
