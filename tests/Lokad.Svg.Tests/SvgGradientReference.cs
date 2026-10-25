// Gradient interpolation adapts Skia. Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
// Legacy sRGB normalization adapts Chromium. Copyright 2012 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the BSD license and pinned source revision.
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>Preserves the unprepared interpolation arithmetic as an independent test reference for prepared ramps.</summary>
internal static class SvgGradientReference
{
    /// <summary>Evaluates source pairs and arbitrary intervals independently of prepared storage; uniform multi-stop cases use native image references.</summary>
    internal static SvgShaderPaint SampleAbsolute(ReadOnlySpan<SvgGradientStop> stops, double position, float opacityScale, int matrix)
    {
        if (stops.Length == 0) return GradientAlpha(default, default, 0, 0, 0, opacityScale, matrix);
        SvgGradientStop first = stops[0], final = stops[^1];
        if (stops.Length == 1) return GradientAlpha(first.Color, first.Color, first.Alpha, first.Alpha, 0, opacityScale, matrix);
        float coordinate = (float)position, start = (float)first.Offset, end = (float)final.Offset;
        if (stops.Length == 2 && start == 0 && end == 1)
            return GradientAlpha(first.Color, final.Color, first.Alpha, final.Alpha, Math.Clamp(coordinate, 0, 1), opacityScale, matrix);
        if (coordinate < start) return GradientAlpha(first.Color, first.Color, first.Alpha, first.Alpha, coordinate, opacityScale, matrix);
        if (coordinate >= end) return GradientAlpha(final.Color, final.Color, final.Alpha, final.Alpha, coordinate, opacityScale, matrix);
        int segment = 0, last = stops.Length - 2;
        while (segment < last)
        {
            int middle = (segment + last + 1) / 2;
            if (coordinate >= (float)stops[middle].Offset) segment = middle;
            else last = middle - 1;
        }
        SvgGradientStop left = stops[segment], right = stops[segment + 1];
        float leftPosition = (float)left.Offset, gap = (float)right.Offset - leftPosition;
        if (gap <= 0) return GradientAlpha(right.Color, right.Color, right.Alpha, right.Alpha, 0, opacityScale, matrix);
        float inverseGap = 1f / gap;
        float stopAlpha = Interpolate(left.Alpha, right.Alpha);
        return new SvgShaderPaint(
            Interpolate(left.Color.Red / 255f, right.Color.Red / 255f) * stopAlpha * opacityScale,
            Interpolate(left.Color.Green / 255f, right.Color.Green / 255f) * stopAlpha * opacityScale,
            Interpolate(left.Color.Blue / 255f, right.Color.Blue / 255f) * stopAlpha * opacityScale,
            stopAlpha * opacityScale).Dither(matrix);

        float Interpolate(float first, float second)
        {
            float factor = (second - first) * inverseGap;
            float bias = first - factor * leftPosition;
            return MathF.FusedMultiplyAdd(coordinate, factor, bias);
        }

        static SvgShaderPaint GradientAlpha(SvgColor start, SvgColor end, float firstAlpha, float secondAlpha, double position, float opacityScale, int matrix)
        {
            float t = (float)position;
            float stopAlpha = MathF.FusedMultiplyAdd(t, secondAlpha - firstAlpha, firstAlpha);
            float alpha = stopAlpha * opacityScale;
            float red = MathF.FusedMultiplyAdd(t, end.Red / 255f - start.Red / 255f, start.Red / 255f) * stopAlpha * opacityScale;
            float green = MathF.FusedMultiplyAdd(t, end.Green / 255f - start.Green / 255f, start.Green / 255f) * stopAlpha * opacityScale;
            float blue = MathF.FusedMultiplyAdd(t, end.Blue / 255f - start.Blue / 255f, start.Blue / 255f) * stopAlpha * opacityScale;
            return new SvgShaderPaint(red, green, blue, alpha).Dither(matrix);
        }
    }
}
