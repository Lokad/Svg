using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns bounded native distance records for one open or closed text baseline.</summary>
/// <remarks>The parser owns this temporary object and disposes it after placing a run. Commands are borrowed
/// only during construction. Each command, knot, lookup and evaluation charges work and checks cancellation.
/// Failed construction releases all arrays; readers and disposal are confined to one conversion thread.</remarks>
internal sealed class SvgMeasuredTextPath : IDisposable
{
    private readonly record struct Curve(SvgMeasuredCurveKind Kind, float Weight, float InitialDistance, float Distance,
        int FirstStep, int StepCount, SvgDevicePoint First, SvgDevicePoint Second, SvgDevicePoint Third, SvgDevicePoint Fourth);

    private OwnedParserArray<Curve> _curves;
    private OwnedParserArray<SvgCurveMeasureStep> _steps;
    private RenderBudget.BudgetReservation? _owner;

    private SvgMeasuredTextPath(SvgTextPathBaseline baseline, bool horizontal, OwnedParserArray<Curve> curves,
        OwnedParserArray<SvgCurveMeasureStep> steps, RenderBudget.BudgetReservation owner)
    {
        Baseline = baseline; IsHorizontal = horizontal; _curves = curves; _steps = steps; _owner = owner;
    }

    internal SvgTextPathBaseline Baseline { get; }
    internal bool IsHorizontal { get; }

    internal static SvgMeasuredTextPath Create(ReadOnlySpan<SvgAuthoredPathCommand> commands, SvgAffineTransform map,
        double? pathLength, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Geometry);
        OwnedParserArray<Curve> ownedCurves = new([], null);
        OwnedParserArray<SvgCurveMeasureStep> ownedSteps = new([], null);
        try
        {
            SvgDevicePoint straightStart = default, straightEnd = default;
            bool straightHasStart = false, straight = true;
            foreach (SvgAuthoredPathCommand command in commands)
            {
                work.Consume(token);
                if (command.Verb == SvgAuthoredPathVerb.End) continue;
                if (command.Verb == SvgAuthoredPathVerb.Move && !straightHasStart)
                {
                    straightStart = straightEnd = map.Apply(command.First.X, command.First.Y);
                    straightHasStart = true;
                    continue;
                }
                if (command.Verb != SvgAuthoredPathVerb.Line) { straight = false; break; }
                SvgDevicePoint next = map.Apply(command.First.X, command.First.Y);
                if (next.Y != straightEnd.Y || next.X < straightEnd.X) { straight = false; break; }
                straightEnd = next;
            }
            if (straight)
            {
                double straightLength = straightEnd.X - straightStart.X;
                double calibration = pathLength is double declared ? straightLength / declared : 1;
                if (!double.IsFinite(straightLength) || !double.IsFinite(calibration))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path length or calibration exceeds the supported range.");
                return new(new(straightStart.X, straightStart.Y, straightLength) { OffsetScale = calibration }, true, ownedCurves, ownedSteps, owner);
            }
            using BudgetedParserList<Curve> curves = new(budget, SvgMemoryCategory.Geometry, work, token);
            using BudgetedParserList<SvgCurveMeasureStep> steps = new(budget, SvgMemoryCategory.Geometry, work, token);
            SvgDevicePoint current = default, first = default, start = default, horizontalEnd = default;
            bool hasStart = false, horizontal = true;
            float distance = 0;
            Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
            Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
            foreach (SvgAuthoredPathCommand command in commands)
            {
                work.Consume(token);
                if (command.Verb == SvgAuthoredPathVerb.End) continue;
                if (command.Verb == SvgAuthoredPathVerb.Move && !hasStart)
                {
                    current = first = command.First;
                    start = horizontalEnd = map.Apply(current.X, current.Y);
                    hasStart = true;
                    continue;
                }
                if (!hasStart || command.Verb == SvgAuthoredPathVerb.Move)
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only one text-path contour is implemented.");
                points[0] = current;
                if (command.Verb == SvgAuthoredPathVerb.Close)
                {
                    points[1] = first;
                    AddCurve(SvgMeasuredCurveKind.Line, points[..2], 1);
                    current = first;
                    horizontal = false;
                    continue;
                }
                if (command.Verb == SvgAuthoredPathVerb.Arc)
                {
                    horizontal = false;
                    for (int phase = 0; phase <= SvgEndpointArc.MaximumConicCount; phase++) work.Consume(token);
                    int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                        (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                        command.Third, arcs, token);
                    if (count == 0) { points[1] = command.Third; AddCurve(SvgMeasuredCurveKind.Line, points[..2], 1); }
                    else for (int part = 0; part < count; part++)
                    {
                        points[1] = arcs[part].Control; points[2] = arcs[part].End;
                        AddCurve(SvgMeasuredCurveKind.Conic, points[..3], arcs[part].Weight);
                        points[0] = points[2];
                    }
                    current = command.Third;
                    continue;
                }
                SvgMeasuredCurveKind kind = command.Verb switch
                {
                    SvgAuthoredPathVerb.Line => SvgMeasuredCurveKind.Line,
                    SvgAuthoredPathVerb.Quadratic => SvgMeasuredCurveKind.Quadratic,
                    SvgAuthoredPathVerb.Cubic => SvgMeasuredCurveKind.Cubic,
                    SvgAuthoredPathVerb.Conic => SvgMeasuredCurveKind.Conic,
                    _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "The text-path curve verb is not implemented.")
                };
                int pointCount = kind == SvgMeasuredCurveKind.Line ? 2 : kind == SvgMeasuredCurveKind.Cubic ? 4 : 3;
                points[1] = command.First;
                if (pointCount >= 3) points[2] = command.Second;
                if (pointCount == 4) points[3] = command.Third;
                SvgDevicePoint next = map.Apply(points[pointCount - 1].X, points[pointCount - 1].Y);
                horizontal &= kind == SvgMeasuredCurveKind.Line && next.Y == horizontalEnd.Y && next.X >= horizontalEnd.X;
                horizontalEnd = next;
                AddCurve(kind, points[..pointCount], kind == SvgMeasuredCurveKind.Conic ? (float)command.Third.X : 1);
                current = points[pointCount - 1];
            }
            double length = horizontal ? horizontalEnd.X - start.X : distance;
            double offsetScale = pathLength is double calibrated ? length / calibrated : 1;
            if (!double.IsFinite(length) || !double.IsFinite(offsetScale))
                throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path length or calibration exceeds the supported range.");
            // Straight positive baselines retain their established double-coordinate placement and strike route.
            if (!horizontal) { ownedCurves = curves.Detach(); ownedSteps = steps.Detach(); }
            return new(new(start.X, start.Y, length) { OffsetScale = offsetScale }, horizontal, ownedCurves, ownedSteps, owner);

