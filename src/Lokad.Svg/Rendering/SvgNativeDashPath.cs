// Adapts SkContourMeasure.cpp and src/utils/SkDashPath.cpp at the pinned revision.
// Copyright 2014 Google Inc.; 2018 Google LLC.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Filters retained float curves into native dash verbs before stroking.</summary>
/// <remarks>Owns temporary curve, knot, contour and interval lists; reserves their storage before allocation.
/// The returned array transfers to one immutable authored-path owner. Every command, recursive measurement,
/// dash interval, search, cut and copied verb checks cancellation. Per-curve scratch is bounded
/// stack storage. Denial releases all private output and temporary owners. Calls share no mutable state.</remarks>
internal static class SvgNativeDashPath
{
    private readonly record struct Curve(SvgMeasuredCurveKind Kind, float Weight, float InitialDistance, float Distance,
        int FirstStep, int StepCount, SvgDevicePoint First, SvgDevicePoint Second, SvgDevicePoint Third, SvgDevicePoint Fourth);
    private readonly record struct Contour(int FirstCurve, int CurveCount, float Distance, bool Closed);

    internal static OwnedParserArray<SvgAuthoredPathCommand> Filter(ReadOnlySpan<SvgAuthoredPathCommand> commands,
        ReadOnlySpan<double> intervals, double offset, double? pathLength, SvgAffineTransform pointMap, float resolutionScale,
        RenderBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!(resolutionScale > 0) || !float.IsFinite(resolutionScale))
            throw new SvgException(SvgErrorCode.LimitExceeded, "Dash measurement scale exceeds the admitted numeric range.");
        // Covers five list owners and their simultaneous old/new reservation objects.
        using RenderBudget.BudgetReservation owners = budget.Reserve(1024, SvgMemoryCategory.Geometry);
        using BudgetedParserList<Curve> curves = new(budget, SvgMemoryCategory.Geometry, token);
        using BudgetedParserList<SvgCurveMeasureStep> steps = new(budget, SvgMemoryCategory.Geometry, token);
        using BudgetedParserList<Contour> contours = new(budget, SvgMemoryCategory.Geometry, token);
        using BudgetedParserList<float> pattern = new(budget, SvgMemoryCategory.Geometry, token);
        using BudgetedParserList<SvgAuthoredPathCommand> output = new(budget, SvgMemoryCategory.Scene, token);
        SvgDevicePoint current = default, first = default, filteredLast = default;
        float distance = 0;
        int firstCurve = 0;
        bool active = false;
        bool singleLine = commands.Length == 3 && commands[0].Verb == SvgAuthoredPathVerb.Move
            && commands[1].Verb == SvgAuthoredPathVerb.Line && commands[2].Verb == SvgAuthoredPathVerb.End;
        Span<SvgDevicePoint> points = stackalloc SvgDevicePoint[4];
        Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        foreach (SvgAuthoredPathCommand command in commands)
        {
            token.ThrowIfCancellationRequested();
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move:
                    if (active) FinishContour(false);
                    current = first = Narrow(command.First); active = true;
                    break;
                case SvgAuthoredPathVerb.Line:
                case SvgAuthoredPathVerb.Quadratic:
                case SvgAuthoredPathVerb.Cubic:
                case SvgAuthoredPathVerb.Conic:
                    SvgMeasuredCurveKind kind = command.Verb switch
                    {
                        SvgAuthoredPathVerb.Line => SvgMeasuredCurveKind.Line,
                        SvgAuthoredPathVerb.Quadratic => SvgMeasuredCurveKind.Quadratic,
                        SvgAuthoredPathVerb.Conic => SvgMeasuredCurveKind.Conic,
                        _ => SvgMeasuredCurveKind.Cubic
                    };
                    int count = kind == SvgMeasuredCurveKind.Line ? 2 : kind == SvgMeasuredCurveKind.Cubic ? 4 : 3;
                    points[0] = current; points[1] = Narrow(command.First);
                    if (count >= 3) points[2] = Narrow(command.Second);
                    if (count == 4) points[3] = Narrow(command.Third);
                    AddCurve(kind, points[..count], kind == SvgMeasuredCurveKind.Conic ? (float)command.Third.X : 1);
                    current = points[count - 1];
                    break;
                case SvgAuthoredPathVerb.Arc:
                    token.ThrowIfCancellationRequested();
                    int arcCount = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                        (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0,
                        command.Third, arcs, token);
                    points[0] = current;
                    if (arcCount == 0) { points[1] = Narrow(command.Third); AddCurve(SvgMeasuredCurveKind.Line, points[..2], 1); }
                    else for (int part = 0; part < arcCount; part++)
                    {
                        points[1] = arcs[part].Control; points[2] = arcs[part].End;
                        AddCurve(SvgMeasuredCurveKind.Conic, points[..3], arcs[part].Weight);
                        points[0] = points[2];
                    }
                    current = Narrow(command.Third);
                    break;
                case SvgAuthoredPathVerb.Close:
                    points[0] = current; points[1] = first;
                    AddCurve(SvgMeasuredCurveKind.Line, points[..2], 1);
                    current = first; FinishContour(true); active = false;
                    break;
                case SvgAuthoredPathVerb.End:
                    FinishContour(false); active = false;
                    break;
            }
        }
        if (active) FinishContour(false);
        double totalLength = 0;
        foreach (Contour contour in contours.AsSpan()) { token.ThrowIfCancellationRequested(); totalLength += contour.Distance; }
        double factor = pathLength is double declared && totalLength > 0 ? totalLength / declared : 1;
        float period = 0;
        foreach (double interval in intervals)
        {
            token.ThrowIfCancellationRequested();
            float value = (float)(interval * factor);
            if (!float.IsFinite(value) || value < 0) throw new SvgException(SvgErrorCode.LimitExceeded, "Dash intervals exceed the admitted numeric range.");
            pattern.Add(value); period += value;
        }
        float phaseOffset = (float)(offset * factor);
        if (pattern.Count < 2 || (pattern.Count & 1) != 0 || !(period > 0) || !float.IsFinite(period) || !float.IsFinite(phaseOffset))
            throw new SvgException(SvgErrorCode.LimitExceeded, "Dash phase exceeds the admitted numeric range.");
        if (phaseOffset < 0)
        {
            phaseOffset = -phaseOffset;
            if (phaseOffset > period) phaseOffset %= period;
            phaseOffset = period - phaseOffset;
            if (phaseOffset == period) phaseOffset = 0;
        }
        else if (phaseOffset >= period) phaseOffset %= period;
        int initialIndex = 0;
        float initialLength = pattern[0];
        for (int at = 0; at < pattern.Count; at++)
        {
            token.ThrowIfCancellationRequested();
            float gap = pattern[at];
            if (phaseOffset > gap || (phaseOffset == gap && gap != 0)) phaseOffset -= gap;
            else { initialIndex = at; initialLength = gap - phaseOffset; break; }
        }
        double dashCount = 0;
        foreach (Contour contour in contours.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            dashCount += contour.Distance * (pattern.Count / 2d) / period;
            if (dashCount > 1_000_000) throw new SvgException(SvgErrorCode.LimitExceeded, "Dash construction exceeds the bounded segment count.");
            int index = initialIndex, stagnant = 0;
            double at = 0, length = initialLength;
            bool skipFirst = contour.Closed, added = false;
            while (at < contour.Distance)
            {
                token.ThrowIfCancellationRequested();
                added = false;
                if ((index & 1) == 0 && !skipFirst)
                {
                    added = true;
                    EmitInterval(contour, (float)at, (float)(at + length), true);
                }
                double next = at + length;
                stagnant = next > at ? 0 : stagnant + 1;
                if (stagnant >= pattern.Count) throw new SvgException(SvgErrorCode.LimitExceeded, "Dash intervals cannot advance the contour distance.");
                at = next; skipFirst = false;
                index = index + 1 == pattern.Count ? 0 : index + 1;
                length = pattern[index];
            }
            if (contour.Closed && (initialIndex & 1) == 0)
            {
                if (added && output.Count != 0 && output[^1].Verb == SvgAuthoredPathVerb.End) output.RemoveLast();
                EmitInterval(contour, 0, initialLength, !added);
            }
        }
        return output.Detach();

        void FinishContour(bool closed)
        {
            token.ThrowIfCancellationRequested();
            if (!float.IsFinite(distance)) throw new SvgException(SvgErrorCode.LimitExceeded, "Curve distances exceed the admitted numeric range.");
            if (curves.Count != firstCurve) contours.Add(new(firstCurve, curves.Count - firstCurve, distance, closed));
            firstCurve = curves.Count; distance = 0;
        }

        void AddCurve(SvgMeasuredCurveKind kind, ReadOnlySpan<SvgDevicePoint> source, float weight)
        {
            Span<SvgDevicePoint> mapped = stackalloc SvgDevicePoint[4];
            for (int at = 0; at < source.Length; at++)
            {
                token.ThrowIfCancellationRequested();
                float x = (float)source[at].X, y = (float)source[at].Y;
                mapped[at] = new(((float)pointMap.A * x + (float)pointMap.C * y) + (float)pointMap.E,
                    ((float)pointMap.B * x + (float)pointMap.D * y) + (float)pointMap.F);
                if (!float.IsFinite((float)mapped[at].X) || !float.IsFinite((float)mapped[at].Y))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Dash coordinates exceed the admitted numeric range.");
            }
            // The native filter gives a standalone zero line enough length to carry its cap.
            if (singleLine && mapped[0] == mapped[1])
                mapped[1] = new((float)mapped[1].X + MathF.Max(1.001f, (float)mapped[1].X) * (1f / (1 << 12)), mapped[1].Y);
            Span<SvgCurveMeasureStep> measured = stackalloc SvgCurveMeasureStep[SvgCurveMeasure.MaximumStepCount];
            float initial = distance;
            int count = SvgCurveMeasure.Measure(kind, mapped[..source.Length], weight, resolutionScale, initial, measured, out distance, token);
            if (count == 0) return;
            curves.Add(new(kind, weight, initial, distance, steps.Count, count, mapped[0], mapped[1], mapped[2], mapped[3]));
            steps.AddRange(measured[..count]);
        }

        void EmitInterval(Contour contour, float start, float stop, bool move)
        {
            token.ThrowIfCancellationRequested();
            start = MathF.Max(0, start); stop = MathF.Min(contour.Distance, stop);
            if (!(start <= stop)) return;
            int firstIndex = FindCurve(contour, start), lastIndex = FindCurve(contour, stop);
            Span<SvgDevicePoint> source = stackalloc SvgDevicePoint[4], cut = stackalloc SvgDevicePoint[4];
            for (int at = firstIndex; at <= lastIndex; at++)
            {
                token.ThrowIfCancellationRequested();
                Curve curve = curves[at];
                source[0] = curve.First; source[1] = curve.Second; source[2] = curve.Third; source[3] = curve.Fourth;
                int pointCount = curve.Kind == SvgMeasuredCurveKind.Line ? 2 : curve.Kind == SvgMeasuredCurveKind.Cubic ? 4 : 3;
                ReadOnlySpan<SvgCurveMeasureStep> knots = steps.AsSpan().Slice(curve.FirstStep, curve.StepCount);
                float minimum = at == firstIndex ? SvgCurveMeasure.ParameterAt(knots, curve.InitialDistance, start, token) : 0;
                float maximum = at == lastIndex ? SvgCurveMeasure.ParameterAt(knots, curve.InitialDistance, stop, token) : 1;
                bool valid = SvgCurveMeasure.Cut(curve.Kind, source[..pointCount], curve.Weight, minimum, maximum, cut,
                    out SvgMeasuredCurveKind resultKind, out float resultWeight, token);
                if (move && at == firstIndex)
                {
                    output.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, cut[0], default, default));
                    filteredLast = cut[0];
                }
                if (!valid) continue;
                SvgAuthoredPathCommand command;
                switch (resultKind)
                {
                    case SvgMeasuredCurveKind.Line:
                        SvgDevicePoint end = minimum == maximum ? filteredLast : cut[1];
                        command = new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, end, default, default);
                        filteredLast = end; break;
                    case SvgMeasuredCurveKind.Quadratic:
                        command = new(SvgAuthoredPathVerb.Quadratic, SvgAuthoredArcFlags.None, cut[1], cut[2], default);
                        filteredLast = cut[2]; break;
                    case SvgMeasuredCurveKind.Conic:
                        command = new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None, cut[1], cut[2], new(resultWeight, 0));
                        filteredLast = cut[2]; break;
                    default:
                        command = new(SvgAuthoredPathVerb.Cubic, SvgAuthoredArcFlags.None, cut[1], cut[2], cut[3]);
                        filteredLast = cut[3]; break;
                }
                output.Add(command);
            }
            output.Add(new(SvgAuthoredPathVerb.End, SvgAuthoredArcFlags.None, default, default, default));
        }

        int FindCurve(Contour contour, float value)
        {
            int low = contour.FirstCurve, high = low + contour.CurveCount - 1;
            while (low < high)
            {
                token.ThrowIfCancellationRequested();
                int middle = (low + high) >> 1;
                if (curves[middle].Distance < value) low = middle + 1; else high = middle;
            }
            return low;
        }

        static SvgDevicePoint Narrow(SvgDevicePoint point) => new((float)point.X, (float)point.Y);
    }
}
