// Gradient interpolation adapts Skia. Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
// Legacy sRGB normalization adapts Chromium. Copyright 2012 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the BSD license and pinned source revision.
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>Preserves the unprepared interpolation arithmetic as an independent test reference for prepared ramps.</summary>
internal static class SvgGradientReference
{
    /// <summary>Samples an absolute gradient coordinate after spreading, padding beyond the authored end stops.</summary>
    internal static SvgShaderPaint SampleAbsolute(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end,
        double offset1, double offset2, double position, float opacityScale, int matrix)
    {
        double absolute = Math.Clamp(position, offset1, offset2);
        float firstAlpha = stops.Length == 0 ? start.Alpha * (1f / 255f) : stops[0].Alpha;
        float secondAlpha = stops.Length == 0 ? end.Alpha * (1f / 255f) : stops[^1].Alpha;
        if (stops.Length > 2)
        {
            // The 256-stop admission bound permits at most eight search comparisons per pixel.
            float coordinate = (float)(0d + absolute * 1d);
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
            // Compute each coefficient at the sample, independently of the prepared segment array.
            float stopAlpha = Interpolate(left.Alpha, right.Alpha);
            float alpha = stopAlpha * opacityScale;
            return new SvgShaderPaint(
                Interpolate(left.Color.Red / 255f, right.Color.Red / 255f) * stopAlpha * opacityScale,
                Interpolate(left.Color.Green / 255f, right.Color.Green / 255f) * stopAlpha * opacityScale,
                Interpolate(left.Color.Blue / 255f, right.Color.Blue / 255f) * stopAlpha * opacityScale,
                alpha).Dither(matrix);

            float Interpolate(float first, float second)
            {
                float factor = (second - first) * inverseGap;
                float bias = first - factor * leftPosition;
                return MathF.FusedMultiplyAdd(coordinate, factor, bias);
            }
        }
        double local = offset2 > offset1 ? (absolute - offset1) / (offset2 - offset1) : 0;
        return GradientAlpha(start, end, firstAlpha, secondAlpha, local, opacityScale, matrix);

        static SvgShaderPaint GradientAlpha(SvgColor start, SvgColor end, float firstAlpha, float secondAlpha, double position, float opacityScale, int matrix)
        {
            float t = (float)position;
            float stopAlpha = MathF.FusedMultiplyAdd(t, secondAlpha - firstAlpha, firstAlpha);
            float alpha = stopAlpha * opacityScale;
            // CSS legacy RGB normalizes by division before native factor/bias interpolation.
            float red = MathF.FusedMultiplyAdd(t, end.Red / 255f - start.Red / 255f, start.Red / 255f) * stopAlpha * opacityScale;
            float green = MathF.FusedMultiplyAdd(t, end.Green / 255f - start.Green / 255f, start.Green / 255f) * stopAlpha * opacityScale;
            float blue = MathF.FusedMultiplyAdd(t, end.Blue / 255f - start.Blue / 255f, start.Blue / 255f) * stopAlpha * opacityScale;
            return new SvgShaderPaint(red, green, blue, alpha).Dither(matrix);
        }
    }
}