            void AddCurve(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> source, float weight)
            {
                Span<SvgDevicePoint> mapped = stackalloc SvgDevicePoint[4];
                for (int index = 0; index < source.Length; index++)
                {
                    work.Consume(token);
                    mapped[index] = SvgAuthoredPath.MapPoint(map, source[index], default);
                }
                Span<SvgCurveMeasureStep> measured = stackalloc SvgCurveMeasureStep[SvgCurveMeasure.MaximumStepCount];
                float initial = distance;
                int count = SvgCurveMeasure.Measure(kind, mapped[..source.Length], weight, 1, initial, measured, out distance, work, token);
                if (!float.IsFinite(distance)) throw new SvgException(SvgErrorCode.LimitExceeded, "Text-path curve distances exceed the supported range.");
                if (count == 0) return;
                curves.Add(new(kind, weight, initial, distance, steps.Count, count, mapped[0], mapped[1], mapped[2], mapped[3]));
                steps.AddRange(measured[..count]);
            }
        }
        catch { ownedCurves.Dispose(); ownedSteps.Dispose(); owner.Dispose(); throw; }
    }

    /// <summary>Returns a glyph-local map about its advance midpoint and the measured tangent.</summary>
    internal SvgAffineTransform GlyphMap(double midpoint, double halfAdvance, double baselineShift,
        ParserWorkCounter work, CancellationToken token)
        => GlyphMap(midpoint, halfAdvance, baselineShift, 0, work, token);

    /// <summary>Combines a normalized character rotation with the path tangent before midpoint placement.</summary>
    internal SvgAffineTransform GlyphMap(double midpoint, double halfAdvance, double baselineShift, double rotation,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (_owner is null) throw new InvalidOperationException("The measured text path is disposed.");
        if (!double.IsFinite(midpoint) || midpoint < 0 || midpoint > Baseline.Length)
            throw new ArgumentOutOfRangeException(nameof(midpoint));
        if (IsHorizontal)
            return rotation == 0 ? SvgAffineTransform.FromTranslation(Baseline.X + midpoint - halfAdvance, Baseline.Y + baselineShift)
                : SvgAffineTransform.FromTranslation(Baseline.X + midpoint, Baseline.Y)
                    .ComposeChecked(SvgAffineTransform.FromRotationDegrees(rotation))
                    .ComposeChecked(SvgAffineTransform.FromTranslation(-halfAdvance, baselineShift));
        ReadOnlySpan<Curve> curves = _curves.Items;
        int low = 0, high = curves.Length - 1;
        float distance = (float)midpoint;
        while (low < high)
        {
            work.Consume(token);
            int middle = (low + high) >> 1;
            if (curves[middle].Distance < distance) low = middle + 1; else high = middle;
        }
        Curve curve = curves[low];
        Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
        points[0] = curve.First; points[1] = curve.Second; points[2] = curve.Third; points[3] = curve.Fourth;
        float parameter = SvgCurveMeasure.ParameterAt(_steps.Items.AsSpan(curve.FirstStep, curve.StepCount), curve.InitialDistance, distance, work, token);
        SvgDevicePoint position = SvgCurveMeasure.Evaluate(curve.Kind, points, curve.Weight, parameter);
        SvgDevicePoint tangent = SvgCurveMeasure.Tangent(curve.Kind, points, curve.Weight, parameter, work, token);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(tangent.X) || !double.IsFinite(tangent.Y))
            throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path position or tangent exceeds the supported range.");
        double angle = Math.Atan2(tangent.Y, tangent.X) * (180 / Math.PI);
        if (rotation != 0) angle = (float)angle + (float)rotation;
        return SvgAffineTransform.FromTranslation(position.X, position.Y)
            .ComposeChecked(SvgAffineTransform.FromRotationDegrees(angle))
            .ComposeChecked(SvgAffineTransform.FromTranslation(-halfAdvance, baselineShift));
    }

    public void Dispose()
    {
        _curves.Dispose(); _steps.Dispose(); _owner?.Dispose(); _owner = null;
        _curves = new([], null); _steps = new([], null);
    }
}
