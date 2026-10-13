using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRotatedEllipseTests
{
    [Theory]
    [InlineData("rotated-fill")]
    [InlineData("rotated-stroke")]
    [InlineData("reflected-composed")]
    [InlineData("fractional-viewport")]
    [InlineData("ordered-group")]
    public void Composed_native_ovals_preserve_input_and_release_storage_after_limits_cancellation_and_retry(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "offset-merge-document", identity + ".svg"));
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(long.MaxValue);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        {
            Assert.Contains(scene.Geometries, geometry => geometry.AuthoredPath is {} source && !source.SceneMap.IsAxisAligned);
            byte[] pixels;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None)) pixels = surface.Pixels.ToArray();
            Assert.Contains(pixels, value => value != 0);
            long retained = measured.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, cancellation.Token);
            });
            Assert.Equal(retained, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(pixels, retry.Pixels);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        foreach (long limit in new[] { 1, measured.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(unchanged, input);
    }
}
