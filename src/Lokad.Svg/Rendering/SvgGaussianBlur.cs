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
/// temporary owners release on completion, denial and cancellation. Work is admitted before
/// allocation or execution. Independent invocations share no mutable state.
/// The fused three-box passes and scaled division follow the pinned Skia CPU blur arithmetic
/// (Copyright 2024 Google LLC; see THIRD_PARTY_NOTICES.txt). Color-space conversion belongs to
/// the filter graph. Large deviations use the placed rescaling path; small kernels remain gated.
/// </remarks>
internal static class SvgGaussianBlur
{
    /// <summary>Rescales original image bounds progressively, blurs the reduced grid and restores its shader samples.</summary>
    /// <remarks>All temporary frames are budget-owned and released on every exit. Source and destination are borrowed;
    /// work is admitted before each pixel phase, and cancellation is checked per row. Color transfer is deferred to sampling.</remarks>
    internal static long ApplyRescaled(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height,
        SvgGaussianDeviation deviation, SvgPaintBounds sourceBounds, bool inputLinear, SvgFilterColorSpace space,
        Span<SvgShaderPaint> samples, RenderBudget budget, long availableWork, CancellationToken token)
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
            long clearWork = destination.Length + samples.Length;
            if (clearWork > availableWork) throw new SvgException(SvgErrorCode.LimitExceeded, "Empty rescaled output exceeds the work limit.");
            destination.Clear(); samples.Clear(); token.ThrowIfCancellationRequested();
            return clearWork;
        }
        float centerX = xSteps == 0 ? 0 : .5f * (srcLeft + srcLeft + srcWidth);
        float centerY = ySteps == 0 ? 0 : .5f * (srcTop + srcTop + srcHeight);
        SvgPaintBounds logical = new(srcLeft, srcTop, srcLeft + srcWidth, srcTop + srcHeight);
        SvgConverter.RasterizedSurface current = default;
        long work = 0;
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
                Charge((long)lowWidth * lowHeight);
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
                            SvgShaderPaint sample = first ? Sample(source, width, height, srcLeft, srcTop, srcWidth, srcHeight, sampleX, sampleY)
                                : Sample(current.Pixels, current.Width, current.Height, 0, 0, current.Width, current.Height, sampleX, sampleY);
                            if (first && !inputLinear && space == SvgFilterColorSpace.LinearRgb) sample = SvgFilterColorTransfer.Convert(sample, SvgFilterColorSpace.LinearRgb);
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
            Charge((long)current.Width * current.Height);
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
            work += Apply(padded.Pixels, blurred.Pixels, padded.Width, padded.Height,
                new(Math.Min(sigmaX / scaleBackX, 135), Math.Min(sigmaY / scaleBackY, 135)), budget, availableWork - work, token);
            Charge((long)width * height);
            destination.Clear();
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    float sampleX = MathF.FusedMultiplyAdd(x + .5f, 1 / scaleBackX, -offsetX / scaleBackX - outputLeft);
                    float sampleY = MathF.FusedMultiplyAdd(y + .5f, 1 / scaleBackY, -offsetY / scaleBackY - outputTop);
                    SvgShaderPaint sample = Sample(blurred.Pixels, blurred.Width, blurred.Height, 0, 0, blurred.Width, blurred.Height, sampleX, sampleY);
                    if (space == SvgFilterColorSpace.LinearRgb) sample = SvgFilterColorTransfer.Convert(sample, SvgFilterColorSpace.Srgb);
                    if (!samples.IsEmpty) samples[y * width + x] = sample;
                    sample.Blend(destination.Slice((y * width + x) * 4, 4), 255);
                }
            }
            return work;

        }
        finally { if (current.Pixels is not null) current.Dispose(); }

        void Charge(long amount)
        {
            if (amount > availableWork - work) throw new SvgException(SvgErrorCode.LimitExceeded, "Rescaled blur exceeds its work limit.");
            work += amount;
        }
        SvgConverter.RasterizedSurface Allocate(int w, int h)
        {
            token.ThrowIfCancellationRequested();
            long pixels = (long)w * h;
            if (w <= 0 || h <= 0 || pixels > int.MaxValue / 4) throw new SvgException(SvgErrorCode.LimitExceeded, "Rescaled blur exceeds contiguous image storage.");
            int bytes = (int)pixels * 4;
            RenderBudget.BudgetReservation owner = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<byte>(bytes), SvgMemoryCategory.Surface);
            try { return new(w, h, new byte[bytes], owner, 0); }
            catch { owner.Dispose(); throw; }
        }
        static int Steps(float scale)
        {
            int steps = 0;float inverse = MathF.Ceiling(1 / scale);
            while ((1 << steps) < inverse) steps++;
            if (steps > 0 && scale * (1 << (steps - 1)) >= (steps == 1 ? .999f : .9f)) steps--;
            return steps;
        }
        static SvgShaderPaint Sample(ReadOnlySpan<byte> data, int w, int h, int ox, int oy, int sw, int sh, float x, float y)
        {
            float px = x - .5f, py = y - .5f;
            int left = (int)MathF.Floor(px), top = (int)MathF.Floor(py);
            float fx = px - left, fy = py - top;
            SvgShaderPaint a = Pixel(data, w, h, ox, oy, sw, sh, left, top), b = Pixel(data, w, h, ox, oy, sw, sh, left + 1, top);
            SvgShaderPaint c = Pixel(data, w, h, ox, oy, sw, sh, left, top + 1), d = Pixel(data, w, h, ox, oy, sw, sh, left + 1, top + 1);
            float wa = (1 - fx) * (1 - fy), wb = fx * (1 - fy), wc = (1 - fx) * fy, wd = fx * fy;
            return new(Weighted(a.Red, b.Red, c.Red, d.Red, wa, wb, wc, wd), Weighted(a.Green, b.Green, c.Green, d.Green, wa, wb, wc, wd),
                Weighted(a.Blue, b.Blue, c.Blue, d.Blue, wa, wb, wc, wd), Weighted(a.Alpha, b.Alpha, c.Alpha, d.Alpha, wa, wb, wc, wd));
            static float Weighted(float a, float b, float c, float d, float wa, float wb, float wc, float wd) => ((a * wa + b * wb) + c * wc) + d * wd;
            static SvgShaderPaint Pixel(ReadOnlySpan<byte> data, int w, int h, int ox, int oy, int sw, int sh, int x, int y)
            {
                if ((uint)x >= (uint)sw || (uint)y >= (uint)sh || (uint)(x + ox) >= (uint)w || (uint)(y + oy) >= (uint)h) return default;
                int offset = ((y + oy) * w + x + ox) * 4;
                const float scale = 1f / 255f;
                return new(data[offset] * scale, data[offset + 1] * scale, data[offset + 2] * scale, data[offset + 3] * scale);
            }
        }
    }

    internal static long Apply(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height,
        SvgGaussianDeviation deviation, RenderBudget budget, long availableWork, CancellationToken token)
        => Apply(source, destination, width, height, deviation, budget, availableWork, token, null);

    /// <summary>The optional observer runs after each complete scanline; it borrows no pixels or owners.</summary>
    internal static long Apply(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height,
        SvgGaussianDeviation deviation, RenderBudget budget, long availableWork, CancellationToken token,
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
        long work = checked(AxisWork(horizontal, width, height) + AxisWork(vertical, height, width));
        if (horizontal.Identity && vertical.Identity) work = bytes;
        if (work > availableWork) throw new SvgException(SvgErrorCode.LimitExceeded, "Gaussian blur exceeds the configured work limit.");
        if (horizontal.Identity && vertical.Identity)
        {
            source.CopyTo(destination);
            token.ThrowIfCancellationRequested();
            return work;
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
        return work;

        static BoxKernel Kernel(float sigma)
        {
            if (sigma <= 0.03f) return default;
            if (sigma < 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Small Gaussian filter kernels are not implemented.");
            if (sigma > 135) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gaussian filter rescaling requires the placed image path.");
            int window = (int)MathF.Floor(sigma * 3f * MathF.Sqrt(2f * MathF.PI) / 4f + 0.5f);
            int third = (window & 1) == 0 ? window + 1 : window;
            uint divisor = checked((uint)(window * window * third));
            uint factor = checked((uint)Math.Round(4294967296d / divisor, MidpointRounding.AwayFromZero));
            return new(window, third, (2 * (window - 1) + third - 1) / 2, factor, (divisor + 1) / 2);
        }

        static long AxisWork(BoxKernel kernel, int extent, int lines)
            => kernel.Identity ? 0 : checked(((long)extent + kernel.Border + kernel.RingLength) * lines * 4);

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

    private readonly record struct BoxKernel(int Window, int ThirdWindow, int Border, uint Factor, uint Half)
    {
        internal bool Identity => Window == 0;
        internal int RingLength => Identity ? 0 : 2 * (Window - 1) + ThirdWindow - 1;
    }
}
