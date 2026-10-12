using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterPlaybackTests
{
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
    [InlineData("<g filter='url(#f)'><rect width='4' height='4' filter='url(#f)'/></g>")]
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
    public void Referenced_graph_errors_release_partial_parser_owners(string definition, SvgErrorCode code)
    {
        byte[] input = Document("<rect width='4' height='4' filter='url(#f)'/><defs>" + definition + "</defs>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(code, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Graph_limits_release_intermediates_preserve_source_and_allow_exact_retry()
    {
        RenderBudget parserBudget = new(1_000_000);
        using SvgRoot root = SvgRootParser.Parse(Document("<defs><filter id='f'><feDropShadow stdDeviation='3' dx='1' dy='1' flood-opacity='.5'/></filter></defs><rect width='4' height='4' filter='url(#f)'/>"), SvgRenderLimits.Default, parserBudget, CancellationToken.None);
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
    public void Pending_pixel_semantics_fail_before_any_stream_output(string primitive)
    {
        byte[] input = Document("<defs><filter id='f'>" + primitive + "</filter></defs><rect width='4' height='4' fill='red' filter='url(#f)'/>");
        using MemoryStream stream = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Translucent_colored_linear_shadow_keeps_its_explicit_color_conversion_gate()
    {
        byte[] input = Document("<defs><filter id='f'><feDropShadow stdDeviation='3'/></filter></defs><rect width='4' height='4' fill='red' fill-opacity='.5' filter='url(#f)'/>");
        using MemoryStream stream = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, stream.Length);
    }

    private static byte[] Document(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'>" + content + "</svg>");
}
