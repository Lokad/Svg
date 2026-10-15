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

    [Theory]
    [InlineData("marker-overflow-cascade")]
    [InlineData("marker-length-angle-units")]
    public void Marker_units_and_cascade_release_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg")));

    [Fact]
    public void Image_filter_source_preserves_input_and_releases_owners_after_denial_and_retry()
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", "image-filter-source.svg")));

    [Theory]
    [InlineData("image-filter-clip")]
    [InlineData("vector-filter-clips")]
    public void Filter_output_clips_release_source_and_clip_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg")));

    [Theory]
    [InlineData("nested-filter-source")]
    [InlineData("nested-filter-halo")]
    public void Nested_filter_sources_release_intermediate_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg")));

    [Fact]
    public void Missing_output_clip_releases_the_decoded_image_and_does_not_publish_output()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", "image-filter-clip.svg"));
        byte[] input = System.Text.Encoding.UTF8.GetBytes(source.Replace("clip-path=\"url(#effectCircle)\"", "clip-path='url(#missing)'", StringComparison.Ordinal));
        RenderBudget budget = new(8_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("style='filter:url(#shadow)!important;filter:none'")]
    [InlineData("class='filtered-image'")]
    public void Image_filter_cascade_uses_the_same_bounded_source_as_the_attribute(string declaration)
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", "image-filter-source.svg"));
        byte[] input = System.Text.Encoding.UTF8.GetBytes(source.Replace("filter=\"url(#shadow)\"", declaration, StringComparison.Ordinal)
            .Replace("<defs>", "<style>.filtered-image{filter:url(#shadow)}</style><defs>", StringComparison.Ordinal));
        Assert.Equal(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(source), SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Image_filter_bounds_and_work_denial_preserve_the_prepared_source_for_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", "image-filter-source.svg"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterBinding binding = arena.BindingAt(scene.Commands[0].MaskContentIndex);
            Assert.Equal(new SvgPaintBounds(14.25f, 15.5f, 110.75f, 86.75f), binding.ObjectBounds);
            long retained = budget.Snapshot().CurrentBytes;
            byte[] expected;
            long work;
            using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            { expected = output.Pixels.ToArray(); work = output.WorkUnits; }
            foreach (long allowed in new[] { work / 2, work - 1 })
            {
                SvgRenderLimits limits = SvgRenderLimits.Default;
                SvgRenderOptions bounded = new(null, 1, SvgColor.Transparent, new(limits.MaximumAccountedBytes, limits.MaximumInputBytes,
                    limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands,
                    checked((int)allowed), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, bounded, budget, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            }
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

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
