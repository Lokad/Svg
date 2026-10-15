using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStaticCssImageTests
{
    [Theory]
    [InlineData("gray-alpha-solid")]
    [InlineData("late-nested-cascade")]
    [InlineData("rgba-fractional-meet")]
    [InlineData("css-image-card")]
    [InlineData("varied-integral-phase")]
    [InlineData("varied-clipped-opacity")]
    public void Image_and_nested_styles_preserve_input_and_release_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "static-css-images", identity + ".svg")));

    [Theory]
    [InlineData("uniform-fractional-placement")]
    [InlineData("affine-varied-placement")]
    [InlineData("image-resource-scope")]
    [InlineData("affine-image-map-sheet")]
    public void Affine_images_preserve_input_and_release_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "affine-image-maps", identity + ".svg")));

    private static void AssertResourceLifecycle(byte[] input)
    {
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new(8_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            expected = output.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        RenderBudget denied = new(Math.Max(1, measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None);
            using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        {
            long retained = measured.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, cancellation.Token));
            Assert.Equal(retained, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Affine_coverage_cancellation_and_raster_work_denial_release_temporary_owners()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "affine-image-maps", "affine-varied-placement.svg"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            byte[] expected;
            long work;
            using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            { expected = output.Pixels.ToArray(); work = output.WorkUnits; }
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            bool reached = false;
            using (SvgAnalyticPaintProbe.Arm((phase, _) =>
            {
                if (phase == SvgAnalyticPaintPhase.BeforeReplay) { reached = true; cancellation.Cancel(); }
            }))
            {
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, cancellation.Token);
                }).CancellationToken);
            }
            Assert.True(reached);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            SvgRenderLimits limits = SvgRenderLimits.Default;
            SvgRenderOptions bounded = new(null, 1, SvgColor.Transparent, new(limits.MaximumAccountedBytes, limits.MaximumInputBytes,
                limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes,
                limits.MaximumPathCommands, checked((int)work - 1), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, bounded, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decoded_image_owners_release_after_placement_or_later_document_failure(bool laterFailure)
    {
        string original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "static-css-images", "rgba-fractional-meet.svg"));
        string invalid = laterFailure
            ? original.Replace("</svg>", "<rect width='invalid'/></svg>", StringComparison.Ordinal)
            : original.Replace("xMidYMid meet", "invalid", StringComparison.Ordinal);
        byte[] input = System.Text.Encoding.UTF8.GetBytes(invalid);
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
