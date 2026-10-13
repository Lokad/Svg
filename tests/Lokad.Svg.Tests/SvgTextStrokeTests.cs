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
    public async Task Pending_text_stroke_semantics_reject_before_acquisition_and_output(string attributes)
    {
        byte[] input = StrokeDocument("<text font-family='Example' font-size='48' x='8' y='64' stroke='red' " + attributes + ">A</text>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException discovery = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, discovery.ErrorCode);
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
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
    public async Task Pending_stroked_text_paths_reject_before_acquisition()
    {
        byte[] input = StrokeDocument("<defs><path id='p' d='M0 64H96'/></defs><text font-family='Example' stroke='red'><textPath href='#p'>A</textPath></text>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
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
        SvgRenderLimits limits = SvgRenderLimits.Default;
        SvgRenderOptions workDenied = new(null, 1, SvgColor.Transparent,
            new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels, limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, 2_000), snapshot);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(root, workDenied, accepted, CancellationToken.None)).ErrorCode);
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
