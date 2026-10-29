using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgStaticCssImageTests
{
    [Theory]
    [InlineData("linear", "x1='4' y1='5' x2='84' y2='80'", "repeat")]
    [InlineData("linear", "x1='4' y1='5' x2='84' y2='80'", "reflect")]
    [InlineData("radial", "cx='44' cy='44' r='40'", "repeat")]
    [InlineData("radial", "cx='44' cy='44' r='40'", "reflect")]
    public void Periodic_stop_padding_releases_ramp_owners_after_denial_cancellation_and_retry(string kind, string coordinates, string spread)
    {
        System.Text.StringBuilder stops = new();
        for (int index = 0; index < 256; index++)
        {
            string offset = (.15 + .72 * index / 255).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            stops.Append("<stop offset='").Append(offset).Append("' stop-color='").Append(index % 2 == 0 ? "#17a9cd" : "#43805b").Append("' stop-opacity='.65'/>");
        }
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><defs><" + kind
            + "Gradient id='g' gradientUnits='userSpaceOnUse' " + coordinates + " spreadMethod='" + spread + "'>" + stops + "</" + kind
            + "Gradient></defs><g opacity='.65'><circle cx='44' cy='44' r='40' fill='url(#g)' fill-opacity='.7'/></g></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Theory]
    [InlineData("linear", "x1='0' y1='0' x2='.000001' y2='0'", "repeat")]
    [InlineData("linear", "x1='0' y1='0' x2='.000001' y2='0'", "reflect")]
    [InlineData("radial", "cx='0' cy='0' r='.000001'", "repeat")]
    [InlineData("radial", "cx='0' cy='0' r='.000001'", "reflect")]
    public void Periodic_factory_integration_releases_owners_after_denial_cancellation_and_retry(string kind, string coordinates, string spread)
    {
        System.Text.StringBuilder stops = new();
        for (int index = 0; index < 256; index++)
            stops.Append("<stop offset='.5' stop-color='").Append(index % 2 == 0 ? "#17a9cd" : "#43805b").Append("' stop-opacity='.37'/>");
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><defs><" + kind
            + "Gradient id='g' gradientUnits='userSpaceOnUse' " + coordinates + " spreadMethod='" + spread + "'>" + stops + "</" + kind
            + "Gradient></defs><g opacity='.65'><circle cx='44' cy='44' r='40' fill='url(#g)' fill-opacity='.7'/></g></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_viewport_clip_releases_owners_around_opacity_and_filter_output(bool filtered)
    {
        string effect = filtered ? " filter='url(#f)'" : "";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><defs>"
            + "<linearGradient id='g'><stop stop-color='#17a9cd'/><stop offset='1' stop-color='#43805b'/></linearGradient>"
            + "<clipPath id='c'><rect x='5.25' y='3.75' width='70.5' height='80.25'/></clipPath>"
            + "<filter id='f' filterUnits='userSpaceOnUse' width='96' height='96'><feOffset dx='1' dy='1'/></filter></defs>"
            + "<svg x='1' y='2' width='90' height='90' viewBox='0 0 96 96'><g clip-path='url(#c)'><g opacity='.65'" + effect
            + "><circle cx='44' cy='44' r='40' fill='url(#g)'/></g></g></svg></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Theory]
    [InlineData("linear", "x1='16' y1='16' x2='16' y2='16'")]
    [InlineData("radial", "cx='16' cy='16' r='0'")]
    public void Constant_factory_paints_release_polygon_owners_after_denial_cancellation_and_retry(string kind, string coordinates)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><defs><" + kind
            + "Gradient id='g' gradientUnits='userSpaceOnUse' " + coordinates
            + "><stop stop-color='#17a9cd' stop-opacity='.37'/><stop offset='.6' stop-color='#43805b'/><stop offset='1' stop-color='#244a77'/></" + kind
            + "Gradient></defs><circle cx='44' cy='44' r='40' fill='url(#g)' fill-opacity='.7'/></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Fact]
    public void Radial_stroke_bands_keep_stroke_opacity_separate_from_pattern_fill()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + "<pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='#507e92'/></pattern>"
            + "<radialGradient id='g'><stop stop-color='#17a9cd'/><stop offset='1' stop-color='#244a77'/></radialGradient></defs>"
            + "<rect x='6' y='6' width='20' height='20' fill='url(#p)' fill-opacity='.4' stroke='url(#g)' stroke-width='4' stroke-opacity='.7'/></svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException();
        SvgGeometry[] strokes = root.Geometries.Where(geometry => geometry.Kind == SvgGeometryKind.RadialGradient).ToArray();
        Assert.NotEmpty(strokes);
        foreach (SvgGeometry geometry in strokes)
        {
            SvgRecordedPaintValue paint = storage.RecordedPaintValues[geometry.RecordedPaint.Identity - 1];
            Assert.True(paint.IsStroke);
            Assert.Equal(179 / 255f, paint.Alpha);
        }
    }

    [Theory]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.996' fy='16'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.996' fy='16'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.996' fy='16'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35' fr='.1'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35' fr='.1'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35' fr='.1'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='14' fy='15' fr='1'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='14' fy='15' fr='1'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='14' fy='15' fr='1'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(2 0 0 2 0 0)", "cx='.5' cy='.5' r='.5' fx='.5' fy='.5' fr='.1'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(2 0 0 2 0 0)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='16' fy='16' fr='2'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35' fr='.1' gradientTransform='translate(.1 -.1)'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='14' fy='15' fr='1' gradientTransform='translate(4 0)'")]
    [InlineData("<rect x='4' y='6' width='24' height='20' rx='4' ry='3' fill='url(#g)'/>", "matrix(-2 0 0 -2 96 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='14' fy='15' fr='1' gradientTransform='scale(2)'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(2 0 0 2 0 0)", "cx='.4' cy='.55' r='.4' fx='.25' fy='.35' fr='.1' gradientTransform='rotate(90)'")]
    [InlineData("<circle cx='16' cy='16' r='10' fill='url(#g)'/>", "matrix(-2 0 0 2 96 0)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='26' fy='16' fr='0'")]
    [InlineData("<ellipse cx='16' cy='16' rx='12' ry='10' fill='url(#g)'/>", "matrix(2 0 0 -2 0 96)", "gradientUnits='userSpaceOnUse' cx='16' cy='16' r='10' fx='25.999' fy='16' fr='0'")]
    public void Reflected_radial_sources_release_owners_after_denial_cancellation_and_retry(string shape, string placement, string coordinates)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'>"
            + "<defs><radialGradient id='g' " + coordinates + "><stop stop-color='#c04080'/><stop offset='1' stop-color='#244a77'/></radialGradient></defs>"
            + "<g transform='" + placement + "'>" + shape + "</g></svg>");
        AssertResourceLifecycle(input);
        AssertActiveRasterLifecycle(input);
    }

    [Theory]
    [InlineData("<stop stop-color='#c04080' stop-opacity='.65'/>")]
    [InlineData("<stop stop-color='#c04080' stop-opacity='.65'/><stop offset='.5' stop-color='#c04080' stop-opacity='.65'/><stop offset='1' stop-color='#c04080' stop-opacity='.65'/>")]
    public void Constant_radial_cone_masks_release_owners_after_denial_cancellation_and_retry(string stops)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='32' cy='32' r='20' fx='52' fy='32' spreadMethod='repeat'>" + stops + "</radialGradient></defs><circle cx='44' cy='44' r='40' fill='url(#g)'/></svg>");
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
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
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
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterBinding binding = arena.BindingAt(scene.Commands[0].MaskContentIndex);
            Assert.Equal(new SvgPaintBounds(14.25f, 15.5f, 110.75f, 86.75f), binding.ObjectBounds);
            long retained = budget.Snapshot().CurrentBytes;
            byte[] expected;
            using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            { expected = output.Pixels.ToArray(); }
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static void AssertResourceLifecycle(byte[] input)
    {
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            expected = output.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        RenderBudget denied = new RenderBudget(new HostMemoryBudget(Math.Max(1, measured.Snapshot().PeakBytes - 1)));
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
    public void Affine_coverage_cancellation_releases_temporary_owners()
        => AssertActiveRasterLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "affine-image-maps", "affine-varied-placement.svg")));

    [Theory]
    [InlineData("affine-filter-regions")]
    [InlineData("effects-marker-sheet")]
    public void Affine_filter_cancellation_releases_temporary_owners(string identity)
        => AssertActiveRasterLifecycle(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "effects-marker-sheet", identity + ".svg")));

    private static void AssertActiveRasterLifecycle(byte[] input)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            { expected = output.Pixels.ToArray(); }
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
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
