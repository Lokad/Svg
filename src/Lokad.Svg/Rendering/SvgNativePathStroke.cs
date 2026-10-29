// Adapts SkStroke.cpp and SkStrokerPriv.cpp at the revision in THIRD_PARTY_NOTICES.txt.
// Copyright 2006, 2008 The Android Open Source Project.
using Lokad.Svg.Parsing;
using Point = Lokad.Svg.Rendering.SvgCurveStroke.Point;
using Curve = Lokad.Svg.Rendering.SvgCurveStroke.Curve;

namespace Lokad.Svg.Rendering;

internal enum SvgStrokeOutlineVerb { Move, Line, Quadratic, Conic, Close }

/// <summary>One native directed outline command; quadratic/conic controls and weights remain retained.</summary>
internal readonly record struct SvgStrokeOutlineCommand(SvgStrokeOutlineVerb Verb, Point Control, Point End, float Weight);

/// <summary>Constructs native curve offsets, joins and caps before flattening or final placement.</summary>
/// <remarks>Input and output belong to the caller. Private buffers are reserved before allocation and
/// never published on failure. Every verb, emitted segment, reversal and numerical phase observes
/// cancellation. Numerical scratch is bounded stack storage; command counts are bounded
/// independently of recursion. No shared mutable state or runtime native dependency exists.</remarks>
internal static class SvgNativePathStroke
{
    internal const int MaximumCommands = 65536;

    internal static void Build(ReadOnlySpan<SvgAuthoredPathCommand> commands, float radius, float miterLimit,
        SvgStrokeCap cap, SvgStrokeJoin join, float resolutionScale, BudgetedParserList<SvgStrokeOutlineCommand> output,
        RenderBudget budget, CancellationToken token)
        => Build(commands, radius, miterLimit, cap, join, resolutionScale, SvgAffineTransform.Identity, output, budget, token);

    internal static void Build(ReadOnlySpan<SvgAuthoredPathCommand> commands, float radius, float miterLimit,
        SvgStrokeCap cap, SvgStrokeJoin join, float resolutionScale, SvgAffineTransform constructionMap,
        BudgetedParserList<SvgStrokeOutlineCommand> output, RenderBudget budget, CancellationToken token)
        => BuildCore(commands, radius, miterLimit, cap, join, resolutionScale, constructionMap, true, output, budget, token);

    /// <summary>Runs only the contour stroker, matching the direct-verb native numerical oracle.</summary>
    internal static void BuildGeneric(ReadOnlySpan<SvgAuthoredPathCommand> commands, float radius, float miterLimit,
        SvgStrokeCap cap, SvgStrokeJoin join, float resolutionScale, BudgetedParserList<SvgStrokeOutlineCommand> output,
        RenderBudget budget, CancellationToken token)
        => BuildCore(commands, radius, miterLimit, cap, join, resolutionScale, SvgAffineTransform.Identity, false, output, budget, token);

