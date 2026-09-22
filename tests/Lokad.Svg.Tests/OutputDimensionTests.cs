using System.Buffers.Binary;

namespace Lokad.Svg.Tests;

public sealed class OutputDimensionTests
{
    [Theory]
    [InlineData(2, 1, 1, 1, false, false)]
    [InlineData(1, 2, 1, 1, false, false)]
    [InlineData(2, 1, 2, 3, false, false)]
    [InlineData(1, 2, 2, 3, false, false)]
    [InlineData(2, 1, 1, 1, true, false)]
    [InlineData(1, 2, 1, 1, true, false)]
    [InlineData(2, 1, 2, 3, true, false)]
    [InlineData(1, 2, 2, 3, true, false)]
    [InlineData(2, 1, 1, 2, false, true)]
    [InlineData(1, 2, 1, 2, false, true)]
    [InlineData(2, 2, 2, 4, false, true)]
    [InlineData(2, 1, 1, 2, true, true)]
    [InlineData(1, 2, 1, 2, true, true)]
    [InlineData(2, 2, 2, 4, true, true)]
    public async Task Dimension_caps_apply_to_scaled_output_in_all_six_api_forms(int width, int height, int scale, int maximumDimension, bool useViewport, bool succeeds)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(useViewport
            ? "<svg xmlns='http://www.w3.org/2000/svg' width='1000' height='1000'/>"
            : FormattableString.Invariant($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'/>"));
        SvgRenderLimits limits = new(200_000, 1_024, maximumDimension, 64, 1_024, 16, 16, 1_000);
        SvgRenderOptions options = new(useViewport ? new SvgViewport(width, height) : null, scale, SvgColor.Transparent, limits);

        foreach (OutputForm form in Enum.GetValues<OutputForm>())
        {
            if (succeeds)
            {
                ReadOnlyMemory<byte> png = await Convert(svg, options, form);
                Assert.Equal(width * scale, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
                Assert.Equal(height * scale, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
            }
            else
            {
                SvgException exception = await Assert.ThrowsAsync<SvgException>(() => Convert(svg, options, form));
                Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
                Assert.Contains("dimension", exception.Message);
            }
        }
    }

    [Theory]
    [InlineData(1.5, 1, 1)]
    [InlineData(1, 1.5, 1)]
    [InlineData(1, 1, 0.5)]
    [InlineData(double.MaxValue, 1, 2)]
    [InlineData(1, double.MaxValue, 2)]
    [InlineData(double.Epsilon, 1, 0.5)]
    public async Task Unsupported_output_dimensions_remain_distinct_from_resource_limits(double width, double height, double scale)
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        SvgRenderOptions options = new(new SvgViewport(width, height), scale, SvgColor.Transparent, SvgRenderLimits.Default);

        foreach (OutputForm form in Enum.GetValues<OutputForm>())
        {
            SvgException exception = await Assert.ThrowsAsync<SvgException>(() => Convert(svg, options, form));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        }
    }

    [Fact]
    public async Task Oversized_integral_dimensions_fail_before_integer_conversion()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray();
        SvgRenderLimits limits = new(200_000, 1_024, int.MaxValue, 64, 1_024, 16, 16, 1_000);
        SvgRenderOptions options = new(new SvgViewport((double)int.MaxValue + 1, 1), 1, SvgColor.Transparent, limits);

        foreach (OutputForm form in Enum.GetValues<OutputForm>())
        {
            SvgException exception = await Assert.ThrowsAsync<SvgException>(() => Convert(svg, options, form));
            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Contains("dimension", exception.Message);
        }
    }

    [Fact]
    public async Task Malformed_inputs_fail_identically_in_all_six_api_forms()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><rect>"u8.ToArray();

        foreach (OutputForm form in Enum.GetValues<OutputForm>())
        {
            SvgException exception = await Assert.ThrowsAsync<SvgException>(() => Convert(svg, SvgRenderOptions.Default, form));
            Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        }
    }

    private static async Task<ReadOnlyMemory<byte>> Convert(byte[] svg, SvgRenderOptions options, OutputForm form)
    {
        byte[] memoryDestination = new byte[1_024];
        using MemoryStream streamDestination = new();
        try
        {
            switch (form)
            {
                case OutputForm.Owned: return SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
                case OutputForm.OwnedAsync: return await SvgConverter.ConvertToPngAsync(svg, options, CancellationToken.None);
                case OutputForm.Memory:
                    int written = SvgConverter.ConvertToPng(svg, memoryDestination, options, CancellationToken.None);
                    return memoryDestination.AsMemory(0, written);
                case OutputForm.MemoryAsync:
                    int writtenAsync = await SvgConverter.ConvertToPngAsync(svg, memoryDestination, options, CancellationToken.None);
                    return memoryDestination.AsMemory(0, writtenAsync);
                case OutputForm.Stream:
                    SvgConverter.ConvertToPng(svg, streamDestination, options, CancellationToken.None);
                    return streamDestination.ToArray();
                case OutputForm.StreamAsync:
                    await SvgConverter.ConvertToPngAsync(svg, streamDestination, options, CancellationToken.None);
                    return streamDestination.ToArray();
                default: throw new ArgumentOutOfRangeException(nameof(form));
            }
        }
        catch (SvgException)
        {
            Assert.All(memoryDestination, value => Assert.Equal(0, value));
            Assert.Equal(0, streamDestination.Length);
            throw;
        }
    }

    private enum OutputForm { Owned, OwnedAsync, Memory, MemoryAsync, Stream, StreamAsync }
}
