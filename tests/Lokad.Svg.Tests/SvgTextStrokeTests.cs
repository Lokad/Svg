using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("stroke='red' stroke-width='6' stroke-linejoin='round'")]
    [InlineData("color='blue' stroke='currentColor' stroke-width='3' stroke-opacity='.4'")]
    [InlineData("stroke='red' stroke-width='0' stroke-dasharray='3 2'")]
    public async Task Inherited_text_strokes_match_explicit_styles_through_child_spans(string attributes)
    {
        byte[] inherited = StrokeDocument("<g " + attributes + "><text font-family='Example' font-size='48' x='8' y='64' fill='none'><tspan>A</tspan></text></g>");
        byte[] explicitStyle = StrokeDocument("<text font-family='Example' font-size='48' x='8' y='64' fill='none' " + attributes + ">A</text>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(explicitStyle, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(inherited, options, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("stroke-dasharray='2 3'")]
    [InlineData("paint-order='stroke fill'")]
    [InlineData("vector-effect='non-scaling-stroke'")]
    public async Task Ordered_dashed_and_non_scaling_text_acquire_and_paint_the_required_face(string attributes)
    {
        byte[] input = StrokeDocument("<text font-family='Example' font-size='48' x='8' y='64' stroke='red' " + attributes + ">A</text>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] painted = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.NotEqual(SvgConverter.ConvertToPng(StrokeDocument(string.Empty), options, CancellationToken.None).ToArray(), painted);
        Assert.Single(snapshot.Fonts);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Theory]
    [InlineData("stroke-width='0'")]
    [InlineData("style='stroke-width:0'")]
    public async Task Child_zero_width_strokes_do_not_request_an_unused_face(string attributes)
    {
        byte[] input = StrokeDocument("<text font-family='Unused' fill='none' stroke='red' stroke-width='8'><tspan " + attributes + ">A</tspan></text>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Empty(snapshot.Fonts);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.True(SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).Length > 0);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Root_zero_width_strokes_do_not_request_an_unused_face()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96' stroke='red' stroke-width='0' fill='none'><text font-family='Unused'>A</text></svg>"u8.ToArray();
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Empty(snapshot.Fonts);
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        Assert.True(SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).Length > 0);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Stroked_text_paths_acquire_and_match_the_same_direct_baseline()
    {
        byte[] input = StrokeDocument("<defs><path id='p' d='M0 64H96'/></defs><text font-family='Example' stroke='red'><textPath href='#p'>A</textPath></text>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] direct = StrokeDocument("<text font-family='Example' stroke='red' y='64'>A</text>");
        Assert.Equal(SvgConverter.ConvertToPng(direct, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Theory]
    [InlineData("stroke-dasharray='-1 2'", SvgErrorCode.MalformedInput)]
    [InlineData("stroke-dasharray='1 2 3 4 5 6 7 8 9'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("stroke-dashoffset='10%'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("vector-effect='fixed-position'", SvgErrorCode.UnsupportedFeature)]
    [InlineData("paint-order='stroke stroke'", SvgErrorCode.UnsupportedFeature)]
    public async Task Invalid_text_stroke_modes_fail_before_acquisition_and_publication(string attributes, SvgErrorCode expected)
    {
        byte[] input = StrokeDocument("<text font-family='Example' stroke='red' " + attributes + ">A</text>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None));
        Assert.Equal(expected, failure.ErrorCode);
        using MemoryStream output = new();
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }

    [Theory]
    [InlineData("stroke-dasharray='0 0'", true)]
    [InlineData("stroke-dasharray='0 1000'", false)]
    public async Task Zero_and_empty_dash_results_preserve_solid_or_nonpainting_outcomes(string attributes, bool solid)
    {
        byte[] input = StrokeDocument("<text font-family='Example' font-size='48' x='8' y='64' fill='none' stroke='red' " + attributes + ">A</text>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = StrokeDocument(solid ? "<text font-family='Example' font-size='48' x='8' y='64' fill='none' stroke='red'>A</text>" : string.Empty);
        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("fill='blue' fill-opacity='0' stroke='none'")]
    [InlineData("fill='none' stroke='red' stroke-width='6' stroke-opacity='0'")]
    public async Task Filtered_glyph_fill_and_stroke_with_zero_opacity_keep_the_output_transparent(string paint)
    {
        string label = "<text font-family='Example' font-size='48' x='8' y='64' " + paint + ">A</text>";
        byte[] plain = StrokeDocument(string.Empty);
        byte[] filtered = StrokeDocument("<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='96' height='96'><feGaussianBlur stdDeviation='0'/></filter></defs><g opacity='.5' filter='url(#f)'>" + label + "</g>");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(plain, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(filtered, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Stroke_surface_denial_and_cancellation_release_temporary_owners_and_allow_retry()
    {
        byte[] input = StrokeDocument("<g opacity='.5'><text font-family='Example' font-size='48' x='8' y='64' fill='none' stroke='red' stroke-width='8'>A</text></g>");
        byte[] unchanged = (byte[])input.Clone();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget sceneBudget = new(1_000_000);
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget accepted = new(1_000_000);
        byte[] expected = SvgConverter.ConvertToPng(root, options, accepted, CancellationToken.None).ToArray();
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new(40_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(root, options, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SvgConverter.ConvertToPng(root, options, accepted, cancellation.Token));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Equal(expected, SvgConverter.ConvertToPng(root, options, accepted, CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
        root.Dispose();
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    private static byte[] StrokeDocument(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'>" + content + "</svg>");
}