    private static void BuildCore(ReadOnlySpan<SvgAuthoredPathCommand> commands, float radius, float miterLimit,
        SvgStrokeCap cap, SvgStrokeJoin join, float resolutionScale, SvgAffineTransform constructionMap, bool recognizeRectangle,
        BudgetedParserList<SvgStrokeOutlineCommand> output, RenderBudget budget, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(budget);
        token.ThrowIfCancellationRequested();
        if (output.Count != 0) throw new ArgumentException("Native stroke output must start empty.", nameof(output));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(miterLimit) || miterLimit <= 0) throw new ArgumentOutOfRangeException(nameof(miterLimit));
        if (!float.IsFinite(resolutionScale) || resolutionScale <= 0) throw new ArgumentOutOfRangeException(nameof(resolutionScale));
        if (cap is not SvgStrokeCap.Butt and not SvgStrokeCap.Round and not SvgStrokeCap.Square) throw new ArgumentOutOfRangeException(nameof(cap));
        if (join is not SvgStrokeJoin.Miter and not SvgStrokeJoin.Round and not SvgStrokeJoin.Bevel) throw new ArgumentOutOfRangeException(nameof(join));
        if (recognizeRectangle)
        {
            SvgRectanglePathRecognizer recognizer = new();
            bool recognized = true;
            foreach (SvgAuthoredPathCommand command in commands)
            {
                token.ThrowIfCancellationRequested();
                SvgDevicePoint point = command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line
                    ? MapPoint(constructionMap, command.First) : default;
                if (!recognizer.Add(command.Verb, point)) { recognized = false; break; }
            }
            if (recognized && recognizer.Finish() is SvgRecognizedRectangle { Closed: true } rectangle)
            {
                BuildRectangle(rectangle, radius, miterLimit, join, output, token);
                return;
            }
        }
        using RenderBudget.BudgetReservation owners = budget.Reserve(1024, SvgMemoryCategory.Geometry);
        using BudgetedParserList<SvgStrokeOutlineCommand> inner = new(budget, SvgMemoryCategory.Geometry, token);
        using BudgetedParserList<SvgStrokeOutlineCommand> cusps = new(budget, SvgMemoryCategory.Geometry, token);
        using BudgetedParserList<SvgStrokeCurveSegment> fitted = new(budget, SvgMemoryCategory.Geometry, token);
        Stroker state = new(radius, miterLimit, cap, join, resolutionScale, output, inner, cusps, fitted, token);
        Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        SvgDevicePoint rawCurrent = default, rawFirst = default;
        int tangentIndex = -1;
        SvgDevicePoint tangentOrigin = default;
        bool futureTangent = false;
        for (int index = 0; index < commands.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgAuthoredPathCommand command = commands[index];
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move: state.Move(Point.Narrow(MapPoint(constructionMap, command.First))); rawCurrent = rawFirst = command.First; break;
                case SvgAuthoredPathVerb.Line:
                    Point endpoint = Point.Narrow(MapPoint(constructionMap, command.First));
                    bool future = false;
                    if (state.NeedsLookahead(endpoint))
                    {
                        // A scan crosses only commands coincident with this origin.
                        // Reuse it until the tangent/contour boundary or origin changes.
                        if (index >= tangentIndex || endpoint.Device() != tangentOrigin)
                        {
                            tangentOrigin = endpoint.Device();
                            (tangentIndex, futureTangent) = FindFutureTangent(commands, index + 1, constructionMap, tangentOrigin, token);
                        }
                        future = futureTangent;
                    }
                    state.Line(endpoint, future); state.LastLine = true; rawCurrent = command.First; break;
                case SvgAuthoredPathVerb.Quadratic: state.Quadratic(Point.Narrow(MapPoint(constructionMap, command.First)), Point.Narrow(MapPoint(constructionMap, command.Second)), 1, false); state.LastLine = false; rawCurrent = command.Second; break;
                case SvgAuthoredPathVerb.Conic: state.Quadratic(Point.Narrow(MapPoint(constructionMap, command.First)), Point.Narrow(MapPoint(constructionMap, command.Second)), (float)command.Third.X, true); state.LastLine = false; rawCurrent = command.Second; break;
                case SvgAuthoredPathVerb.Cubic: state.Cubic(Point.Narrow(MapPoint(constructionMap, command.First)), Point.Narrow(MapPoint(constructionMap, command.Second)), Point.Narrow(MapPoint(constructionMap, command.Third))); state.LastLine = false; rawCurrent = command.Third; break;
                case SvgAuthoredPathVerb.Arc:
                    token.ThrowIfCancellationRequested();
                    int count = SvgEndpointArc.Convert(rawCurrent, command.First.X, command.First.Y, command.Second.X,
                        (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0, command.Third, arcs, token);
                    if (count == 0) { state.Line(Point.Narrow(MapPoint(constructionMap, command.Third)), false); state.LastLine = true; }
                    else
                    {
                        for (int arc = 0; arc < count; arc++) state.Quadratic(Point.Narrow(MapPoint(constructionMap, arcs[arc].Control)), Point.Narrow(MapPoint(constructionMap, arcs[arc].End)), arcs[arc].Weight, true);
                        state.LastLine = false;
                    }
                    rawCurrent = command.Third; break;
                case SvgAuthoredPathVerb.Close: state.Close(MapPoint(constructionMap, rawCurrent) != MapPoint(constructionMap, rawFirst)); rawCurrent = rawFirst; break;
                case SvgAuthoredPathVerb.End: break;
                default: throw new InvalidOperationException("Invalid stroke centerline verb.");
            }
        }
        state.Finish(false, state.LastLine);

