// Managed adaptation of Skia's constant shader construction, arbitrary-stop ramps,
// high-precision premul, scale, dither, source-over and RGBA8 store stages.
// Copyright 2016 Google Inc.; 2018, 2022 Google LLC.
// Legacy sRGB normalization adapts Chromium. Copyright 2012 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the BSD license and pinned source revision.
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Retains a constant shader's packed premultiplied lanes for bounded low-precision replay.</summary>
/// <remarks>Immutable and allocation-free. The caller owns pixels and checks row cancellation.</remarks>
internal readonly record struct SvgConstantShaderPaint(byte Red, byte Green, byte Blue, byte Alpha)
{
    /// <summary>Samples a packed affine bitmap with native signed Q15 bilinear fractions.</summary>
    /// <remarks>Pixels are borrowed and premultiplied. The caller validates dimensions, owns work
    /// and cancellation, and applies paint opacity after sampling. Transparent borders serve
    /// filtered layers; ordinary embedded images clamp their edge pixels.</remarks>
    internal static SvgConstantShaderPaint SampleAffine(ReadOnlySpan<byte> pixels, int width, int height,
        float x, float y, bool transparentBorder)
        => SampleAffine(pixels, width, height, x, y, transparentBorder, new(0, 0));

    /// <summary>Retains a native bitmap's sampling phase independently of compact pixel storage.</summary>
    /// <remarks>Pixel offsets are applied after fixed-coordinate rounding. Pixels, work and
    /// cancellation remain caller-owned; the offset allocates no storage.</remarks>
    internal static SvgConstantShaderPaint SampleAffine(ReadOnlySpan<byte> pixels, int width, int height,
        float x, float y, bool transparentBorder, SvgCpuBitmapOrigin pixelOffset)
        => SampleAffine(pixels, width, height, x, y, transparentBorder, pixelOffset, new(0, 0, width, height));

    /// <summary>Samples an integer source view without copying the decoded raster.</summary>
    /// <remarks>Bounds are admitted pixel coordinates within the borrowed raster. Coordinates
    /// belong to the view; offsets follow fixed rounding. The caller owns work and cancellation.</remarks>
    internal static SvgConstantShaderPaint SampleAffine(ReadOnlySpan<byte> pixels, int width, int height,
        float x, float y, bool transparentBorder, SvgCpuBitmapOrigin pixelOffset, SvgAnalyticClipBounds bounds)
    {
        int sampleWidth = bounds.Right - bounds.Left, sampleHeight = bounds.Bottom - bounds.Top;
        if (transparentBorder && (x + pixelOffset.X < -1 || y + pixelOffset.Y < -1
            || x + pixelOffset.X > sampleWidth + 1 || y + pixelOffset.Y > sampleHeight + 1)) return default;
        if (!transparentBorder) { x = Math.Clamp(x, -1, sampleWidth); y = Math.Clamp(y, -1, sampleHeight); }
        long qx = (long)MathF.Floor(65536f * x + .5f) - 32768 + ((long)pixelOffset.X << 16);
        long qy = (long)MathF.Floor(65536f * y + .5f) - 32768 + ((long)pixelOffset.Y << 16);
        int tx = unchecked((short)(qx ^ 0x8000)), ty = unchecked((short)(qy ^ 0x8000));
        int left = (int)(qx >> 16), top = (int)(qy >> 16);
        return InterpolatePipeline(Pixel(pixels, width, left, top, transparentBorder, bounds),
            Pixel(pixels, width, left + 1, top, transparentBorder, bounds),
            Pixel(pixels, width, left, top + 1, transparentBorder, bounds),
            Pixel(pixels, width, left + 1, top + 1, transparentBorder, bounds), tx, ty);

        static SvgConstantShaderPaint Pixel(ReadOnlySpan<byte> data, int stride, int px, int py, bool border, SvgAnalyticClipBounds subset)
        {
            int w = subset.Right - subset.Left, h = subset.Bottom - subset.Top;
            if (border && ((uint)px >= (uint)w || (uint)py >= (uint)h)) return default;
            int at = ((Math.Clamp(py, 0, h - 1) + subset.Top) * stride + Math.Clamp(px, 0, w - 1) + subset.Left) * 4;
            return new(data[at], data[at + 1], data[at + 2], data[at + 3]);
        }
    }

    /// <summary>Interpolates four premultiplied pixels with native four-bit bitmap fractions.</summary>
    /// <remarks>Fractions are in [0,15]. Allocation-free bounded arithmetic; callers own samples,
    /// observe cancellation. Opacity and coverage are applied after interpolation.</remarks>
    internal static SvgConstantShaderPaint Interpolate(SvgConstantShaderPaint a, SvgConstantShaderPaint b,
        SvgConstantShaderPaint c, SvgConstantShaderPaint d, int fractionX, int fractionY)
    {
        int wa = (16 - fractionX) * (16 - fractionY), wb = fractionX * (16 - fractionY);
        int wc = (16 - fractionX) * fractionY, wd = fractionX * fractionY;
        return new(Lane(a.Red, b.Red, c.Red, d.Red, wa, wb, wc, wd), Lane(a.Green, b.Green, c.Green, d.Green, wa, wb, wc, wd),
            Lane(a.Blue, b.Blue, c.Blue, d.Blue, wa, wb, wc, wd), Lane(a.Alpha, b.Alpha, c.Alpha, d.Alpha, wa, wb, wc, wd));
        static byte Lane(byte a, byte b, byte c, byte d, int wa, int wb, int wc, int wd)
            => (byte)((a * wa + b * wb + c * wc + d * wd) >> 8);
    }

    /// <summary>Interpolates four packed pixels with the raster pipeline's signed Q15 fractions.</summary>
    /// <remarks>Fractions encode [0,1] as [-32768,32767]. The caller prepares native fixed coordinates,
    /// owns the samples and checks cancellation during sampling. Legacy bitmap fractions remain separate.</remarks>
    internal static SvgConstantShaderPaint InterpolatePipeline(SvgConstantShaderPaint a, SvgConstantShaderPaint b,
        SvgConstantShaderPaint c, SvgConstantShaderPaint d, int fractionX, int fractionY)
    {
        return new(Lane(a.Red, b.Red, c.Red, d.Red, fractionX, fractionY), Lane(a.Green, b.Green, c.Green, d.Green, fractionX, fractionY),
            Lane(a.Blue, b.Blue, c.Blue, d.Blue, fractionX, fractionY), Lane(a.Alpha, b.Alpha, c.Alpha, d.Alpha, fractionX, fractionY));

        static byte Lane(byte a, byte b, byte c, byte d, int fractionX, int fractionY)
        {
            int upper = Horizontal(a, b, fractionX), lower = Horizontal(c, d, fractionX);
            return (byte)((Multiply(fractionY, lower - upper) + lower + upper + 128) >> 8);

            static int Horizontal(int left, int right, int fraction)
                => (Multiply(fraction, (right - left) << 7) + ((right + left) << 7) + 1) >> 1;
            static int Multiply(int first, int second) => (first * second + 16384) >> 15;
        }
    }

    /// <summary>Composites a bitmap sample with native packed row coverage arithmetic.</summary>
    /// <remarks>Samples and destination are premultiplied. The caller owns storage, work and cancellation.
    /// Covered source and destination products remain together until the final shift.</remarks>
    internal void BlendBitmap(Span<byte> destination, byte coverage)
    {
        if (coverage == 255)
        {
            SvgConverter.BlendPremultipliedOver(destination, Red, Green, Blue, Alpha);
            return;
        }
        int sourceScale = coverage + 1;
        int inverse = 65535 - Alpha * sourceScale;
        int destinationScale = (inverse + (inverse >> 8)) >> 8;
        destination[0] = (byte)((Red * sourceScale + destination[0] * destinationScale) >> 8);
        destination[1] = (byte)((Green * sourceScale + destination[1] * destinationScale) >> 8);
        destination[2] = (byte)((Blue * sourceScale + destination[2] * destinationScale) >> 8);
        destination[3] = (byte)((Alpha * sourceScale + destination[3] * destinationScale) >> 8);
    }

    internal void Blend(Span<byte> destination, byte coverage)
        => Blend(destination, coverage, false);

    /// <summary>Optionally preserves native opaque SrcOver-to-Src reduction before coverage interpolation.</summary>
    internal void Blend(Span<byte> destination, byte coverage, bool reduceOpaqueSource)
    {
        if (coverage == 0 || Alpha == 0) return;
        if (reduceOpaqueSource && Alpha == 255)
        {
            int inverseCoverage = 255 - coverage;
            destination[0] = (byte)((destination[0] * inverseCoverage + Red * coverage + 255) >> 8);
            destination[1] = (byte)((destination[1] * inverseCoverage + Green * coverage + 255) >> 8);
            destination[2] = (byte)((destination[2] * inverseCoverage + Blue * coverage + 255) >> 8);
            destination[3] = (byte)((destination[3] * inverseCoverage + 255 * coverage + 255) >> 8);
            return;
        }
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
/// check cancellation between rows. Samples retain no input storage.</remarks>
internal readonly record struct SvgShaderPaint(float Red, float Green, float Blue, float Alpha)
{
    /// <summary>Samples a filtered bitmap while retaining floating interpolation through the final store.</summary>
    /// <remarks>Pixels are borrowed premultiplied RGBA8. Dimensions and coordinates are caller-validated;
    /// transparent borders belong to filter surfaces. The caller checks cancellation during sampling.</remarks>
    internal static SvgShaderPaint SampleAffine(ReadOnlySpan<byte> pixels, int width, int height,
        float x, float y, SvgCpuBitmapOrigin pixelOffset, float opacity)
        => SampleBilinear(pixels, width, new(0, 0, width, height), x, y, pixelOffset, opacity, true);

    /// <summary>Samples an admitted image view with clamped edges and the parent raster's stride.</summary>
    /// <remarks>Pixels are borrowed premultiplied RGBA8. Bounds and coordinates are caller-validated;
    /// sampling owns no storage. The caller owns work accounting and cancellation.</remarks>
    internal static SvgShaderPaint SampleView(ReadOnlySpan<byte> pixels, int stride, SvgAnalyticClipBounds bounds,
        float x, float y, float opacity)
        => SampleBilinear(pixels, stride, bounds, x, y, new(0, 0), opacity, false);

    private static SvgShaderPaint SampleBilinear(ReadOnlySpan<byte> pixels, int stride, SvgAnalyticClipBounds bounds,
        float x, float y, SvgCpuBitmapOrigin pixelOffset, float opacity, bool transparentBorder)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (transparentBorder && (x + pixelOffset.X < -1 || y + pixelOffset.Y < -1
            || x + pixelOffset.X > width + 1 || y + pixelOffset.Y > height + 1)) return default;
        if (!transparentBorder) { x = Math.Clamp(x, -1, width); y = Math.Clamp(y, -1, height); }
        // Native high-precision gathering computes weights before shifting the
        // texel addresses. Subtracting .5 first loses a rounding boundary at powers of two.
        float weightX = x + .5f, weightY = y + .5f;
        float centeredX = x - .5f, centeredY = y - .5f;
        float floorX = MathF.Floor(centeredX), floorY = MathF.Floor(centeredY);
        int ix = (int)floorX + pixelOffset.X, iy = (int)floorY + pixelOffset.Y;
        int right = ix + 1, bottom = iy + 1;
        if (!transparentBorder)
        {
            ix = Math.Clamp(ix, 0, width - 1); right = Math.Clamp(right, 0, width - 1);
            iy = Math.Clamp(iy, 0, height - 1); bottom = Math.Clamp(bottom, 0, height - 1);
        }
        return InterpolatePremultiplied(Pixel(pixels, stride, bounds, ix, iy), Pixel(pixels, stride, bounds, right, iy),
            Pixel(pixels, stride, bounds, ix, bottom), Pixel(pixels, stride, bounds, right, bottom),
            weightX - MathF.Floor(weightX), weightY - MathF.Floor(weightY), opacity);

        static SvgConstantShaderPaint Pixel(ReadOnlySpan<byte> data, int stride, SvgAnalyticClipBounds bounds, int x, int y)
        {
            int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return default;
            int at = ((y + bounds.Top) * stride + x + bounds.Left) * 4;
            return new(data[at], data[at + 1], data[at + 2], data[at + 3]);
        }
    }

    /// <summary>Interpolates premultiplied picture pixels before paint alpha and the floating store.</summary>
    /// <remarks>Allocation-free shared image/repeat sampling. Callers own pixels, sampling work and cancellation.</remarks>
    internal static SvgShaderPaint InterpolatePremultiplied(SvgConstantShaderPaint a, SvgConstantShaderPaint b,
        SvgConstantShaderPaint c, SvgConstantShaderPaint d, float fx, float fy, float opacity)
    {
        float wa = (1 - fx) * (1 - fy), wb = fx * (1 - fy), wc = (1 - fx) * fy, wd = fx * fy;
        return new(Lane(a.Red, b.Red, c.Red, d.Red, wa, wb, wc, wd) * opacity,
            Lane(a.Green, b.Green, c.Green, d.Green, wa, wb, wc, wd) * opacity,
            Lane(a.Blue, b.Blue, c.Blue, d.Blue, wa, wb, wc, wd) * opacity,
            Lane(a.Alpha, b.Alpha, c.Alpha, d.Alpha, wa, wb, wc, wd) * opacity);

        static float Lane(byte a, byte b, byte c, byte d, float wa, float wb, float wc, float wd)
        {
            const float scale = 1f / 255f;
            float value = MathF.FusedMultiplyAdd(wa, a * scale, 0);
            value = MathF.FusedMultiplyAdd(wb, b * scale, value);
            value = MathF.FusedMultiplyAdd(wc, c * scale, value);
            return MathF.FusedMultiplyAdd(wd, d * scale, value);
        }
    }

    /// <summary>Packs prepared premultiplied shader lanes for native constant-paint replay.</summary>
    internal SvgConstantShaderPaint AsConstant(float opacity)
    {
        return new(Quantize(Red * opacity), Quantize(Green * opacity), Quantize(Blue * opacity), Quantize(Alpha * opacity));

        static byte Quantize(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
    }

    internal static SvgConstantShaderPaint ConstantGradient(SvgGradientStop stop, float opacity)
    {
        float alpha = stop.Alpha * opacity;
        return new(Quantize(stop.Color.Red / 255f * stop.Alpha * opacity), Quantize(stop.Color.Green / 255f * stop.Alpha * opacity), Quantize(stop.Color.Blue / 255f * stop.Alpha * opacity), Quantize(alpha));

        static byte Quantize(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
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
        // Native normalized blitters clamp shader lanes before coverage and source-over.
        float alpha = Clamp(Alpha) * scale;
        float inverse = 1 - alpha;
        destination[0] = Quantize(MathF.FusedMultiplyAdd(destination[0] * byteScale, inverse, Clamp(Red) * scale));
        destination[1] = Quantize(MathF.FusedMultiplyAdd(destination[1] * byteScale, inverse, Clamp(Green) * scale));
        destination[2] = Quantize(MathF.FusedMultiplyAdd(destination[2] * byteScale, inverse, Clamp(Blue) * scale));
        destination[3] = Quantize(MathF.FusedMultiplyAdd(destination[3] * byteScale, inverse, alpha));

        static float Clamp(float value) => float.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1);
        // The x64 SIMD native store uses nearest-even conversion after scaling.
        static byte Quantize(float value) => (byte)Math.Clamp(MathF.Round(value * 255f, MidpointRounding.ToEven), 0, 255);
    }
}
