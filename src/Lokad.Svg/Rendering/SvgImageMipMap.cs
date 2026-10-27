// Managed adaptation of Skia source-image mip selection and 8888 reduction.
// Copyright 2013, 2015 Google Inc.; 2023 Google LLC.
// Decoded-view sizing adapts Chromium. Copyright 2016, 2018 The Chromium Authors.
// See THIRD_PARTY_NOTICES.txt for the source pin and BSD license.
namespace Lokad.Svg.Rendering;

/// <summary>Reduces immutable premultiplied image views before their existing sampler.</summary>
/// <remarks>Stateless and thread-safe. Callers own source/destination storage, reserve
/// scratch before allocation, charge SampleCount per output pixel, and pass cancellation.
/// A level halves each axis independently, retaining a one-pixel axis.</remarks>
internal static class SvgImageMipMap
{
    // Recorded drawImage requests choose ceil-sized decoded levels from rounded
    // destination extents. This is independent of Skia's later floor-sized mip.
    internal static (int Width, int Height) SelectDecodeSize(SvgShaderMatrix forward, int width, int height)
    {
        float x = forward.ScaleX, y = forward.ScaleY;
        if (forward.SkewX != 0 || forward.SkewY != 0)
        {
            x = Length(forward.ScaleX, forward.SkewY);
            y = Length(forward.SkewX, forward.ScaleY);
            if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 1f / 4096 || y <= 1f / 4096) return (width, height);
        }
        float requestedWidth = MathF.Floor(MathF.Abs(width * x) + .5f);
        float requestedHeight = MathF.Floor(MathF.Abs(height * y) + .5f);
        if (requestedWidth == 0 || requestedHeight == 0) return (0, 0);
        int reducedWidth = width, reducedHeight = height;
        while (reducedWidth > 1 || reducedHeight > 1)
        {
            int nextWidth = (reducedWidth + 1) / 2, nextHeight = (reducedHeight + 1) / 2;
            if (nextWidth < requestedWidth || nextHeight < requestedHeight) break;
            reducedWidth = nextWidth;
            reducedHeight = nextHeight;
        }
        return (reducedWidth, reducedHeight);
    }

    internal static void Resize(ReadOnlySpan<byte> source, int stride, SvgAnalyticClipBounds bounds,
        Span<byte> destination, int width, int height, CancellationToken cancellationToken)
    {
        int sourceWidth = bounds.Right - bounds.Left, sourceHeight = bounds.Bottom - bounds.Top;
        float inverseX = 1f / ((float)width / sourceWidth), inverseY = 1f / ((float)height / sourceHeight);
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                if ((x & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                SvgConstantShaderPaint sample = SvgConstantShaderPaint.SampleAffine(source, stride, source.Length / stride / 4,
                    (x + .5f) * inverseX, (y + .5f) * inverseY, false, new(0, 0), bounds);
                int target = (y * width + x) * 4;
                destination[target] = sample.Red; destination[target + 1] = sample.Green;
                destination[target + 2] = sample.Blue; destination[target + 3] = sample.Alpha;
            }
        }
    }

    /// <summary>Selects the nearest floor-sized mip before source-replacement resizing.</summary>
    internal static int SelectLevel(SvgShaderMatrix inverse, int width, int height)
    {
        float level = GetLevel(inverse);
        if (level < 0) return 0;
        int selected = (int)MathF.Floor(level + .5f);
        int available = System.Numerics.BitOperations.Log2((uint)Math.Max(width, height));
        return selected <= available ? selected : 0;
    }

    /// <summary>Selects adjacent picture mips and their float interpolation weight.</summary>
    internal static (int Upper, float LowerWeight) SelectLinearLevels(SvgShaderMatrix inverse, int width, int height)
    {
        float level = GetLevel(inverse);
        if (level < 0) return (0, 0);
        int upper = (int)MathF.Floor(level);
        int available = System.Numerics.BitOperations.Log2((uint)Math.Max(width, height));
        return upper > available ? (0, 0) : (upper, upper == available ? 0 : level - upper);
    }

    private static float GetLevel(SvgShaderMatrix inverse)
    {
        float x = Length(inverse.ScaleX, inverse.SkewY), y = Length(inverse.SkewX, inverse.ScaleY);
        if (!float.IsFinite(x) || !float.IsFinite(y) || x <= 1f / 4096 || y <= 1f / 4096) return -1;
        float scale = MathF.Min(1f / x, 1f / y);
        return scale <= 0 || scale >= 1 ? -1 : MathF.Max(-MathF.Log2(scale) - .5f, 0);
    }

    private static float Length(float a, float b)
    {
        float squared = a * a + b * b;
        return float.IsFinite(squared) ? MathF.Sqrt(squared) : (float)Math.Sqrt((double)a * a + (double)b * b);
    }

    internal static int SampleCount(SvgAnalyticClipBounds bounds)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        return (width == 1 ? 1 : 2 + (width & 1)) * (height == 1 ? 1 : 2 + (height & 1));
    }

    /// <summary>Writes one nonoverlapping level, preserving parent stride and truncating only after both axis sums.</summary>
    internal static void Reduce(ReadOnlySpan<byte> source, int stride, SvgAnalyticClipBounds bounds,
        Span<byte> destination, CancellationToken cancellationToken)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        int nx = width == 1 ? 1 : 2 + (width & 1), ny = height == 1 ? 1 : 2 + (height & 1);
        int shift = nx - 1 + ny - 1;
        int outputWidth = Math.Max(1, width / 2), outputHeight = Math.Max(1, height / 2);
        for (int y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < outputWidth; x++)
            {
                if ((x & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                int red = 0, green = 0, blue = 0, alpha = 0;
                for (int dy = 0; dy < ny; dy++)
                for (int dx = 0; dx < nx; dx++)
                {
                    int weight = (nx == 3 && dx == 1 ? 2 : 1) * (ny == 3 && dy == 1 ? 2 : 1);
                    int at = ((bounds.Top + y * 2 + dy) * stride + bounds.Left + x * 2 + dx) * 4;
                    red += source[at] * weight;
                    green += source[at + 1] * weight;
                    blue += source[at + 2] * weight;
                    alpha += source[at + 3] * weight;
                }
                int target = (y * outputWidth + x) * 4;
                destination[target] = (byte)(red >> shift);
                destination[target + 1] = (byte)(green >> shift);
                destination[target + 2] = (byte)(blue >> shift);
                destination[target + 3] = (byte)(alpha >> shift);
            }
        }
    }
}
