// Retains pinned Skia's native arbitrary-stop factor/bias and premultiplied shader stages.
// Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
// Legacy sRGB normalization adapts Chromium. Copyright 2012 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the BSD license and pinned source revision.
namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Prepares one immutable stop ramp for linear or radial replay, retaining native float factors and biases.</summary>
/// <remarks>Two-stop coefficients are inline; arbitrary ramps own at most 255 segments. Preparation reserves before allocation,
/// consumes bounded work and observes cancellation. Sampling is allocation-free; the caller charges pixels and checks rows.
/// Copies borrow the same array owner and must not outlive disposal of the preparing scope. No source stop storage is retained.</remarks>
internal readonly struct SvgGradientRamp : IDisposable
{
    private readonly SvgGradientRampSegment _pair;
    private readonly OwnedSceneArray<SvgGradientRampSegment> _segments;
    private readonly double _offset1, _offset2;
    private readonly bool _multiple;

    private SvgGradientRamp(SvgGradientRampSegment pair, OwnedSceneArray<SvgGradientRampSegment> segments, double offset1, double offset2, bool multiple)
    {
        _pair = pair;
        _segments = segments;
        _offset1 = offset1;
        _offset2 = offset2;
        _multiple = multiple;
    }

    internal static SvgGradientRamp Create(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end, double offset1, double offset2,
        RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (stops.Length > 256) throw new SvgException(SvgErrorCode.LimitExceeded, "Gradient ramp preparation exceeds the supported stop limit.");
        if (stops.Length > 2 && SamePaint(stops[0], stops[^1]))
        {
            SvgGradientStop first = stops[0];
            bool uniform = true;
            for (int index = 1; index < stops.Length; index++)
            {
                work.Consume(token);
                SvgGradientStop next = stops[index];
                if (!SamePaint(next, first)) { uniform = false; break; }
            }
            if (uniform) return new(SvgGradientRampSegment.Pair(first.Color, first.Color, first.Alpha, first.Alpha), new([], null), offset1, offset2, false);
        }
        if (stops.Length <= 2)
        {
            float firstAlpha = stops.Length == 0 ? start.Alpha * (1f / 255f) : stops[0].Alpha;
            float secondAlpha = stops.Length == 0 ? end.Alpha * (1f / 255f) : stops[^1].Alpha;
            return new(SvgGradientRampSegment.Pair(start, end, firstAlpha, secondAlpha), new([], null), offset1, offset2, false);
        }
        int count = stops.Length - 1;
        RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGradientRampSegment>(count), SvgMemoryCategory.Temporary);
        try
        {
            SvgGradientRampSegment[] segments = new SvgGradientRampSegment[count];
            for (int index = 0; index < segments.Length; index++)
            {
                work.Consume(token);
                segments[index] = SvgGradientRampSegment.Arbitrary(stops[index], stops[index + 1]);
            }
            return new(default, new(segments, owner), offset1, offset2, true);
        }
        catch { owner.Dispose(); throw; }

        static bool SamePaint(SvgGradientStop first, SvgGradientStop second)
            => first.Color.Red == second.Color.Red && first.Color.Green == second.Color.Green
                && first.Color.Blue == second.Color.Blue && first.Alpha == second.Alpha;
    }

    /// <summary>Spreads one gradient position, retaining native cone-boundary infinities until ramp evaluation.</summary>
    internal static double Spread(double position, SvgGradientSpread spread)
    {
        if (double.IsPositiveInfinity(position)) return spread == SvgGradientSpread.Pad ? position : 1;
        float coordinate = (float)position;
        if (spread == SvgGradientSpread.Repeat) return Clamp(coordinate - MathF.Floor(coordinate));
        if (spread == SvgGradientSpread.Reflect)
            return Clamp(MathF.Abs((coordinate - 1f) - 2f * MathF.Floor((coordinate - 1f) * .5f) - 1f));
        return Math.Clamp(position, 0, 1);

        static float Clamp(float value) => float.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1);
    }

    /// <summary>Preserves absolute stop padding before selecting an arbitrary segment or normalizing a two-stop pair.</summary>
    internal SvgShaderPaint SampleAbsolute(double position, float opacity, int matrix)
    {
        if (_multiple && double.IsPositiveInfinity(position))
        {
            // Native terminal padding has zero factors; retain 0*infinity through coverage and store.
            float terminal = 0f * (float)position;
            return new(terminal, terminal, terminal, terminal);
        }
        double absolute = Math.Clamp(position, _offset1, _offset2);
        double coordinate = _multiple ? 0d + absolute * 1d : _offset2 > _offset1 ? (absolute - _offset1) / (_offset2 - _offset1) : 0;
        float samplePosition = (float)coordinate;
        if (!_multiple) return _pair.Sample(samplePosition, opacity, matrix);
        SvgGradientRampSegment[] segments = _segments.Items;
        int segment = 0, last = segments.Length - 1;
        while (segment < last)
        {
            int middle = (segment + last + 1) / 2;
            if (samplePosition >= segments[middle].Position) segment = middle;
            else last = middle - 1;
        }
        return segments[segment].Sample(samplePosition, opacity, matrix);
    }

    public void Dispose() => _segments.Dispose();
}

/// <summary>Retains one straight-RGBA factor/bias interval, premultiplying and dithering only after interpolation.</summary>
internal readonly record struct SvgGradientRampSegment(float Position, SvgShaderPaint Factor, SvgShaderPaint Bias, bool Constant)
{
    internal static SvgGradientRampSegment Pair(SvgColor start, SvgColor end, float firstAlpha, float secondAlpha)
    {
        SvgShaderPaint first = Normalize(start, firstAlpha), second = Normalize(end, secondAlpha);
        return new(0, new(second.Red - first.Red, second.Green - first.Green, second.Blue - first.Blue, second.Alpha - first.Alpha), first, first == second);
    }

    internal static SvgGradientRampSegment Arbitrary(SvgGradientStop left, SvgGradientStop right)
    {
        float position = (float)left.Offset, gap = (float)right.Offset - position;
        if (gap <= 0) return Pair(right.Color, right.Color, right.Alpha, right.Alpha) with { Position = position, Constant = true };
        float inverseGap = 1f / gap;
        SvgShaderPaint first = Normalize(left.Color, left.Alpha), second = Normalize(right.Color, right.Alpha);
        SvgShaderPaint factor = new((second.Red - first.Red) * inverseGap, (second.Green - first.Green) * inverseGap,
            (second.Blue - first.Blue) * inverseGap, (second.Alpha - first.Alpha) * inverseGap);
        SvgShaderPaint bias = new(first.Red - factor.Red * position, first.Green - factor.Green * position,
            first.Blue - factor.Blue * position, first.Alpha - factor.Alpha * position);
        return new(position, factor, bias, false);
    }

    internal SvgShaderPaint Sample(float position, float opacity, int matrix)
    {
        if (Constant) position = 0;
        float stopAlpha = MathF.FusedMultiplyAdd(position, Factor.Alpha, Bias.Alpha);
        return new SvgShaderPaint(
            MathF.FusedMultiplyAdd(position, Factor.Red, Bias.Red) * stopAlpha * opacity,
            MathF.FusedMultiplyAdd(position, Factor.Green, Bias.Green) * stopAlpha * opacity,
            MathF.FusedMultiplyAdd(position, Factor.Blue, Bias.Blue) * stopAlpha * opacity,
            stopAlpha * opacity).Dither(matrix);
    }

    private static SvgShaderPaint Normalize(SvgColor color, float alpha)
        => new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha);
}
