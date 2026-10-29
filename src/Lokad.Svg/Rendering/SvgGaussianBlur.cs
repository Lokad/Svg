using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Describes device-space Gaussian deviations before choosing a bounded native kernel.</summary>
internal readonly record struct SvgGaussianDeviation
{
    internal SvgGaussianDeviation(float horizontal, float vertical)
    {
        if (!float.IsFinite(horizontal) || horizontal < 0) throw new ArgumentOutOfRangeException(nameof(horizontal));
        if (!float.IsFinite(vertical) || vertical < 0) throw new ArgumentOutOfRangeException(nameof(vertical));
        Horizontal = horizontal;
        Vertical = vertical;
    }

    internal float Horizontal { get; }
    internal float Vertical { get; }
}

/// <summary>Blurs borrowed premultiplied RGBA pixels with transparent samples outside their frame.</summary>
/// <remarks>
/// The caller owns both frames and their reservations. This routine never changes the source;
/// temporary owners release on completion, refusal and cancellation. Storage is reserved before
/// allocation, and pixel traversal checks cancellation. Independent invocations share no mutable state.
/// The fused three-box passes and scaled division follow the pinned Skia CPU blur arithmetic
/// (Copyright 2024 Google LLC; see THIRD_PARTY_NOTICES.txt). Color-space conversion belongs to
/// the filter graph. Small deviations retain Chromium's box-kernel selection, including
/// identity windows; large deviations use the placed rescaling path.
/// </remarks>
internal static class SvgGaussianBlur
{
    /// <summary>Rescales original image bounds progressively, blurs the reduced grid and restores its shader samples.</summary>
    /// <remarks>All temporary frames are budget-owned and released on every exit. Source and destination are borrowed;
    /// storage is reserved before allocation, and cancellation is checked per row. Color transfer is deferred to sampling.</remarks>
    internal static void ApplyRescaled(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height,
        SvgGaussianDeviation deviation, SvgPaintBounds sourceBounds, SvgPaintBounds requestedBounds, SvgFilterRasterPlacement placement, bool inputLinear, SvgFilterColorSpace space,
        Span<SvgShaderPaint> samples, RenderBudget budget, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(budget);
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > int.MaxValue / 4 || source.Length != pixels * 4 || destination.Length != source.Length)
            throw new ArgumentException("Rescaled filter frames must contain exactly four bytes per pixel.");
        if (source.Overlaps(destination)) throw new ArgumentException("The filter source and destination must be independent frames.");
        if (!samples.IsEmpty && samples.Length != pixels) throw new ArgumentException("Shader samples must match the filter frame.");
        token.ThrowIfCancellationRequested();
        if (!float.IsFinite(sourceBounds.Left) || !float.IsFinite(sourceBounds.Top) || !float.IsFinite(sourceBounds.Right) || !float.IsFinite(sourceBounds.Bottom)
            || sourceBounds.Left < int.MinValue || sourceBounds.Top < int.MinValue || sourceBounds.Right >= int.MaxValue || sourceBounds.Bottom >= int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Rescaled source bounds exceed the coordinate range.");
        float sigmaX = Math.Min(deviation.Horizontal, 532), sigmaY = Math.Min(deviation.Vertical, 532);
        float finalScaleX = sigmaX > 135 ? 135 / sigmaX : 1, finalScaleY = sigmaY > 135 ? 135 / sigmaY : 1;
        int xSteps = Steps(finalScaleX), ySteps = Steps(finalScaleY);
        int srcLeft = (int)Math.Floor(sourceBounds.Left), srcTop = (int)Math.Floor(sourceBounds.Top);
        double sourceWidth = Math.Ceiling(sourceBounds.Right) - srcLeft, sourceHeight = Math.Ceiling(sourceBounds.Bottom) - srcTop;
        if (sourceWidth > int.MaxValue || sourceHeight > int.MaxValue)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Rescaled source extents exceed the image range.");
        int srcWidth = (int)Math.Max(0, sourceWidth), srcHeight = (int)Math.Max(0, sourceHeight);
        if (srcWidth <= 0 || srcHeight <= 0)
        {
            destination.Clear(); samples.Clear(); token.ThrowIfCancellationRequested();
            return;
        }
        float centerX = xSteps == 0 ? 0 : .5f * (srcLeft + srcLeft + srcWidth);
        float centerY = ySteps == 0 ? 0 : .5f * (srcTop + srcTop + srcHeight);
        SvgPaintBounds logical = new(srcLeft, srcTop, srcLeft + srcWidth, srcTop + srcHeight);
        SvgConverter.RasterizedSurface current = default;
        try
        {
            bool first = true;
            do
            {
                float sx = xSteps > 0 ? xSteps > 1 ? .5f : srcWidth * finalScaleX / (logical.Right - logical.Left) : 1;
                float sy = ySteps > 0 ? ySteps > 1 ? .5f : srcHeight * finalScaleY / (logical.Bottom - logical.Top) : 1;
                if (xSteps > 0) xSteps--;
                if (ySteps > 0) ySteps--;
                float cx = sx == 1 ? 0 : .5f * logical.Left + .5f * logical.Right;
                float cy = sy == 1 ? 0 : .5f * logical.Top + .5f * logical.Bottom;
                SvgPaintBounds next = new((logical.Left - cx) * sx, (logical.Top - cy) * sy,
                    (logical.Right - cx) * sx, (logical.Bottom - cy) * sy);
                int lowLeft = (int)Math.Floor(next.Left), lowTop = (int)Math.Floor(next.Top);
                int lowWidth = (int)Math.Ceiling(next.Right) - lowLeft, lowHeight = (int)Math.Ceiling(next.Bottom) - lowTop;
                token.ThrowIfCancellationRequested();
                SvgConverter.RasterizedSurface down = Allocate(lowWidth, lowHeight);

                try
                {
                    for (int y = 0; y < lowHeight; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int x = 0; x < lowWidth; x++)
                        {
                            float sampleX = MathF.FusedMultiplyAdd(lowLeft + x + .5f, 1 / sx, first ? cx - srcLeft : cx - (float)Math.Floor(logical.Left));
                            float sampleY = MathF.FusedMultiplyAdd(lowTop + y + .5f, 1 / sy, first ? cy - srcTop : cy - (float)Math.Floor(logical.Top));
                            SvgShaderPaint sample = first ? Sample(source, width, height, srcLeft, srcTop, srcWidth, srcHeight, sampleX, sampleY, false)
                                : Sample(current.Pixels, current.Width, current.Height, 0, 0, current.Width, current.Height, sampleX, sampleY, false);
                            if (first && inputLinear != (space == SvgFilterColorSpace.LinearRgb)) sample = SvgFilterColorTransfer.Convert(sample, space);
                            sample.Blend(down.Pixels.AsSpan((y * lowWidth + x) * 4, 4), 255);
                        }
                    }
                }
                catch { down.Dispose(); throw; }
                if (!first) current.Dispose();
                current = down;
                first = false;
                logical = next;
            } while (xSteps > 0 || ySteps > 0);

            float scaleBackX = srcWidth / (logical.Right - logical.Left), scaleBackY = srcHeight / (logical.Bottom - logical.Top);
            float offsetX = centerX, offsetY = centerY;
            int outputLeft = (int)Math.Floor((0 - offsetX) / scaleBackX) - 1;
            int outputTop = (int)Math.Floor((0 - offsetY) / scaleBackY) - 1;
            int outputRight = (int)Math.Ceiling((width - offsetX) / scaleBackX) + 1;
            int outputBottom = (int)Math.Ceiling((height - offsetY) / scaleBackY) + 1;
            using SvgConverter.RasterizedSurface padded = Allocate(outputRight - outputLeft, outputBottom - outputTop);
            int imageLeft = (int)Math.Floor(logical.Left), imageTop = (int)Math.Floor(logical.Top);
            token.ThrowIfCancellationRequested();
            for (int row = 0; row < current.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                int y = imageTop + row - outputTop;
                if ((uint)y >= (uint)padded.Height) continue;
                int x = imageLeft - outputLeft;
                int start = Math.Max(0, -x), end = Math.Min(current.Width, padded.Width - x);
                if (end > start) current.Pixels.AsSpan((row * current.Width + start) * 4, (end - start) * 4)
                    .CopyTo(padded.Pixels.AsSpan((y * padded.Width + x + start) * 4, (end - start) * 4));
            }
            using SvgConverter.RasterizedSurface blurred = Allocate(padded.Width, padded.Height);
            Apply(padded.Pixels, blurred.Pixels, padded.Width, padded.Height,
                new(Math.Min(sigmaX / scaleBackX, 135), Math.Min(sigmaY / scaleBackY, 135)), budget, token);
            token.ThrowIfCancellationRequested();
            destination.Clear();
            SvgCpuBitmapOrigin sourceOrigin = placement.SourceOrigin;
            if (space == SvgFilterColorSpace.LinearRgb)
            {
                token.ThrowIfCancellationRequested();
                samples.Clear();
                SvgAnalyticClipBounds clip = placement.Clip;
                SvgAnalyticClipBounds parent = new(Math.Max(clip.Left, sourceOrigin.X), Math.Max(clip.Top, sourceOrigin.Y),
                    (int)Math.Min(clip.Right, (long)sourceOrigin.X + width), (int)Math.Min(clip.Bottom, (long)sourceOrigin.Y + height));
                if (parent.Right <= parent.Left || parent.Bottom <= parent.Top) return;
                int requestedLeft = (int)Math.Clamp(Math.Floor((double)sourceOrigin.X + requestedBounds.Left), parent.Left, parent.Right);
                int requestedTop = (int)Math.Clamp(Math.Floor((double)sourceOrigin.Y + requestedBounds.Top), parent.Top, parent.Bottom);
                int requestedRight = (int)Math.Clamp(Math.Ceiling((double)sourceOrigin.X + requestedBounds.Right), parent.Left, parent.Right);
                int requestedBottom = (int)Math.Clamp(Math.Ceiling((double)sourceOrigin.Y + requestedBounds.Bottom), parent.Top, parent.Bottom);
                foreach (SvgCpuRasterContext tile in placement.Grid.GetContexts(new(requestedLeft, requestedTop, requestedRight, requestedBottom)))
                {
                    token.ThrowIfCancellationRequested();
                    SvgCpuBitmapOrigin origin = new(Math.Max(tile.Origin.X, clip.Left), Math.Max(tile.Origin.Y, clip.Top));
                    int cropLeft = Math.Max(requestedLeft, tile.BitmapClip.Left), cropTop = Math.Max(requestedTop, tile.BitmapClip.Top);
                    int cropRight = Math.Min(requestedRight, tile.BitmapClip.Right), cropBottom = Math.Min(requestedBottom, tile.BitmapClip.Bottom);
                    int restoreLeft = (int)Math.Floor((cropLeft - (offsetX + sourceOrigin.X)) / scaleBackX) - 1;
                    int restoreTop = (int)Math.Floor((cropTop - (offsetY + sourceOrigin.Y)) / scaleBackY) - 1;
                    // The reduced source image is placed first. The blur subset is
                    // concatenated afterwards, in the parent's bitmap coordinates.
                    float translateX = scaleBackX * (restoreLeft + 1 - imageLeft)
                        + (scaleBackX * imageLeft + (offsetX + sourceOrigin.X - origin.X));
                    float translateY = scaleBackY * (restoreTop + 1 - imageTop)
                        + (scaleBackY * imageTop + (offsetY + sourceOrigin.Y - origin.Y));
                    SvgShaderMatrix restore = new(scaleBackX, 0, translateX, 0, scaleBackY, translateY);
                    if (!restore.TryInvert(out SvgShaderMatrix inverse))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Restored bitmap coordinates are not invertible.");
                    int firstX = Math.Max(cropLeft, tile.Interior.Left), lastX = Math.Min(cropRight, tile.Interior.Right);
                    int firstY = Math.Max(cropTop, tile.Interior.Top), lastY = Math.Min(cropBottom, tile.Interior.Bottom);
                    for (int y = firstY; y < lastY; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        float sampleY = MathF.FusedMultiplyAdd(y - origin.Y + .5f, inverse.ScaleY, inverse.TranslateY);
                        for (int x = firstX; x < lastX; x++)
                        {
                            float sampleX = MathF.FusedMultiplyAdd(x - origin.X + .5f, inverse.ScaleX, inverse.TranslateX);
                            SvgShaderPaint sample = Sample(blurred.Pixels, blurred.Width, blurred.Height,
                                restoreLeft + 1 - outputLeft, restoreTop + 1 - outputTop,
                                blurred.Width, blurred.Height, sampleX, sampleY, true);
                            sample = SvgFilterColorTransfer.Convert(sample, SvgFilterColorSpace.Srgb);
                            int index = (y - sourceOrigin.Y) * width + x - sourceOrigin.X;
                            if (!samples.IsEmpty) samples[index] = sample;
                            sample.Blend(destination.Slice(index * 4, 4), 255);
                        }
                    }
                }
                return;
            }
            // Packed restoration retains the qualified bitmap sampler and paint-alpha path.
            SvgShaderMatrix bitmapRestore = new(scaleBackX, 0, scaleBackX * (outputLeft + 1) + (offsetX + sourceOrigin.X),
                0, scaleBackY, scaleBackY * (outputTop + 1) + (offsetY + sourceOrigin.Y));
            if (!bitmapRestore.TryInvert(out SvgShaderMatrix restoreInverse))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Restored bitmap coordinates are not invertible.");
            long bitmapStep = (long)((double)restoreInverse.ScaleX * 4294967296d);
            long bitmapStart = (long)((double)((sourceOrigin.X + .5f) * restoreInverse.ScaleX + restoreInverse.TranslateX) * 4294967296d) + 4294967296L;
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                long bitmapX = bitmapStart;
                long bitmapY = (long)((double)((y + sourceOrigin.Y + .5f) * restoreInverse.ScaleY + restoreInverse.TranslateY) * 4294967296d) + 4294967296L;
                for (int x = 0; x < width; x++)
                {
                    SvgConstantShaderPaint value = SampleBitmap(blurred.Pixels, blurred.Width, blurred.Height, bitmapX, bitmapY);
                    Span<byte> pixel = destination.Slice((y * width + x) * 4, 4);
                    pixel[0] = value.Red; pixel[1] = value.Green; pixel[2] = value.Blue; pixel[3] = value.Alpha;
                    if (!samples.IsEmpty)
                    {
                        const float scale = 1f / 255f;
                        samples[y * width + x] = new(value.Red * scale, value.Green * scale, value.Blue * scale, value.Alpha * scale);
                    }
                    bitmapX += bitmapStep;
                }
            }
            return;

        }
        finally { if (current.Pixels is not null) current.Dispose(); }
        SvgConverter.RasterizedSurface Allocate(int w, int h)
        {
            token.ThrowIfCancellationRequested();
            long pixels = (long)w * h;
            if (w <= 0 || h <= 0 || pixels > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Rescaled blur exceeds contiguous image storage.");
            int bytes = (int)pixels * 4;
            RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(bytes), SvgMemoryCategory.Surface);
            try { return new(w, h, new byte[bytes], owner); }
            catch { owner.Dispose(); throw; }
        }
        static int Steps(float scale)
        {
            int steps = 0;float inverse = MathF.Ceiling(1 / scale);
            while ((1 << steps) < inverse) steps++;
            if (steps > 0 && scale * (1 << (steps - 1)) >= (steps == 1 ? .999f : .9f)) steps--;
            return steps;
        }
        static SvgConstantShaderPaint SampleBitmap(ReadOnlySpan<byte> data, int width, int height, long x, long y)
        {
            x -= 2147483648L; y -= 2147483648L;
            int left = (int)(x >> 32), top = (int)(y >> 32), fx = (int)(x >> 28 & 15), fy = (int)(y >> 28 & 15);
            return SvgConstantShaderPaint.Interpolate(Pixel(data, width, height, left, top), Pixel(data, width, height, left + 1, top),
                Pixel(data, width, height, left, top + 1), Pixel(data, width, height, left + 1, top + 1), fx, fy);
            static SvgConstantShaderPaint Pixel(ReadOnlySpan<byte> pixels, int width, int height, int x, int y)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height) return default;
                int at = (y * width + x) * 4;
                return new(pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
            }
        }
        static SvgShaderPaint Sample(ReadOnlySpan<byte> data, int w, int h, int ox, int oy, int sw, int sh, float x, float y, bool backingBorder)
        {
            float px = x - .5f, py = y - .5f;
            int left = (int)MathF.Floor(px), top = (int)MathF.Floor(py);
            // Match native save_xy before gathering either the source subset or
            // the blur result's initialized backing border.
            float fx = x + .5f - MathF.Floor(x + .5f), fy = y + .5f - MathF.Floor(y + .5f);
            SvgShaderPaint a = Pixel(data, w, h, ox, oy, sw, sh, left, top, backingBorder), b = Pixel(data, w, h, ox, oy, sw, sh, left + 1, top, backingBorder);
            SvgShaderPaint c = Pixel(data, w, h, ox, oy, sw, sh, left, top + 1, backingBorder), d = Pixel(data, w, h, ox, oy, sw, sh, left + 1, top + 1, backingBorder);
            float wa = (1 - fx) * (1 - fy), wb = fx * (1 - fy), wc = (1 - fx) * fy, wd = fx * fy;
            return new(Weighted(a.Red, b.Red, c.Red, d.Red, wa, wb, wc, wd), Weighted(a.Green, b.Green, c.Green, d.Green, wa, wb, wc, wd),
                Weighted(a.Blue, b.Blue, c.Blue, d.Blue, wa, wb, wc, wd), Weighted(a.Alpha, b.Alpha, c.Alpha, d.Alpha, wa, wb, wc, wd));
            static float Weighted(float a, float b, float c, float d, float wa, float wb, float wc, float wd) => ((a * wa + b * wb) + c * wc) + d * wd;
            static SvgShaderPaint Pixel(ReadOnlySpan<byte> data, int w, int h, int ox, int oy, int sw, int sh, int x, int y, bool backingBorder)
            {
                if ((!backingBorder && ((uint)x >= (uint)sw || (uint)y >= (uint)sh)) || (uint)(x + ox) >= (uint)w || (uint)(y + oy) >= (uint)h) return default;
                int offset = ((y + oy) * w + x + ox) * 4;
                const float scale = 1f / 255f;
                return new(data[offset] * scale, data[offset + 1] * scale, data[offset + 2] * scale, data[offset + 3] * scale);
            }
        }
    }

    internal static void Apply(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height,
        SvgGaussianDeviation deviation, RenderBudget budget, CancellationToken token)
        => Apply(source, destination, width, height, deviation, budget, token, null);

    /// <summary>The optional observer runs after each complete scanline; it borrows no pixels or owners.</summary>
    internal static void Apply(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height,
        SvgGaussianDeviation deviation, RenderBudget budget, CancellationToken token,
        Action<int>? completedScanline)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        long pixels = (long)width * height;
        if (pixels > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Filter frames exceed the supported array range.");
        int bytes = (int)pixels * 4;
        if (source.Length != bytes || destination.Length != bytes) throw new ArgumentException("Filter frames must contain exactly four bytes per pixel.");
        if (source.Overlaps(destination)) throw new ArgumentException("The filter source and destination must be independent frames.");
        token.ThrowIfCancellationRequested();
        BoxKernel horizontal = Kernel(deviation.Horizontal), vertical = Kernel(deviation.Vertical);
        if (horizontal.Identity && vertical.Identity) {}        if (horizontal.Identity && vertical.Identity)
        {
            source.CopyTo(destination);
            token.ThrowIfCancellationRequested();
            return;
        }

        int ringLength = Math.Max(horizontal.RingLength, vertical.RingLength);
        using RenderBudget.BudgetReservation ringOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<uint>(ringLength), SvgMemoryCategory.Surface);
        uint[] ring = new uint[ringLength];
        int scanlines = 0;
        if (horizontal.Identity) Axis(source, destination, vertical, false);
        else if (vertical.Identity) Axis(source, destination, horizontal, true);
        else
        {
            using RenderBudget.BudgetReservation intermediateOwner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(bytes), SvgMemoryCategory.Surface);
            byte[] intermediate = new byte[bytes];
            Axis(source, intermediate, horizontal, true);
            Axis(intermediate, destination, vertical, false);
        }
        token.ThrowIfCancellationRequested();
        return;

        void Axis(ReadOnlySpan<byte> input, Span<byte> output, BoxKernel kernel, bool alongRows)
        {
            int extent = alongRows ? width : height, lines = alongRows ? height : width;
            int firstSize = kernel.Window - 1, thirdSize = kernel.ThirdWindow - 1;
            for (int line = 0; line < lines; line++)
            {
                token.ThrowIfCancellationRequested();
                for (int channel = 0; channel < 4; channel++)
                {
                    ring.AsSpan(0, kernel.RingLength).Clear();
                    int first = 0, second = 0, third = 0;
                    uint sum0 = 0, sum1 = 0, sum2 = kernel.Half;
                    for (int leading = 0; leading < extent + kernel.Border; leading++)
                    {
                        token.ThrowIfCancellationRequested();
                        uint value = 0;
                        if (leading < extent)
                        {
                            int offset = alongRows ? (line * width + leading) * 4 + channel : (leading * width + line) * 4 + channel;
                            value = input[offset];
                        }
                        sum0 += value;
                        sum1 += sum0;
                        sum2 += sum1;
                        if (leading >= kernel.Border)
                        {
                            int position = leading - kernel.Border;
                            int target = alongRows ? (line * width + position) * 4 + channel : (position * width + line) * 4 + channel;
                            output[target] = (byte)(((ulong)sum2 * kernel.Factor) >> 32);
                        }
                        sum2 -= ring[2 * firstSize + third];
                        ring[2 * firstSize + third] = sum1;
                        sum1 -= ring[firstSize + second];
                        ring[firstSize + second] = sum0;
                        sum0 -= ring[first];
                        ring[first] = value;
                        if (++first == firstSize) first = 0;
                        if (++second == firstSize) second = 0;
                        if (++third == thirdSize) third = 0;
                    }
                }
                completedScanline?.Invoke(++scanlines);
            }
        }
    }

    /// <summary>Identifies a copying pixel kernel; nonzero blurs can still materialize and round image bounds.</summary>
    internal static bool IsIdentity(SvgGaussianDeviation deviation)
        => deviation.Horizontal <= 135 && deviation.Vertical <= 135
            && Kernel(deviation.Horizontal).Identity && Kernel(deviation.Vertical).Identity;

    private static BoxKernel Kernel(float sigma)
    {
        if (sigma <= 0.03f) return default;
        if (sigma > 135) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gaussian filter rescaling requires the placed image path.");
        int window = (int)MathF.Floor(sigma * 3f * MathF.Sqrt(2f * MathF.PI) / 4f + 0.5f);
        if (window <= 1) return default;
        int third = (window & 1) == 0 ? window + 1 : window;
        uint divisor = checked((uint)(window * window * third));
        uint factor = checked((uint)Math.Round(4294967296d / divisor, MidpointRounding.AwayFromZero));
        return new(window, third, (2 * (window - 1) + third - 1) / 2, factor, (divisor + 1) / 2);
    }

    private readonly record struct BoxKernel(int Window, int ThirdWindow, int Border, uint Factor, uint Half)
    {
        internal bool Identity => Window == 0;
        internal int RingLength => Identity ? 0 : 2 * (Window - 1) + ThirdWindow - 1;
    }
}
