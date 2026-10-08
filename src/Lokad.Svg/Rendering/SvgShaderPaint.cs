// Managed adaptation of Skia's constant shader construction, high-precision premul,
// scale, dither, source-over and RGBA8 store stages. Copyright 2016 Google Inc.; 2018 Google LLC.
// Legacy sRGB normalization adapts Chromium. Copyright 2012 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the BSD license and pinned source revision.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains a constant shader's packed premultiplied lanes for bounded low-precision replay.</summary>
/// <remarks>Immutable and allocation-free. The caller owns pixels, charges raster work and checks row cancellation.</remarks>
internal readonly record struct SvgConstantShaderPaint(byte Red, byte Green, byte Blue, byte Alpha)
{
    internal void Blend(Span<byte> destination, byte coverage)
    {
        if (coverage == 0 || Alpha == 0) return;
        int red = (Red * coverage + 255) >> 8;
        int green = (Green * coverage + 255) >> 8;
        int blue = (Blue * coverage + 255) >> 8;
        int alpha = (Alpha * coverage + 255) >> 8;
        int inverse = 255 - alpha;
        destination[0] = (byte)(red + Divide255(destination[0] * inverse));
        destination[1] = (byte)(green + Divide255(destination[1] * inverse));
        destination[2] = (byte)(blue + Divide255(destination[2] * inverse));
        destination[3] = (byte)(alpha + Divide255(destination[3] * inverse));

        static int Divide255(int value) { value += 128; return (value + (value >> 8)) >> 8; }
    }
}

/// <summary>Retains a shader sample as premultiplied floats until pixel storage.</summary>
/// <remarks>Immutable, allocation-free and safe to share. Callers own the destination,
/// charge raster work and check cancellation between rows. Samples retain no input storage.</remarks>
internal readonly record struct SvgShaderPaint(float Red, float Green, float Blue, float Alpha)
{
    internal static SvgConstantShaderPaint ConstantGradient(SvgGradientStop stop, float opacity)
    {
        float alpha = stop.Alpha * opacity;
        return new(Quantize(stop.Color.Red / 255f * stop.Alpha * opacity), Quantize(stop.Color.Green / 255f * stop.Alpha * opacity), Quantize(stop.Color.Blue / 255f * stop.Alpha * opacity), Quantize(alpha));

        static byte Quantize(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
    }

    internal static SvgShaderPaint Ramp(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end, double offset1, double offset2, double position, byte opacity, int matrix)
        => Ramp(stops, start, end, offset1, offset2, position, opacity / 255f, matrix);
    internal static SvgShaderPaint Ramp(ReadOnlySpan<SvgGradientStop> stops, SvgColor start, SvgColor end, double offset1, double offset2, double position, float opacityScale, int matrix)
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
        return GradientAlpha(start, end, firstAlpha, secondAlpha, position, opacityScale, matrix);
    }

    private static SvgShaderPaint GradientAlpha(SvgColor start, SvgColor end, float firstAlpha, float secondAlpha, double position, float opacityScale, int matrix)
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
        => Pattern(red, green, blue, alpha, opacity * (1f / 255f));
    internal static SvgShaderPaint Pattern(byte red, byte green, byte blue, byte alpha, float opacityScale)
    {
        const float scale = 1f / 255f;
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
