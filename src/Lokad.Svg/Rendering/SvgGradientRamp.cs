// Adapts native gradient preparation and interpolation. Copyright 2016 Google Inc.;
// 2018, 2022 Google LLC. See THIRD_PARTY_NOTICES.txt for the pinned BSD source.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Prepares immutable native coefficients from canonical stops for linear and radial replay.</summary>
/// <remarks>Inline pairs own no storage; other ramps reserve before allocating at most 259 segments. Preparation charges work
/// and observes cancellation. Sampling allocates nothing; callers charge pixels and check rows. Copies borrow the same owner
/// and must finish before the preparing scope disposes it. No source storage or retained scene metadata is added.</remarks>
internal readonly struct SvgGradientRamp : IDisposable
{
    private readonly SvgGradientRampSegment _pair;
    private readonly OwnedSceneArray<SvgGradientRampSegment> _segments;
    private readonly int _segmentCount, _uniformGapCount;

    private SvgGradientRamp(SvgGradientRampSegment pair, OwnedSceneArray<SvgGradientRampSegment> segments, int segmentCount, int uniformGapCount)
    {
        _pair = pair; _segments = segments;
        _segmentCount = segmentCount; _uniformGapCount = uniformGapCount;
    }

    internal static SvgGradientRamp Create(ReadOnlySpan<SvgGradientStop> stops, SvgGradientSpread spread,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (stops.Length > 256) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient ramp preparation exceeds the supported stop limit.");
        if (stops.Length == 0) return new(Pair(default, default, 0, 0), new([], null), 0, 0);
        if (stops.Length == 1) return new(Pair(stops[0].Color, stops[0].Color, stops[0].Alpha, stops[0].Alpha), new([], null), 0, 0);
        if (stops.Length > 2 && SamePaint(stops[0], stops[^1]))
        {
            bool same = true;
            for (int index = 1; index < stops.Length; index++)
            {
                work.Consume(token);
                if (!SamePaint(stops[index], stops[0])) { same = false; break; }
            }
            if (same) return new(Pair(stops[0].Color, stops[0].Color, stops[0].Alpha, stops[0].Alpha), new([], null), 0, 0);
        }
        if (stops.Length == 2 && (float)stops[0].Offset == 0 && (float)stops[1].Offset == 1)
            return new(Pair(stops[0].Color, stops[1].Color, stops[0].Alpha, stops[1].Alpha), new([], null), 0, 0);

        SvgGradientStops canonical = SvgGradientStops.Create(stops, spread, work, token);
        int capacity = canonical.Uniform ? canonical.Count : canonical.Count + 1;
        RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGradientRampSegment>(capacity), SvgMemoryCategory.Temporary);
        try
        {
            SvgGradientRampSegment[] segments = new SvgGradientRampSegment[capacity];
            SvgGradientStops.Enumerator iterator = canonical.GetEnumerator(work, token);
            if (!iterator.MoveNext()) throw new InvalidOperationException("Canonical gradient is missing its first color.");
            SvgGradientStop previous = iterator.Current;
            int used = 0;
            if (canonical.Uniform)
            {
                int gaps = canonical.Count - 1;
                while (iterator.MoveNext())
                {
                    SvgGradientStop current = iterator.Current;
                    segments[used] = Uniform(previous, current, used, gaps);
                    used++; previous = current;
                }
                segments[used++] = Padding(1, previous);
                return new(default, new(segments, owner), used, gaps);
            }

            if (!iterator.MoveNext()) throw new InvalidOperationException("Canonical gradient is missing its second color.");
            SvgGradientStop next = iterator.Current;
            int nextIndex = 1;
            if (canonical.Count > 2 && SamePaint(previous, next))
            {
                previous = next;
                if (!iterator.MoveNext()) throw new InvalidOperationException("Canonical gradient is missing an interior color.");
                next = iterator.Current; nextIndex = 2;
            }
            segments[used++] = Padding(float.NegativeInfinity, previous);
            while (true)
            {
                if (nextIndex == canonical.Count - 1 && canonical.Count > 2 && SamePaint(previous, next)) break;
                float gap = (float)next.Offset - (float)previous.Offset;
                float reciprocal = 1f / gap;
                if (gap > 0 && float.IsFinite(reciprocal))
                    segments[used++] = Arbitrary(previous, next);
                previous = next;
                if (!iterator.MoveNext()) break;
                next = iterator.Current; nextIndex++;
            }
            segments[used++] = Padding((float)previous.Offset, previous);
            return new(default, new(segments, owner), used, 0);
        }
        catch { owner.Dispose(); throw; }

        static SvgGradientRampSegment Pair(SvgColor start, SvgColor end, float firstAlpha, float secondAlpha)
        {
            SvgShaderPaint first = Normalize(start, firstAlpha), second = Normalize(end, secondAlpha);
            return new(0, new(second.Red - first.Red, second.Green - first.Green, second.Blue - first.Blue, second.Alpha - first.Alpha), first, first == second);
        }

        static SvgGradientRampSegment Padding(float position, SvgGradientStop stop)
            => new(position, default, Normalize(stop.Color, stop.Alpha), false);

        static SvgGradientRampSegment Uniform(SvgGradientStop left, SvgGradientStop right, int index, int gapCount)
        {
            SvgShaderPaint a = Normalize(left.Color, left.Alpha), b = Normalize(right.Color, right.Alpha);
            SvgShaderPaint factor = new((b.Red - a.Red) * gapCount, (b.Green - a.Green) * gapCount, (b.Blue - a.Blue) * gapCount, (b.Alpha - a.Alpha) * gapCount);
            float position = (float)index / gapCount;
            return new(position, factor, new(a.Red - factor.Red * position, a.Green - factor.Green * position,
                a.Blue - factor.Blue * position, a.Alpha - factor.Alpha * position), false);
        }

        static SvgGradientRampSegment Arbitrary(SvgGradientStop left, SvgGradientStop right)
        {
            float position = (float)left.Offset, reciprocal = 1f / ((float)right.Offset - position);
            SvgShaderPaint a = Normalize(left.Color, left.Alpha), b = Normalize(right.Color, right.Alpha);
            SvgShaderPaint factor = new((b.Red - a.Red) * reciprocal, (b.Green - a.Green) * reciprocal, (b.Blue - a.Blue) * reciprocal, (b.Alpha - a.Alpha) * reciprocal);
            return new(position, factor, new(a.Red - factor.Red * position, a.Green - factor.Green * position,
                a.Blue - factor.Blue * position, a.Alpha - factor.Alpha * position), false);
        }

        static SvgShaderPaint Normalize(SvgColor color, float alpha)
            => new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha);

        static bool SamePaint(SvgGradientStop first, SvgGradientStop second)
            => first.Color.Red == second.Color.Red && first.Color.Green == second.Color.Green
                && first.Color.Blue == second.Color.Blue && first.Alpha == second.Alpha;
    }

    /// <summary>Preserves pad coordinates for arbitrary terminal intervals; periodic spreading retains native float order.</summary>
    internal static double Spread(double position, SvgGradientSpread spread)
    {
        if (double.IsPositiveInfinity(position)) return spread == SvgGradientSpread.Pad ? position : 1;
        float coordinate = (float)position;
        if (spread == SvgGradientSpread.Repeat) return Clamp(coordinate - MathF.Floor(coordinate));
        if (spread == SvgGradientSpread.Reflect)
            return Clamp(MathF.Abs((coordinate - 1f) - 2f * MathF.Floor((coordinate - 1f) * .5f) - 1f));
        return position;

        static float Clamp(float value) => float.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1);
    }

    internal SvgShaderPaint SampleAbsolute(double position, float opacity, int matrix)
    {
        if (_segmentCount == 0) return Sample(_pair, (float)Math.Clamp(position, 0, 1), opacity, matrix);
        float coordinate = (float)position;
        SvgGradientRampSegment[] segments = _segments.Items;
        if (_uniformGapCount > 0)
        {
            coordinate = Math.Clamp(coordinate, 0, 1);
            float index = coordinate * _uniformGapCount;
            int segment = float.IsNaN(index) ? 0 : Math.Min((int)index, _segmentCount - 1);
            return Sample(segments[segment], coordinate, opacity, matrix);
        }
        int first = 0, last = _segmentCount - 1;
        while (first < last)
        {
            int middle = (first + last + 1) / 2;
            if (coordinate >= segments[middle].Position) first = middle;
            else last = middle - 1;
        }
        return Sample(segments[first], coordinate, opacity, matrix);

        static SvgShaderPaint Sample(SvgGradientRampSegment paint, float position, float opacity, int matrix)
        {
            if (paint.Constant) position = 0;
            float alpha = MathF.FusedMultiplyAdd(position, paint.Factor.Alpha, paint.Bias.Alpha);
            return new SvgShaderPaint(MathF.FusedMultiplyAdd(position, paint.Factor.Red, paint.Bias.Red) * alpha * opacity,
                MathF.FusedMultiplyAdd(position, paint.Factor.Green, paint.Bias.Green) * alpha * opacity,
                MathF.FusedMultiplyAdd(position, paint.Factor.Blue, paint.Bias.Blue) * alpha * opacity, alpha * opacity).Dither(matrix);
        }
    }

    public void Dispose() => _segments.Dispose();

    private readonly record struct SvgGradientRampSegment(float Position, SvgShaderPaint Factor, SvgShaderPaint Bias, bool Constant);
}