        static (int Index, bool HasTangent) FindFutureTangent(ReadOnlySpan<SvgAuthoredPathCommand> commands, int start, SvgAffineTransform map,
            SvgDevicePoint current, CancellationToken token)
        {
            // Charge each inspected verb, including the cached range's boundary.
            for (int index = start; index < commands.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgAuthoredPathCommand command = commands[index];
                if (command.Verb is SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.End or SvgAuthoredPathVerb.Close) return (index, false);
                if (command.Verb == SvgAuthoredPathVerb.Line)
                { SvgDevicePoint end = MapPoint(map, command.First); if (current != end) return (index, true); current = end; }
                else if (command.Verb is SvgAuthoredPathVerb.Conic or SvgAuthoredPathVerb.Quadratic)
                { SvgDevicePoint end = MapPoint(map, command.Second); if (current != MapPoint(map, command.First) || current != end) return (index, true); current = end; }
                else if (command.Verb == SvgAuthoredPathVerb.Cubic)
                { SvgDevicePoint end = MapPoint(map, command.Third); if (current != MapPoint(map, command.First) || current != MapPoint(map, command.Second) || current != end) return (index, true); current = end; }
                else if (command.Verb == SvgAuthoredPathVerb.Arc)
                { SvgDevicePoint end = MapPoint(map, command.Third); if (current != end) return (index, true); current = end; }
            }
            return (commands.Length, false);
        }

