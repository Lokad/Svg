// Managed adaptation of Skia's high-precision premul, scale, dither,
// source-over and RGBA8 store stages. Copyright 2018 Google LLC.
// Legacy sRGB normalization adapts Chromium. Copyright 2012 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the BSD license and pinned source revision.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains a shader sample as premultiplied floats until pixel storage.</summary>
/// <remarks>Immutable, allocation-free and safe to share. Callers own the destination,
/// charge raster work and check cancellation between rows. Samples retain no input storage.</remarks>
internal readonly record struct SvgShaderPaint(float Red, float Green, float Blue, float Alpha)
{
    internal static SvgShaderPaint Ramp(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end, double offset1, double offset2, double position, byte opacity, int matrix)
    {
        float firstAlpha = stops.Length == 0 ? start.Alpha * (1f / 255f) : stops[0].Alpha;
        float secondAlpha = stops.Length == 0 ? end.Alpha * (1f / 255f) : stops[^1].Alpha;
        if (stops.Length > 2)
        {
            // Parsing bounds the ramp to eight stops, keeping pixel work bounded.
            double absolute = offset1 + position * (offset2 - offset1);
            int segment = 0;
            while (segment < stops.Length - 2 && absolute >= stops[segment + 1].Offset) segment++;
            firstAlpha = stops[segment].Alpha;
            secondAlpha = stops[segment + 1].Alpha;
            start = stops[segment].Color;
            end = stops[segment + 1].Color;
            position = stops[segment + 1].Offset <= stops[segment].Offset ? 1 : (absolute - stops[segment].Offset) / (stops[segment + 1].Offset - stops[segment].Offset);
        }
        return GradientAlpha(start, end, firstAlpha, secondAlpha, position, opacity, matrix);
    }

    private static SvgShaderPaint GradientAlpha(SvgColor start, SvgColor end, float firstAlpha, float secondAlpha, double position, byte opacity, int matrix)
    {
        float t = (float)position;
        float stopAlpha = MathF.FusedMultiplyAdd(t, secondAlpha - firstAlpha, firstAlpha);
        float opacityScale = opacity / 255f;
        float alpha = stopAlpha * opacityScale;
        // CSS legacy RGB normalizes by division before native factor/bias interpolation.
        float red = MathF.FusedMultiplyAdd(t, end.Red / 255f - start.Red / 255f, start.Red / 255f) * stopAlpha * opacityScale;
        float green = MathF.FusedMultiplyAdd(t, end.Green / 255f - start.Green / 255f, start.Green / 255f) * stopAlpha * opacityScale;
        float blue = MathF.FusedMultiplyAdd(t, end.Blue / 255f - start.Blue / 255f, start.Blue / 255f) * stopAlpha * opacityScale;
        return new SvgShaderPaint(red, green, blue, alpha).Dither(matrix);
    }

    /// <summary>Applies native ordered dither to supplied premultiplied float lanes.</summary>
    internal SvgShaderPaint Dither(int matrix)
    {
        const float scale = 1f / 255f;
        // Native dithering is centered around zero and precedes coverage.
        float dither = matrix * (2f / 128f) - 63f / 128f;
        return new(Math.Clamp(MathF.FusedMultiplyAdd(dither, scale, Red), 0, Alpha),
            Math.Clamp(MathF.FusedMultiplyAdd(dither, scale, Green), 0, Alpha),
            Math.Clamp(MathF.FusedMultiplyAdd(dither, scale, Blue), 0, Alpha), Alpha);
    }

    internal static SvgShaderPaint Pattern(byte red, byte green, byte blue, byte alpha, byte opacity)
    {
        const float scale = 1f / 255f;
        float opacityScale = opacity * scale;
        return new(red * scale * opacityScale, green * scale * opacityScale, blue * scale * opacityScale, alpha * scale * opacityScale);
    }

    /// <summary>Applies byte coverage and source-over to a premultiplied RGBA8 pixel.</summary>
    internal void Blend(Span<byte> destination, byte coverage)
    {
        if (coverage == 0 || Alpha == 0) return;
        const float byteScale = 1f / 255f;
        float scale = coverage * byteScale;
        float alpha = Alpha * scale;
        float inverse = 1 - alpha;
        destination[0] = Quantize(MathF.FusedMultiplyAdd(destination[0] * byteScale, inverse, Red * scale));
        destination[1] = Quantize(MathF.FusedMultiplyAdd(destination[1] * byteScale, inverse, Green * scale));
        destination[2] = Quantize(MathF.FusedMultiplyAdd(destination[2] * byteScale, inverse, Blue * scale));
        destination[3] = Quantize(MathF.FusedMultiplyAdd(destination[3] * byteScale, inverse, alpha));

        // The x64 SIMD native store uses nearest-even conversion after scaling.
        static byte Quantize(float value) => (byte)Math.Clamp(MathF.Round(value * 255f, MidpointRounding.ToEven), 0, 255);
    }
}
