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
/// the filter graph. Small Gaussian kernels and large-blur rescaling remain separate admissions.
/// </remarks>
internal static class SvgGaussianBlur
{
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
            if (sigma >= 135) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Gaussian filter rescaling is not implemented.");
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
