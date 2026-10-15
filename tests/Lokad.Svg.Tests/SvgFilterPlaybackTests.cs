using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterPlaybackTests
{
    [Fact]
    public void Stored_merge_inputs_preserve_native_packed_image_composition()
    {
        byte[] source = [47, 86, 115, 159];
        Assert.Equal(new byte[] { 64, 118, 158, 219 }, Apply("<feMerge><feMergeNode in='SourceGraphic'/><feMergeNode in='SourceGraphic'/></feMerge>", source, 1));
        Assert.Equal(new byte[] { 47, 86, 115, 159 }, source);
    }

    [Fact]
    public void Source_layer_bounds_match_the_native_two_blur_hint_and_release_temporary_owners()
    {
        byte[] input = Document("<defs><filter id='f'><feGaussianBlur stdDeviation='1.125'/><feGaussianBlur stdDeviation='1.125'/></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(144, 108), new(-100, -100, 200, 200));
            SvgPaintBounds source = new(7.25f, 19.5f, 61.75f, 84.75f);
            RenderBudget budget = new(1_000_000);
            SvgPaintBounds hint = SvgFilterPlayback.SourceLayerBounds(resource, new(1, 1), context, source, budget, new(1_000), CancellationToken.None);
            // The retained native command trace records [0,12,69,92].
            Assert.Equal((0d, 12d, 69d, 92d), (Math.Floor(hint.Left), Math.Floor(hint.Top), Math.Ceiling(hint.Right), Math.Ceiling(hint.Bottom)));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFilterPlayback.SourceLayerBounds(resource,
                new(1, 1), context, source, budget, new(1), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgFilterPlayback.SourceLayerBounds(resource,
                new(1, 1), context, source, budget, new(1_000), cancellation.Token)).CancellationToken);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Primitive_color_spaces_inherit_independently_and_keep_their_declared_override()
    {
        byte[] input = Document("<defs><filter id='f' color-interpolation-filters='sRGB'><feGaussianBlur stdDeviation='1' color-interpolation-filters='linearRGB'/><feOffset/><feMerge color-interpolation-filters='linearRGB'><feMergeNode in='SourceGraphic'/></feMerge></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            Assert.Equal(SvgFilterColorSpace.Srgb, resource.ColorSpace);
            Assert.Equal(new SvgFilterColorSpace?[] { SvgFilterColorSpace.LinearRgb, SvgFilterColorSpace.Srgb, SvgFilterColorSpace.LinearRgb },
                resource.Primitives.Items.Select(p => p.ColorSpace));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Required_source_bounds_preserve_both_branches_and_the_displaced_blur_halo()
    {
        byte[] input = Document("<defs><filter id='f' filterUnits='userSpaceOnUse' x='-100' y='-100' width='200' height='200'>"
            + "<feGaussianBlur stdDeviation='1' result='b'/><feOffset in='b' dx='5' dy='-3' result='m'/>"
            + "<feMerge><feMergeNode in='SourceGraphic'/><feMergeNode in='m'/></feMerge></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(200, 200), new(-100, -100, 100, 100));
            RenderBudget budget = new(1_000_000);
            // The direct branch needs [10,20,30,40]. The shifted blur needs
            // [5,23,25,43] expanded by its native three-sigma radius of 3.
            Assert.Equal(new SvgPaintBounds(2, 20, 30, 46), SvgFilterPlayback.RequiredSourceBounds(resource,
                new(1, 1), context, new(10, 20, 30, 40), budget, new(1_000), CancellationToken.None));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFilterPlayback.RequiredSourceBounds(resource,
                new(1, 1), context, new(10, 20, 30, 40), budget, new(1), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgFilterPlayback.RequiredSourceBounds(resource,
                new(1, 1), context, new(10, 20, 30, 40), budget, new(1_000), cancellation.Token)).CancellationToken);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rescaled_blur_keeps_offscreen_intermediate_pixels_needed_by_a_later_offset()
    {
        const int width = 128, height = 96;
        RenderBudget parserBudget = new(1_000_000);
        byte[] input = Document("<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='128' height='96'>"
            + "<feGaussianBlur stdDeviation='144'/><feOffset dx='-64'/></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parserBudget, CancellationToken.None);
        SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
        SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
        byte[] source = new byte[width * height * 4];
        for (int pixel = 0; pixel < width * height; pixel++) source[pixel * 4 + 2] = source[pixel * 4 + 3] = 255;
        SvgFilterDeviceMap map = new(1, 1)
        {
            Regions = new(SvgAffineTransform.FromScaleOffset(1, 1, 0, 0), new(width, height), new(0, 0, width, height))
            {
                SourceBounds = new(0, 0, width, height),
                RasterPlacement = new(new(0, 0), new(32, height), new(0, 0, 32, height))
            }
        };
        RenderBudget rasterBudget = new(1_000_000);
        using (SvgFilterResult result = SvgFilterPlayback.Apply(source, width, height, resource, map, rasterBudget, long.MaxValue, CancellationToken.None))
            Assert.True(result.Pixels[(48 * width) * 4 + 3] > 0);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Extreme_finite_gaussian_deviations_use_the_bounded_native_device_cap()
    {
        byte[] capped = Document("<defs><filter id='f'><feGaussianBlur stdDeviation='532'/></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        byte[] extreme = Document("<defs><filter id='f'><feGaussianBlur stdDeviation='1e300'/></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        Assert.Equal(SvgConverter.ConvertToPng(capped, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(extreme, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("filter='url(#f)'", "")]
    [InlineData("class='filtered'", "<style>.filtered { filter:url(#f); }</style>")]
    [InlineData("style='filter:url(#f) !important;filter:none'", "")]
    public void Group_sources_compose_overlapping_children_before_filter_and_outer_opacity(string attributes, string css)
    {
        byte[] input = Document(css + "<g " + attributes + " opacity='.5'><rect width='4' height='4' fill='red'/><rect width='4' height='4' fill='blue'/></g><defs><filter id='f'><feGaussianBlur stdDeviation='0'/></filter></defs>");
        SvgRenderOptions options = new(null, 1, new(255, 255, 255, 255), SvgRenderLimits.Default);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RenderBudget budget = new(1_000_000);
        DecodedSolidImage image = PngImageDecoder.DecodeSolidColor(png, options.Limits, budget, new ParserWorkCounter(options.Limits.MaximumWorkUnits), CancellationToken.None);
        Assert.Equal(new SvgColor(127, 127, 255, 255), image.Color);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Hidden_and_empty_groups_do_not_activate_dormant_filter_failures()
    {
        byte[] plain = Document("<rect width='4' height='4' fill='blue'/>");
        byte[] withGroups = Document("<g display='none' filter='url(#missing)'><rect width='4' height='4'/></g><g filter='url(#missing)'/><rect width='4' height='4' fill='blue'/>");
        Assert.Equal(SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(withGroups, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<g filter='url(#f)' clip-path='url(#c)'><rect width='4' height='4'/></g>")]
    [InlineData("<g filter='url(#f)' mask='url(#m)'><rect width='4' height='4'/></g>")]
    public void Pending_filter_group_combinations_reject_without_output_and_release_parser_owners(string content)
    {
        byte[] input = Document("<defs><filter id='f'><feGaussianBlur stdDeviation='3'/></filter></defs>" + content);
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using MemoryStream stream = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Nested_filter_groups_keep_unfiltered_object_bounds_and_release_source_owners()
    {
        // Retains the original nested-source rejection input as positive acceptance.
        byte[] input = Document("<defs><filter id='f'><feGaussianBlur stdDeviation='3'/></filter></defs>"
            + "<g filter='url(#f)'><rect width='4' height='4' filter='url(#f)'/></g>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgDrawCommand[] filters = scene.Commands.Where(c => c.Kind == SvgDrawCommandKind.BeginGroup && c.RegionKind == SvgLayerRegionKind.Filter).ToArray();
            Assert.Equal(2, filters.Length);
            foreach (SvgDrawCommand command in filters)
                Assert.Equal(new SvgPaintBounds(0, 0, 4, 4), arena.BindingAt(command.MaskContentIndex).ObjectBounds);
            using SvgConverter.RasterizedSurface result = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(result.Pixels, value => value != 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Outer_integer_offset_preserves_the_child_blur_outside_its_geometry_bounds()
    {
        const string shape = "<rect x='20.25' y='24.5' width='34.75' height='29.25' fill='#428365' fill-opacity='.7' filter='url(#inner)'/>";
        byte[] expected = Input("<g transform='translate(13 7)'>" + shape + "</g>");
        byte[] nested = Input("<g filter='url(#outer)'>" + shape + "</g>");
        Assert.Equal(SvgConverter.ConvertToPng(expected, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(nested, SvgRenderOptions.Default, CancellationToken.None).ToArray());

        static byte[] Input(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='108'><defs>"
            + "<filter id='inner' filterUnits='userSpaceOnUse' x='0' y='0' width='128' height='108' color-interpolation-filters='sRGB'><feGaussianBlur stdDeviation='2'/></filter>"
            + "<filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='128' height='108' color-interpolation-filters='sRGB'><feOffset dx='13' dy='7'/></filter>"
            + "</defs>" + content + "</svg>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_forward_binding_and_named_results_apply_element_opacity_after_filter(bool important)
    {
        string style = important ? "style='filter:url(#f) !important;filter:none;opacity:.5'" : "filter='url(#f)' opacity='.5'";
        byte[] input = Document("<rect width='4' height='4' fill='red' " + style + "/><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='4' height='4'><feGaussianBlur stdDeviation='0' result='a'/><feOffset in='a' dx='0' dy='0'/></filter></defs>");
        SvgRenderOptions options = new(null, 1, new(255, 255, 255, 255), SvgRenderLimits.Default);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RenderBudget budget = new(1_000_000);
        DecodedSolidImage image = PngImageDecoder.DecodeSolidColor(png, options.Limits, budget, new ParserWorkCounter(options.Limits.MaximumWorkUnits), CancellationToken.None);
        Assert.Equal(new SvgColor(255, 127, 127, 255), image.Color);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<feMorphology radius='5'/>")]
    [InlineData("<feGaussianBlur stdDeviation='bad'/>")]
    [InlineData("<feGaussianBlur in='later'/><feOffset result='later'/>")]
    public void Dormant_filter_errors_do_not_change_unfiltered_drawing(string primitives)
    {
        byte[] plain = Document("<rect width='4' height='4'/>");
        byte[] withDefinition = Document("<defs><filter id='f'>" + primitives + "</filter></defs><rect width='4' height='4'/>");
        Assert.Equal(SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(withDefinition, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<filter id='f'><feMorphology/></filter>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<filter id='f'><feGaussianBlur stdDeviation='-1'/></filter>", SvgErrorCode.MalformedInput)]
    [InlineData("<filter id='f'><feGaussianBlur in='later'/><feOffset result='later'/></filter>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<filter id='other'><feGaussianBlur/></filter>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<filter id='f'/><filter id='f'/>", SvgErrorCode.MalformedInput)]
    [InlineData("<filter id='f' color-interpolation-filters='invalid'><feOffset/></filter>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<filter id='f'><feFlood color-interpolation-filters='invalid'/></filter>", SvgErrorCode.UnsupportedFeature)]
    public void Referenced_graph_errors_release_partial_parser_owners(string definition, SvgErrorCode code)
    {
        byte[] input = Document("<rect width='4' height='4' filter='url(#f)'/><defs>" + definition + "</defs>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Graph_limits_release_intermediates_preserve_source_and_allow_exact_retry(bool merge)
    {
        RenderBudget parserBudget = new(1_000_000);
        string graph = "<feDropShadow stdDeviation='3' dx='1' dy='1' flood-opacity='.5' result='a'/>"
            + (merge ? "<feMerge><feMergeNode in='a'/><feMergeNode in='SourceAlpha'/><feMergeNode in='SourceGraphic'/></feMerge>" : "");
        using SvgRoot root = SvgRootParser.Parse(Document("<defs><filter id='f'>" + graph + "</filter></defs><rect width='4' height='4' filter='url(#f)'/>"), SvgRenderLimits.Default, parserBudget, CancellationToken.None);
        SvgFilterArena arena = root.SceneStorage?.Filters ?? throw new InvalidOperationException();
        SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(root.Commands[0].MaskContentIndex).ResourceIndex);
        byte[] source = new byte[13 * 9 * 4];
        source[(4 * 13 + 6) * 4] = source[(4 * 13 + 6) * 4 + 3] = 255;
        byte[] before = (byte[])source.Clone();
        RenderBudget accepted = new(1_000_000);
        byte[] expected;
        long work;
        using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 13, 9, resource, new(1, 1), accepted, long.MaxValue, CancellationToken.None))
        { expected = (byte[])result.Pixels.Clone(); work = result.WorkUnits; }
        long peak = accepted.Snapshot().PeakBytes;
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        foreach (long allowedWork in new[] { 0, 1, work / 2, work - 1 })
        {
            RenderBudget budget = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFilterPlayback.Apply(source, 13, 9, resource, new(1, 1), budget, allowedWork, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        foreach (long allowedBytes in new[] { 1, 256, peak / 2, peak - 1 })
        {
            RenderBudget budget = new(allowedBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFilterPlayback.Apply(source, 13, 9, resource, new(1, 1), budget, work, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using (SvgFilterResult retry = SvgFilterPlayback.Apply(source, 13, 9, resource, new(1, 1), accepted, work, CancellationToken.None)) Assert.Equal(expected, retry.Pixels);
        Assert.Equal(before, source);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgFilterPlayback.Apply(source, 13, 9, resource, new(1, 1), accepted, work, cancellation.Token));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<feOffset dx='.5'/>")]
    [InlineData("<feGaussianBlur stdDeviation='.5'/>")]
    [InlineData("<feDropShadow stdDeviation='1e300'/>")]
    public void Primitive_pixel_admissions_preserve_output_and_explicit_boundaries(string primitive)
    {
        byte[] input = Document("<defs><filter id='f'>" + primitive + "</filter></defs><rect width='4' height='4' fill='red' filter='url(#f)'/>");
        using MemoryStream stream = new();
        if (primitive.Contains("1e300", StringComparison.Ordinal))
        {
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, stream.Length);
        }
        else
        {
            byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            Assert.True(SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None) > 0);
            Assert.Equal(expected, stream.ToArray());
        }
    }

    [Fact]
    public void Translucent_colored_linear_shadow_keeps_its_explicit_color_conversion_gate()
    {
        byte[] input = Document("<defs><filter id='f'><feDropShadow stdDeviation='3'/></filter></defs><rect width='4' height='4' fill='red' fill-opacity='.5' filter='url(#f)'/>");
        using MemoryStream stream = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Merge_nodes_preserve_authored_source_over_order(bool reverse)
    {
        string nodes = reverse ? "<feMergeNode in='SourceGraphic'/><feMergeNode in='a'/>" : "<feMergeNode in='a'/><feMergeNode in='SourceGraphic'/>";
        byte[] source = [128, 0, 0, 128, 0, 0, 128, 128];
        byte[] pixels = Apply("<feOffset dx='1' result='a'/><feMerge>" + nodes + "</feMerge>", source, 2);
        Assert.Equal(reverse ? new byte[] { 128, 0, 64, 192 } : [64, 0, 128, 192], pixels.AsSpan(4, 4).ToArray());
        Assert.Equal(new byte[] { 128, 0, 0, 128, 0, 0, 128, 128 }, source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("in='a'")]
    public void Missing_merge_input_borrows_the_result_before_merge(string input)
    {
        byte[] pixels = Apply("<feOffset in='SourceAlpha' result='a'/><feMerge><feMergeNode in='SourceGraphic'/><feMergeNode " + input + "/></feMerge>", [0, 0, 128, 128], 1);
        Assert.Equal(new byte[] { 0, 0, 64, 192 }, pixels);
    }

    [Theory]
    [InlineData("<feMerge/>")]
    [InlineData("<feMerge></feMerge>")]
    public void Empty_merge_produces_transparent_pixels(string primitive)
        => Assert.Equal(new byte[4], Apply(primitive, [100, 40, 20, 128], 1));

    [Fact]
    public void Single_merge_input_preserves_its_srgb_bytes_without_a_color_round_trip()
    {
        byte[] source = [20, 41, 63, 128];
        Assert.Equal(source, Apply("<feMerge><feMergeNode/></feMerge>", source, 1));
    }

    [Theory]
    [InlineData("<feMerge><feMergeNode in='later'/></feMerge><feOffset result='later'/>")]
    [InlineData("<feMerge result='self'><feMergeNode in='self'/></feMerge>")]
    [InlineData("<feMerge><feMergeNode dx='1'/></feMerge>")]
    [InlineData("<feMerge><g><feMergeNode/></g></feMerge>")]
    public void Invalid_merge_graphs_remain_dormant_until_used_and_release_owners(string graph)
    {
        byte[] plain = Document("<rect width='4' height='4'/>");
        byte[] dormant = Document("<defs><filter id='f'>" + graph + "</filter></defs><rect width='4' height='4'/>");
        Assert.Equal(SvgConverter.ConvertToPng(plain, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(dormant, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        byte[] active = Document("<defs><filter id='f'>" + graph + "</filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(active, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using MemoryStream output = new();
        Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(active, output, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Merge_input_growth_has_a_bounded_limit_and_releases_partial_storage()
    {
        string nodes = string.Concat(Enumerable.Repeat("<feMergeNode in='SourceGraphic'/>", 257));
        byte[] input = Document("<defs><filter id='f'><feMerge>" + nodes + "</feMerge></filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Offset_source_cancellation_releases_the_partial_bitmap_and_allows_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='800' height='700'><defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='800' height='700'><feOffset dx='315' dy='450' result='a'/><feMerge><feMergeNode in='SourceGraphic'/><feMergeNode in='a'/></feMerge></filter></defs><g filter='url(#f)' transform='translate(0 0)'><ellipse cx='48' cy='48' rx='45' ry='28' fill='none' stroke='blue' stroke-width='2'/></g></svg>");
        RenderBudget budget = new(16_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            bool reached = false;
            using (SvgAnalyticPaintProbe.Arm((phase, count) => { reached = true; cancellation.Cancel(); }))
            {
                OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, cancellation.Token);
                });
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
            Assert.True(reached);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            long work;
            byte[] pixels;
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            {
                Assert.Contains(retry.Pixels, value => value != 0);
                work = retry.WorkUnits;
                pixels = retry.Pixels.ToArray();
            }
            foreach (long allowedWork in new[] { work / 2, work - 1 })
            {
                SvgRenderLimits limits = SvgRenderLimits.Default;
                limits = new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                    limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)allowedWork), limits.MaximumXmlDepth, limits.MaximumAttributesPerElement);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, new(null, 1, SvgColor.Transparent, limits), budget, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            }
            RenderBudget tight = new(budget.Snapshot().PeakBytes - retained - 1_024);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, tight, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface final = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(pixels, final.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Apply(string graph, byte[] source, int width)
    {
        RenderBudget parserBudget = new(1_000_000);
        byte[] input = Document("<defs><filter id='f' color-interpolation-filters='sRGB'>" + graph + "</filter></defs><rect width='4' height='4' filter='url(#f)'/>");
        byte[] pixels;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parserBudget, CancellationToken.None))
        {
            SvgFilterArena arena = scene.SceneStorage?.Filters ?? throw new InvalidOperationException();
            SvgFilterResource resource = arena.ResourceAt(arena.BindingAt(scene.Commands[0].MaskContentIndex).ResourceIndex);
            RenderBudget rasterBudget = new(1_000_000);
            using (SvgFilterResult result = SvgFilterPlayback.Apply(source, width, 1, resource, new(1, 1), rasterBudget, 1_000_000, CancellationToken.None)) pixels = result.Pixels.ToArray();
            Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parserBudget.Snapshot().CurrentBytes);
        return pixels;
    }

    private static byte[] Document(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'>" + content + "</svg>");
}
