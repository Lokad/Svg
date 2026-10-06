using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgBitmapLocalLineTests
{
    [Theory]
    [InlineData("bitmap-local-straight-clip.svg")]
    [InlineData("bitmap-local-compound-clip.svg")]
    [InlineData("float-polygon-viewbox.svg")]
    public async Task Six_outputs_agree_for_bitmap_local_lines_and_float_polygons(string source)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", source));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Fact]
    public void Narrowed_svg_clip_preserves_bitmap_origin_and_releases_denied_contexts()
    {
        SvgDevicePoint[] points = [new(27.731, 31.129), new(723.413, 84.617), new(689.291, 361.873), new(32.587, 337.429)];
        SvgAnalyticContourPreparation preparation = new(points, [4], [], false, null, SvgPolygonMapping.FiniteEdges, [], [], new(27, 31, 724, 362), 768, 384);
        // The SVG clip starts inside the owning bitmap. Its left/top cannot
        // replace the bitmap origin when preparing local line intersections.
        SvgAnalyticClipContext clip = new(new(279, 63, 510, 256), new(254, 0));
        RenderBudget budget = new(1_000_000);
        long setup;
        long storage;
        {
            using SvgAnalyticCoverage warm = SvgAnalyticCoverage.Prepare(ref preparation, 279, 510, 31, 362, new(279, 510), clip, SvgAnalyticPaintClip.Antialiased, 1_000_000, budget, out long _, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
        }
        {
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 279, 510, 31, 362, new(279, 510), clip, SvgAnalyticPaintClip.Antialiased, 1_000_000, budget, out setup, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            storage = budget.Snapshot().CurrentBytes;
            walker.PrepareRow(100, 1_000_000, out long _, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(300 - 279, pixel, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), SvgAnalyticClipCoverage.Full);
            Assert.Equal(255, pixel[3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 279, 510, 31, 362, new(279, 510), clip, SvgAnalyticPaintClip.Antialiased, setup - 1, budget, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Local coordinate mapping and fixed-edge translation must fit the remaining work.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget tight = new(storage - 1);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 279, 510, 31, 362, new(279, 510), clip, SvgAnalyticPaintClip.Antialiased, 1_000_000, tight, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Local clipping must release edge storage after a later reservation is denied.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            using SvgAnalyticCoverage canceled = SvgAnalyticCoverage.Prepare(ref preparation, 279, 510, 31, 362, new(279, 510), clip, SvgAnalyticPaintClip.Antialiased, 1_000_000, budget, out long _, out bool _, cancellation.Token);
            Assert.Fail("Cached local clipping must still observe the caller's cancellation.");
        }
        catch (OperationCanceledException exception) { Assert.Equal(cancellation.Token, exception.CancellationToken); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