        static void BuildRectangle(SvgRecognizedRectangle rectangle, float radius, float limit, SvgStrokeJoin join,
            BudgetedParserList<SvgStrokeOutlineCommand> output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            float left = rectangle.Left - radius, top = rectangle.Top - radius;
            float right = rectangle.Right + radius, bottom = rectangle.Bottom + radius;
            if (!float.IsFinite(left) || !float.IsFinite(top) || !float.IsFinite(right) || !float.IsFinite(bottom))
                throw new SvgException(SvgErrorCode.LimitExceeded, "Rectangle stroke construction exceeds the finite float range.");
            if (join == SvgStrokeJoin.Miter && limit < 1.4142135623730951f) join = SvgStrokeJoin.Bevel;
            if (join == SvgStrokeJoin.Miter) AddRectangle(left, top, right, bottom, rectangle.Clockwise, 0, output, token);
            else
            {
                bool closed = false;
                Span<Point> points = stackalloc Point[8];
                if (join == SvgStrokeJoin.Bevel)
                {
                    points[0] = new(rectangle.Left, top); points[1] = new(rectangle.Right, top);
                    points[2] = new(right, rectangle.Top); points[3] = new(right, rectangle.Bottom);
                    points[4] = new(rectangle.Right, bottom); points[5] = new(rectangle.Left, bottom);
                    points[6] = new(left, rectangle.Bottom); points[7] = new(left, rectangle.Top);
                    for (int index = 0; index < 8; index++)
                        Add(index == 0 ? SvgStrokeOutlineVerb.Move : SvgStrokeOutlineVerb.Line, default,
                            points[rectangle.Clockwise ? index : 7 - index], 1, output, token);
                }
                else
                {
                    float outerWidth = right - left, outerHeight = bottom - top;
                    if (!float.IsFinite(outerWidth) || !float.IsFinite(outerHeight))
                        throw new SvgException(SvgErrorCode.LimitExceeded, "Rounded rectangle stroke dimensions exceed the finite float range.");
                    float rx = radius, ry = radius;
                    if (outerWidth < radius + radius || outerHeight < radius + radius)
                    {
                        float scale = MathF.Min(outerWidth / (radius + radius), outerHeight / (radius + radius));
                        rx *= scale; ry *= scale;
                    }
                    if (rx <= 0 || ry <= 0)
                        {
                        AddRectangle(left, top, right, bottom, rectangle.Clockwise, rectangle.Clockwise ? 3 : 0, output, token);
                        closed = true;
                    }
                    else if (rx >= outerWidth / 2 && ry >= outerHeight / 2)
                    {
                        float midX = (float)(.5 * ((double)left + right)), midY = (float)(.5 * ((double)top + bottom));
                        points[0] = new(midX, top); points[1] = new(right, midY);
                        points[2] = new(midX, bottom); points[3] = new(left, midY);
                        Span<Point> corners = stackalloc Point[] { new(left, top), new(right, top), new(right, bottom), new(left, bottom) };
                        int index = 3, controlIndex = rectangle.Clockwise ? 3 : 0, step = rectangle.Clockwise ? 1 : 3;
                        Add(SvgStrokeOutlineVerb.Move, default, points[index], 1, output, token);
                        for (int corner = 0; corner < 4; corner++)
                        {
                            index = (index + step) & 3; controlIndex = (controlIndex + step) & 3;
                            Add(SvgStrokeOutlineVerb.Conic, corners[controlIndex], points[index], SvgRationalQuadratic.QuarterCircleWeight, output, token);
                        }
                    }
                    else
                    {
                        points[0] = new(left + rx, top); points[1] = new(right - rx, top);
                        points[2] = new(right, top + ry); points[3] = new(right, bottom - ry);
                        points[4] = new(right - rx, bottom); points[5] = new(left + rx, bottom);
                        points[6] = new(left, bottom - ry); points[7] = new(left, top + ry);
                        Span<Point> corners = stackalloc Point[] { new(left, top), new(right, top), new(right, bottom), new(left, bottom) };
                        int index = rectangle.Clockwise ? 6 : 7, step = rectangle.Clockwise ? 1 : 7;
                        Add(SvgStrokeOutlineVerb.Move, default, points[index], 1, output, token);
                        for (int corner = 0; corner < 4; corner++)
                        {
                            index = (index + step) & 7;
                            Add(SvgStrokeOutlineVerb.Line, default, points[index], 1, output, token);
                            int controlIndex = rectangle.Clockwise ? ((index + 1) & 7) / 2 : index / 2;
                            index = (index + step) & 7;
                            Add(SvgStrokeOutlineVerb.Conic, corners[controlIndex], points[index], SvgRationalQuadratic.QuarterCircleWeight, output, token);
                        }
                    }
                }
                if (!closed) Add(SvgStrokeOutlineVerb.Close, default, output[^1].End, 1, output, token);
            }
            if (radius * 2 < MathF.Min(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top))
                AddRectangle(rectangle.Left + radius, rectangle.Top + radius, rectangle.Right - radius, rectangle.Bottom - radius,
                    !rectangle.Clockwise, 0, output, token);

            static void AddRectangle(float left, float top, float right, float bottom, bool clockwise, int startIndex,
                BudgetedParserList<SvgStrokeOutlineCommand> output, CancellationToken token)
            {
                Span<Point> points = stackalloc Point[] { new(left, top), new(right, top), new(right, bottom), new(left, bottom) };
                int index = startIndex;
                for (int visit = 0; visit < 4; visit++)
                {
                    Add(visit == 0 ? SvgStrokeOutlineVerb.Move : SvgStrokeOutlineVerb.Line, default, points[index], 1, output, token);
                    index = (index + (clockwise ? 1 : 3)) & 3;
                }
                Add(SvgStrokeOutlineVerb.Close, default, output[^1].End, 1, output, token);
            }

            static void Add(SvgStrokeOutlineVerb verb, Point control, Point end, float weight,
                BudgetedParserList<SvgStrokeOutlineCommand> output, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                _ = control.Device(); _ = end.Device();
                output.Add(new(verb, control, end, weight));
            }
        }
    }

    internal static SvgDevicePoint MapPoint(SvgAffineTransform map, SvgDevicePoint point)
    {
        float x = (float)point.X, y = (float)point.Y;
        if (map.A == 1 && map.B == 0 && map.C == 0 && map.D == 1 && map.E == 0 && map.F == 0)
            return Point.Narrow(point).Device();
        float mappedX = map.IsAxisAligned ? (float)map.A * x + (float)map.E : ((float)map.A * x + (float)map.C * y) + (float)map.E;
        float mappedY = map.IsAxisAligned ? (float)map.D * y + (float)map.F : ((float)map.B * x + (float)map.D * y) + (float)map.F;
        if (!float.IsFinite(mappedX) || !float.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Native stroke mapping exceeds the finite float range.");
        return new(mappedX, mappedY);
    }

    private ref struct Stroker
    {
        private readonly float _radius, _scale, _inverseScale, _inverseMiter;
        private readonly SvgStrokeCap _cap;
        private SvgStrokeJoin _join;
        private readonly BudgetedParserList<SvgStrokeOutlineCommand> _outer, _inner, _cusps;
        private readonly BudgetedParserList<SvgStrokeCurveSegment> _fitted;
        private readonly CancellationToken _token;
        private Point _first, _firstNormal, _firstUnit, _firstOuter, _previousNormal, _previousUnit;
        private int _segments, _firstOuterIndex;
        private bool _previousLine, _joinCompleted;
        internal Point Previous { get; private set; }
        internal bool LastLine { get; set; }

        internal Stroker(float radius, float limit, SvgStrokeCap cap, SvgStrokeJoin join, float scale,
            BudgetedParserList<SvgStrokeOutlineCommand> outer, BudgetedParserList<SvgStrokeOutlineCommand> inner,
            BudgetedParserList<SvgStrokeOutlineCommand> cusps, BudgetedParserList<SvgStrokeCurveSegment> fitted,
CancellationToken token)
        {
            _radius = radius; _scale = scale; _inverseScale = 1 / (scale * 4); _inverseMiter = join == SvgStrokeJoin.Miter && limit > 1 ? 1 / limit : 0;
            _join = join == SvgStrokeJoin.Miter && limit <= 1 ? SvgStrokeJoin.Bevel : join;
            _cap = cap; _outer = outer; _inner = inner; _cusps = cusps; _fitted = fitted; _token = token;
            _segments = -1;
        }

        internal void Move(Point point)
        {
            if (_segments > 0) Finish(false, false);
            _segments = 0; _first = Previous = point; _joinCompleted = false;
        }

        internal void Line(Point point, bool futureTangent)
        {
            float threshold = (1f / 4096) * _inverseScale;
            Point delta = Previous - point;
            bool teeny = MathF.Abs(delta.X) <= threshold && MathF.Abs(delta.Y) <= threshold;
            if (teeny && (_cap == SvgStrokeCap.Butt || _joinCompleted || futureTangent)) return;
            if (!PreJoin(point, true, out Point normal, out Point unit)) return;
            Add(_outer, SvgStrokeOutlineVerb.Line, default, point + normal, 1);
            Add(_inner, SvgStrokeOutlineVerb.Line, default, point - normal, 1);
            PostJoin(point, normal, unit);
        }

        internal bool NeedsLookahead(Point point)
        {
            Point delta = Previous - point;
            float threshold = (1f / 4096) * _inverseScale;
            return _cap != SvgStrokeCap.Butt && !_joinCompleted && MathF.Abs(delta.X) <= threshold && MathF.Abs(delta.Y) <= threshold;
        }

        internal void Quadratic(Point control, Point end, float weight, bool conic)
        {
            Curve curve = new(conic ? SvgMeasuredCurveKind.Conic : SvgMeasuredCurveKind.Quadratic, Previous, control, default, end, weight == 0 ? 1 : weight);
            Point ab = control - Previous, bc = end - control;
            bool degenerateAB = !CanNormalize(ab), degenerateBC = !CanNormalize(bc);
            if (degenerateAB || degenerateBC) { Line(end, false); return; }
            _token.ThrowIfCancellationRequested();
            Span<Point> vertices = stackalloc Point[] { Previous, control, end };
            if (InLine(vertices, .000005f))
            {
                Point a = control - Previous, b = Previous - control - control + end;
                float numerator = -a.Dot(b), denominator = b.Dot(b);
                float t = numerator <= 0 ? 0 : numerator >= denominator ? 1 : numerator / denominator;
                if (t == 0 || (conic ? float.IsNaN(t) : t == 1)) { Line(end, false); return; }
                Line(curve.Evaluate(t), false);
                SvgStrokeJoin saved = _join; _join = SvgStrokeJoin.Round; Line(end, false); _join = saved;
                return;
            }
            if (!PreJoin(control, false, out Point normal, out Point unit)) { Line(end, false); return; }
            Fit(curve, SvgStrokeSide.Outer, _outer);
            Fit(curve, SvgStrokeSide.Inner, _inner);
            if (!Normal(control, end, out Point endNormal, out Point endUnit)) { endNormal = normal; endUnit = unit; }
            PostJoin(end, endNormal, endUnit);
        }

        internal void Cubic(Point first, Point second, Point end)
        {
            Curve curve = new(SvgMeasuredCurveKind.Cubic, Previous, first, second, end, 1);
            Point ab = first - Previous, bc = second - first, cd = end - second;
            bool degenerateAB = !CanNormalize(ab), degenerateBC = !CanNormalize(bc), degenerateCD = !CanNormalize(cd);
            int degenerate = (degenerateAB ? 1 : 0) + (degenerateBC ? 1 : 0) + (degenerateCD ? 1 : 0);
            if (degenerate >= 2) { Line(end, false); return; }
            _token.ThrowIfCancellationRequested();
            Span<Point> vertices = stackalloc Point[] { Previous, first, second, end };
            if (InLine(vertices, .00001f))
            {
                Span<float> roots = stackalloc float[3];
                Span<Point> reduction = stackalloc Point[3];
                int count = curve.CurvatureParameters(roots), retained = 0;
                for (int index = 0; index < count; index++)
                {
                    if (roots[index] <= 0 || roots[index] >= 1) continue;
                    Point point = curve.Evaluate(roots[index]);
                    if (point != Previous && point != end) reduction[retained++] = point;
                }
                if (retained == 0) { Line(end, false); return; }
                Line(reduction[0], false);
                SvgStrokeJoin saved = _join; _join = SvgStrokeJoin.Round;
                for (int index = 1; index < retained; index++) Line(reduction[index], false);
                Line(end, false); _join = saved;
                return;
            }
            if (!PreJoin(degenerateAB ? second : first, false, out Point normal, out Point unit)) { Line(end, false); return; }
            Fit(curve, SvgStrokeSide.Outer, _outer); Fit(curve, SvgStrokeSide.Inner, _inner);
            float cusp = curve.Cusp();
            if (cusp > 0) Circle(curve.Evaluate(cusp));
            Point endNormal = normal, endUnit = unit;
            if (!(degenerateAB && degenerateCD))
            {
                if (degenerateAB) ab = second - curve.Start;
                if (degenerateCD) cd = end - first;
                if (CanNormalize(ab) && CanNormalize(cd) && cd.SetLength(1, out Point direction))
                { endUnit = new(direction.Y, -direction.X); endNormal = endUnit * _radius; }
            }
            PostJoin(end, endNormal, endUnit);
        }

        private void Fit(Curve curve, SvgStrokeSide side, BudgetedParserList<SvgStrokeOutlineCommand> sink)
        {
            _fitted.Clear();
            if (curve.Kind == SvgMeasuredCurveKind.Conic)
                SvgCurveStroke.Append(curve.Start.Device(), curve.First.Device(), curve.End.Device(), curve.Weight, _radius, _scale, side, _fitted, _token);
            else SvgCurveStroke.AppendPolynomial(curve, _radius, _scale, side, _fitted, _token);
            foreach (SvgStrokeCurveSegment segment in _fitted.AsSpan())
                Add(sink, segment.Control.HasValue ? SvgStrokeOutlineVerb.Quadratic : SvgStrokeOutlineVerb.Line,
                    segment.Control is SvgDevicePoint control ? Point.Narrow(control) : default, Point.Narrow(segment.End), 1);
        }

        private bool PreJoin(Point point, bool line, out Point normal, out Point unit)
        {
            if (!Normal(Previous, point, out normal, out unit))
            {
                if (_cap == SvgStrokeCap.Butt) return false;
                normal = new(_radius, 0); unit = new(1, 0);
            }
            if (_segments == 0)
            {
                _firstNormal = normal; _firstUnit = unit; _firstOuter = Previous + normal;
                Add(_outer, SvgStrokeOutlineVerb.Move, default, _firstOuter, 1);
                Add(_inner, SvgStrokeOutlineVerb.Move, default, Previous - normal, 1);
            }
            else Join(_previousUnit, Previous, unit, _previousLine, line);
            _previousLine = line;
            return true;
        }

        private void PostJoin(Point point, Point normal, Point unit)
        { _joinCompleted = true; Previous = point; _previousNormal = normal; _previousUnit = unit; _segments++; }

        private bool Normal(Point before, Point after, out Point normal, out Point unit)
        {
            if (((after - before) * _scale).SetLength(1, out Point direction))
            { unit = new(direction.Y, -direction.X); normal = unit * _radius; return true; }
            normal = unit = default; return false;
        }

        internal void Close(bool closingLine)
        {
            if (closingLine) { Line(_first, false); LastLine = true; }
            if (_cap != SvgStrokeCap.Butt)
            {
                if (_segments == 0) { Line(_first, false); LastLine = true; return; }
                if (EmptyContour(_inner.AsSpan()) && EmptyContour(_outer.AsSpan()[_firstOuterIndex..])) { LastLine = true; return; }
            }
            Finish(true, LastLine);
        }

        internal void Finish(bool close, bool currentLine)
        {
            if (_segments > 0)
            {
                if (close)
                {
                    Join(_previousUnit, Previous, _firstUnit, _previousLine, currentLine);
                    Add(_outer, SvgStrokeOutlineVerb.Close, default, _outer[^1].End, 1);
                    Add(_outer, SvgStrokeOutlineVerb.Move, default, _inner[^1].End, 1);
                    ReverseInner();
                    Add(_outer, SvgStrokeOutlineVerb.Close, default, _outer[^1].End, 1);
                }
                else
                {
                    Cap(Previous, _previousNormal, _inner[^1].End, currentLine);
                    ReverseInner();
                    Cap(_first, new(-_firstNormal.X, -_firstNormal.Y), _firstOuter, _previousLine);
                    Add(_outer, SvgStrokeOutlineVerb.Close, default, _outer[^1].End, 1);
                }
                foreach (SvgStrokeOutlineCommand command in _cusps.AsSpan()) Add(_outer, command.Verb, command.Control, command.End, command.Weight);
            }
            _inner.Clear(); _cusps.Clear(); _segments = -1; _firstOuterIndex = _outer.Count;
        }

        private void ReverseInner()
        {
            for (int index = _inner.Count - 1; index > 0; index--)
            {
                SvgStrokeOutlineCommand command = _inner[index];
                Add(_outer, command.Verb, command.Control, _inner[index - 1].End, command.Weight);
            }
        }

        private void Cap(Point pivot, Point normal, Point stop, bool extend)
        {
            if (_cap == SvgStrokeCap.Butt) { Add(_outer, SvgStrokeOutlineVerb.Line, default, stop, 1); return; }
            Point parallel = new(-normal.Y, normal.X), projected = pivot + parallel;
            if (_cap == SvgStrokeCap.Round)
            {
                Add(_outer, SvgStrokeOutlineVerb.Conic, projected + normal, projected, SvgRationalQuadratic.QuarterCircleWeight);
                Add(_outer, SvgStrokeOutlineVerb.Conic, projected - normal, stop, SvgRationalQuadratic.QuarterCircleWeight);
            }
            else
            {
                Point first = pivot + normal + parallel;
                if (extend) SetLast(_outer, first); else Add(_outer, SvgStrokeOutlineVerb.Line, default, first, 1);
                Add(_outer, SvgStrokeOutlineVerb.Line, default, pivot - normal + parallel, 1);
                if (!extend) Add(_outer, SvgStrokeOutlineVerb.Line, default, stop, 1);
            }
        }

        private void Join(Point before, Point pivot, Point after, bool previousLine, bool currentLine)
        {
            _token.ThrowIfCancellationRequested();
            float dot = before.Dot(after);
            if (_join != SvgStrokeJoin.Bevel && MathF.Abs(1 - dot) <= 1f / 4096) return;
            BudgetedParserList<SvgStrokeOutlineCommand> outer = _outer, inner = _inner;
            bool counterClockwise = !(before.X * after.Y > before.Y * after.X);
            bool blunt = _join == SvgStrokeJoin.Bevel;
            if (_join == SvgStrokeJoin.Miter && MathF.Abs(1 + dot) <= 1f / 4096) { blunt = true; currentLine = false; }
            else if (counterClockwise)
            { (outer, inner) = (inner, outer); before = new(-before.X, -before.Y); after = new(-after.X, -after.Y); }
            if (_join == SvgStrokeJoin.Round)
            {
                Span<Point> controls = stackalloc Point[4], ends = stackalloc Point[4];
                Span<float> weights = stackalloc float[4];
                int count = Arc(before, after, counterClockwise, pivot, _radius, controls, ends, weights);
                if (count == 0) return;
                for (int index = 0; index < count; index++) Add(outer, SvgStrokeOutlineVerb.Conic, controls[index], ends[index], weights[index]);
            }
            else
            {
                if (!blunt)
                {
                    Point middle;
                    float sinHalf = MathF.Sqrt((1 + dot) / 2);
                    if (dot == 0 && _inverseMiter <= .707106781f) middle = (before + after) * _radius;
                    else if (sinHalf < _inverseMiter) { middle = default; blunt = true; currentLine = false; }
                    else
                    {
                        middle = dot < 0 ? new(after.Y - before.Y, before.X - after.X) : before + after;
                        if (dot < 0 && counterClockwise) middle = new(-middle.X, -middle.Y);
                        _ = middle.SetLength(_radius / sinHalf, out middle);
                    }
                    if (!blunt)
                    {
                        if (previousLine) SetLast(outer, pivot + middle); else Add(outer, SvgStrokeOutlineVerb.Line, default, pivot + middle, 1);
                    }
                }
                if (_join == SvgStrokeJoin.Bevel || !currentLine) Add(outer, SvgStrokeOutlineVerb.Line, default, pivot + after * _radius, 1);
            }
            Add(inner, SvgStrokeOutlineVerb.Line, default, pivot, 1);
            Add(inner, SvgStrokeOutlineVerb.Line, default, pivot - after * _radius, 1);

            static int Arc(Point from, Point to, bool reverse, Point pivot, float radius,
                Span<Point> controls, Span<Point> ends, Span<float> weights)
            {
                float x = from.Dot(to), y = from.Cross(to);
                if (MathF.Abs(y) <= 1f / 4096 && x > 0 && (reverse ? y <= 0 : y >= 0)) return 0;
                if (reverse) y = -y;
                int quadrant = y == 0 ? 2 : x == 0 ? y > 0 ? 1 : 3 : (y < 0 ? 2 : 0) + ((x < 0) != (y < 0) ? 1 : 0);
                ReadOnlySpan<Point> unit = stackalloc Point[] { new(1,0),new(1,1),new(0,1),new(-1,1),new(-1,0),new(-1,-1),new(0,-1),new(1,-1) };
                float scaleX = radius * from.X, scaleY = radius * from.Y;
                int count = quadrant;
                for (int index = 0; index < count; index++)
                {
                    controls[index] = Map(unit[2 * index + 1], pivot, scaleX, scaleY, reverse);
                    ends[index] = Map(unit[2 * index + 2], pivot, scaleX, scaleY, reverse);
                    weights[index] = SvgRationalQuadratic.QuarterCircleWeight;
                }
                Point last = unit[2 * quadrant];
                float dot = last.Dot(new(x, y));
                if (float.IsNaN(dot)) return 0;
                if (dot < 1)
                {
                    float weight = MathF.Sqrt((1 + dot) / 2);
                    Point control = last + new Point(x, y);
                    _ = control.SetLength(1 / weight, out control);
                    if (CanNormalize(control - last))
                    { controls[count] = Map(control, pivot, scaleX, scaleY, reverse); ends[count] = Map(new(x,y), pivot, scaleX, scaleY, reverse); weights[count++] = weight; }
                }
                return count;

                static Point Map(Point p, Point pivot, float x, float y, bool reverse)
                    => new(x * p.X + (reverse ? y : -y) * p.Y + pivot.X, y * p.X + (reverse ? -x : x) * p.Y + pivot.Y);
            }
        }

        private void Circle(Point pivot)
        {
            float left = pivot.X - _radius, right = pivot.X + _radius, top = pivot.Y - _radius, bottom = pivot.Y + _radius;
            float x = (float)(.5 * ((double)left + right)), y = (float)(.5 * ((double)top + bottom));
            Add(_cusps, SvgStrokeOutlineVerb.Move, default, new(right, y), 1);
            Add(_cusps, SvgStrokeOutlineVerb.Conic, new(right,bottom), new(x,bottom), SvgRationalQuadratic.QuarterCircleWeight);
            Add(_cusps, SvgStrokeOutlineVerb.Conic, new(left,bottom), new(left,y), SvgRationalQuadratic.QuarterCircleWeight);
            Add(_cusps, SvgStrokeOutlineVerb.Conic, new(left,top), new(x,top), SvgRationalQuadratic.QuarterCircleWeight);
            Add(_cusps, SvgStrokeOutlineVerb.Conic, new(right,top), new(right,y), SvgRationalQuadratic.QuarterCircleWeight);
            Add(_cusps, SvgStrokeOutlineVerb.Close, default, new(right,y), 1);
        }

        private void Add(BudgetedParserList<SvgStrokeOutlineCommand> sink, SvgStrokeOutlineVerb verb, Point control, Point end, float weight)
        {
            _token.ThrowIfCancellationRequested();
            if (sink.Count >= MaximumCommands) throw new SvgException(SvgErrorCode.LimitExceeded, "Native stroke outline exceeds the supported command range.");
            _ = end.Device(); _ = control.Device();
            if (!float.IsFinite(weight)) throw new SvgException(SvgErrorCode.LimitExceeded, "Native stroke weight exceeds the finite range.");
            sink.Add(new(verb, control, end, weight));
        }

        private void SetLast(BudgetedParserList<SvgStrokeOutlineCommand> sink, Point point)
        { _token.ThrowIfCancellationRequested(); _ = point.Device(); sink[^1] = sink[^1] with { End = point }; }

        private static bool CanNormalize(Point point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && (point.X != 0 || point.Y != 0);

        private static bool InLine(ReadOnlySpan<Point> points, float slop)
        {
            float maximum = -1; int first = 0, last = 1;
            for (int index = 0; index < points.Length - 1; index++)
                for (int other = index + 1; other < points.Length; other++)
                {
                    Point delta = points[other] - points[index];
                    float distance = MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y));
                    if (maximum < distance) { maximum = distance; first = index; last = other; }
                }
            float limit = maximum * maximum * slop;
            for (int index = 0; index < points.Length; index++)
                if (index != first && index != last && !(SvgCurveStroke.LineDistance(points[index], points[first], points[last]) <= limit)) return false;
            return true;
        }

        private bool EmptyContour(ReadOnlySpan<SvgStrokeOutlineCommand> commands)
        {
            if (commands.IsEmpty) return true;
            Point first = commands[0].End;
            foreach (SvgStrokeOutlineCommand command in commands)
            {
                _token.ThrowIfCancellationRequested();
                if (command.End != first || command.Verb is SvgStrokeOutlineVerb.Quadratic or SvgStrokeOutlineVerb.Conic && command.Control != first) return false;
            }
            return true;
        }
    }
}
