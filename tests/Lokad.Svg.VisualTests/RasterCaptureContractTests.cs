using Lokad.Svg.Testing;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Contract")]
public sealed class RasterCaptureContractTests
{
    private static ReadOnlyMemory<byte> Input => "<svg xmlns='http://www.w3.org/2000/svg' width='7' height='5'><path d='M.5 4.5Q3.5 .25 6.5 4.5Z' fill='red' fill-opacity='.37'/></svg>"u8.ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(255)]
    public void Borrowed_capture_matches_every_canonical_png_lane_and_retains_its_owner(byte backgroundAlpha)
    {
        SvgRenderOptions options = new(null, 1.5, new SvgColor(0, 0, 255, backgroundAlpha), SvgRenderLimits.Default);
        RgbaImage png = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(Input, options, CancellationToken.None).ToArray(), 256);
        using TestMemoryBudget host = new(2_000_000);
        using (SvgConverter.RasterizedConversion conversion = SvgConverter.PrepareRasterization(Input, options.WithMemoryBudget(host), CancellationToken.None))
        {
            Assert.True(host.Reserved > 0);
            Assert.Equal(1, host.ActiveScopeCount);
            Assert.Equal(png.Width, conversion.Surface.Width);
            Assert.Equal(png.Height, conversion.Surface.Height);
            byte[] canonical = conversion.Surface.Pixels.ToArray();
            // Encoding clears only zero-alpha RGB. Every other RGBA byte must
            // agree exactly; never compare premultiplied values or widen tolerance.
            for (int offset = 0; offset < canonical.Length; offset += 4)
                if (canonical[offset + 3] == 0) canonical.AsSpan(offset, 3).Clear();
            Assert.Equal(png.Pixels, canonical);
        }
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, host.ActiveScopeCount);
        Assert.False(host.Disposed);
    }

    [Fact]
    public void Canceled_capture_preserves_the_original_token_and_borrowed_root()
    {
        using TestMemoryBudget host = new(2_000_000);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgConverter.PrepareRasterization(Input, SvgRenderOptions.Default.WithMemoryBudget(host), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, host.ActiveScopeCount);
        Assert.False(host.Disposed);
    }

    [Fact]
    public void Refused_capture_releases_partial_preparation_and_allows_immediate_retry()
    {
        using TestMemoryBudget host = new(1);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.PrepareRasterization(Input, options, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, host.ActiveScopeCount);
        host.Limit = 2_000_000;
        using (SvgConverter.RasterizedConversion conversion = SvgConverter.PrepareRasterization(Input, options, CancellationToken.None))
            Assert.True(host.Reserved >= conversion.Surface.Pixels.Length);
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
    }
}
