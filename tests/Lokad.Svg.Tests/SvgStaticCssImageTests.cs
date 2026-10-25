using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStaticCssImageTests
{
    [Theory]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.996' fy='16'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.996' fy='16'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.996' fy='16'")]
    public void Reflected_radial_sources_release_owners_after_denial_cancellation_and_retry(string shape, string placement, string coordinates)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'>"
            + "<defs><radialGradient id='g' " + coordinates + "><stop stop-color='#c04080'/><stop offset='1' stop-color='#244a77'/></radialGradient></defs>"
            + "<g transform='" + placement + "'>" + shape + "</g></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Theory]
    [InlineData("<circle cx='16' cy='16' r='9' fill='url(#g)'/>")]
    [InlineData("<ellipse cx='16' cy='16' rx='11' ry='9' fill='url(#g)'/>")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>")]
    public void Source_bound_linear_fills_release_owners_after_denial_cancellation_and_retry(string shape)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'>"
            + "<defs><linearGradient id='g' gradientTransform='matrix(1 .2 .3 1 0 0)'><stop stop-color='#c04080'/><stop offset='1' stop-color='#244a77'/></linearGradient></defs>"
            + "<g transform='matrix(2 .4 .6 2 0 0)'>" + shape + "</g></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Theory]
    [InlineData("object-box-tile")]
    [InlineData("object-box-content")]
    [InlineData("object-box-affine-map")]
    public void Pattern_unit_replay_preserves_sources_and_releases_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "pattern-tile-sheet", identity + ".svg")));

    [Theory]
    [InlineData("image-tile")]
    [InlineData("filtered-tile")]
    public void Pattern_content_replay_releases_image_filter_owners_on_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "pattern-tile-sheet", identity + ".svg")));

    [Fact]
    public void Filtered_tile_capture_cancellation_and_work_denial_release_temporary_owners()
        => AssertActiveRasterLifecycle("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='64' height='64'><feGaussianBlur stdDeviation='1'/></filter><pattern id='p' width='64' height='64' patternUnits='userSpaceOnUse'><circle cx='32' cy='32' r='24' fill='#025bc7' filter='url(#f)'/></pattern></defs><rect width='128' height='128' fill='url(#p)'/></svg>"u8.ToArray());

    [Fact]
    public void Object_relative_tile_replay_cancellation_and_work_denial_release_temporary_owners()
        => AssertActiveRasterLifecycle("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><defs><pattern id='p' width='.5' height='.5' patternUnits='objectBoundingBox' patternContentUnits='objectBoundingBox'><circle cx='.33' cy='.33' r='.25' fill='#025bc7'/></pattern></defs><rect width='256' height='256' fill='url(#p)'/></svg>"u8.ToArray());

    [Theory]
    [InlineData("paint-recording", "pattern-tile-tile-fill-stroke-element")]
    [InlineData("pattern-vector-content", "group-opacity-promotion")]
    public void Pattern_opacity_replay_preserves_sources_and_releases_owners_after_denial_and_retry(string family, string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", family, identity + ".svg")));

    [Fact]
    public void Pattern_group_opacity_cancellation_and_work_denial_release_temporary_owners()
        => AssertActiveRasterLifecycle("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><defs><pattern id='p' width='256' height='256' patternUnits='userSpaceOnUse'><g opacity='.6'><rect x='16' y='16' width='96' height='96' fill='red'/><circle cx='128' cy='128' r='100' fill='#025bc7'/></g></pattern></defs><rect width='256' height='256' fill='url(#p)'/></svg>"u8.ToArray());

    [Theory]
    [InlineData("composition-sheet")]
    [InlineData("partial-mask-use")]
    public void Composition_replay_preserves_sources_and_releases_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "composition-sheet", identity + ".svg")));

    [Fact]
    public void Composition_replay_cancellation_and_work_denial_release_temporary_owners()
        => AssertActiveRasterLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "composition-sheet", "composition-sheet.svg")));

    [Theory]
    [InlineData("affine-filter-regions")]
    [InlineData("effects-marker-sheet")]
    public void Affine_filter_layers_preserve_sources_and_release_temporary_owners_after_denial_and_retry(string identity)
        => AssertResourceLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg")));

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

    [Theory]
    [InlineData("mixed-filter-spaces")]
    [InlineData("mixed-filter-branches")]
    [InlineData("mixed-filter-consumers")]
    public void Mixed_filter_graphs_preserve_sources_and_release_temporary_owners_after_denial_and_retry(string identity)
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
        => AssertActiveRasterLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "affine-image-maps", "affine-varied-placement.svg")));

    [Theory]
    [InlineData("affine-filter-regions")]
    [InlineData("effects-marker-sheet")]
    public void Affine_filter_cancellation_and_raster_work_denial_release_temporary_owners(string identity)
        => AssertActiveRasterLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg")));

    private static void AssertActiveRasterLifecycle(byte[] input)
    {
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
