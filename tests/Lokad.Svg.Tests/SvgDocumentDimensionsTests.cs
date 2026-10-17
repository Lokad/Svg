using System.Buffers.Binary;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDocumentDimensionsTests
{
    [Theory]
    [InlineData("", 300, 150, SvgDimensionSource.Fallback, SvgDimensionSource.Fallback)]
    [InlineData("viewBox='10 20 40 80'", 300, 150, SvgDimensionSource.Fallback, SvgDimensionSource.Fallback)]
    [InlineData("width='1in' height='72pt'", 96, 96, SvgDimensionSource.Declaration, SvgDimensionSource.Declaration)]
    [InlineData("width='200' viewBox='0 0 400 200'", 200, 100, SvgDimensionSource.Declaration, SvgDimensionSource.AspectRatio)]
    [InlineData("height='80' viewBox='0 0 400 200'", 160, 80, SvgDimensionSource.AspectRatio, SvgDimensionSource.Declaration)]
    [InlineData("width='200'", 200, 150, SvgDimensionSource.Declaration, SvgDimensionSource.Fallback)]
    [InlineData("height='80'", 300, 80, SvgDimensionSource.Fallback, SvgDimensionSource.Declaration)]
    [InlineData("width='50%' height='25%'", 150, 37.5, SvgDimensionSource.Declaration, SvgDimensionSource.Declaration)]
    public void Inspection_and_conversion_share_logical_sizing(string attributes, double width, double height,
        SvgDimensionSource widthSource, SvgDimensionSource heightSource)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + attributes + "/>");
        SvgDocumentDimensions result = SvgConverter.InspectDimensions(svg, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new SvgViewport(width, height), result.LogicalViewport);
        Assert.Equal(widthSource, result.WidthSource);
        Assert.Equal(heightSource, result.HeightSource);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal((int)Math.Round(width, MidpointRounding.AwayFromZero), BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
        Assert.Equal((int)Math.Round(height, MidpointRounding.AwayFromZero), BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
    }

    [Fact]
    public void Inspection_retains_percentages_fallback_provenance_and_view_box_mapping()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='50%' viewBox='10 20 400 200' preserveAspectRatio='xMaxYMin slice'/>"u8.ToArray();
        SvgDocumentDimensions result = SvgConverter.InspectDimensions(svg, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(50, result.Width?.Value);
        Assert.True(result.Width?.IsPercentage);
        Assert.Null(result.Height);
        Assert.Equal(new SvgViewport(150, 75), result.LogicalViewport);
        Assert.True(result.WidthUsesFallbackBase);
        Assert.True(result.HeightUsesFallbackBase);
        SvgDocumentViewBox box = Assert.IsType<SvgDocumentViewBox>(result.ViewBox);
        Assert.Equal(10, box.X); Assert.Equal(20, box.Y);
        Assert.Equal(400, box.Width); Assert.Equal(200, box.Height);
        Assert.Equal(SvgAspectRatioAlignment.Max, box.HorizontalAlignment);
        Assert.Equal(SvgAspectRatioAlignment.Min, box.VerticalAlignment);
        Assert.Equal(SvgAspectRatioFit.Slice, box.Fit);
    }

    [Theory]
    [InlineData("width='50%' height='25%'", 400, 150)]
    [InlineData("width='12' height='24'", 800, 600)]
    [InlineData("", 800, 600)]
    public void Supplied_viewport_retains_existing_percentage_and_override_rules(string attributes, double width, double height)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + attributes + "/>");
        SvgRenderOptions options = new(new SvgViewport(800, 600), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgDocumentDimensions result = SvgConverter.InspectDimensions(svg, options, CancellationToken.None);
        Assert.Equal(new SvgViewport(width, height), result.LogicalViewport);
        Assert.False(result.WidthUsesFallbackBase); Assert.False(result.HeightUsesFallbackBase);
    }

    [Fact]
    public void Late_css_cascade_is_shared_without_loading_fonts_or_lowering_children()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' id='root' width='20' height='10' style='width:40px'><text font-family='Unavailable' y='12'>A</text><foreignObject/><image href='invalid'/><style>svg{width:80px!important;height:30px}</style></svg>"u8.ToArray();
        SvgDocumentDimensions result = SvgConverter.InspectDimensions(svg, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(new SvgViewport(80, 30), result.LogicalViewport);
        byte[] drawable = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(svg).Replace("<text font-family='Unavailable' y='12'>A</text><foreignObject/><image href='invalid'/>", "", StringComparison.Ordinal));
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(drawable, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(80, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4)));
        Assert.Equal(30, BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4)));
    }

    [Fact]
    public void Inspection_does_not_apply_raster_caps_or_allocate_a_surface()
    {
        SvgRenderLimits limits = new(32_768, 4096, 1, 1, 1, 8, 1, 10_000, 8, 8);
        SvgRenderOptions options = new(null, 100, SvgColor.Transparent, limits);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1000000000' height='1000000000'><text font-family='Missing'>A</text></svg>"u8.ToArray();
        SvgDocumentDimensions result = SvgConverter.InspectDimensions(svg, options, CancellationToken.None);
        Assert.Equal(new SvgViewport(1_000_000_000, 1_000_000_000), result.LogicalViewport);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='2' height='2'/>"u8.ToArray(), options, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g>", SvgErrorCode.MalformedInput)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/><svg/>", SvgErrorCode.MalformedInput)]
    [InlineData("<other/>", SvgErrorCode.MalformedInput)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='12em'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' style='width:12em'/>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='0'/>", SvgErrorCode.MalformedInput)]
    public void Inspection_reports_input_errors_without_best_effort_success(string xml, SvgErrorCode expected)
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(xml);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgConverter.InspectDimensions(svg, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Inspection_releases_parser_storage_on_success_denial_and_malformed_xml()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><style>svg{width:40px;height:20px}</style></svg>"u8.ToArray();
        RenderBudget budget = new(1_000_000);
        Assert.Equal(new SvgViewport(40, 20), SvgDocumentSizing.Inspect(svg, SvgRenderOptions.Default, budget, new(10_000), CancellationToken.None).LogicalViewport);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgDocumentSizing.Inspect(svg, SvgRenderOptions.Default, budget, new(1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgDocumentSizing.Inspect(svg, SvgRenderOptions.Default, denied, new(10_000), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Throws<System.Xml.XmlException>(() => SvgDocumentSizing.Inspect(
            "<svg xmlns='http://www.w3.org/2000/svg'><style>svg{width:40px}</style><g>"u8.ToArray(),
            SvgRenderOptions.Default, budget, new(10_000), CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Missing_axis_fallback_is_also_the_nested_layout_percentage_base()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='200'><svg x='50%' y='50%' width='50%' height='50%'><rect width='4' height='4' fill='red'/></svg></svg>"u8.ToArray();
        Assert.Equal(new SvgViewport(200, 150), SvgConverter.InspectDimensions(svg, SvgRenderOptions.Default, CancellationToken.None).LogicalViewport);
        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new SvgRectangle(100, 75, 4, 4, new SvgColor(255, 0, 0, 255), true, null), Assert.Single(root.Rectangles));
        Assert.Empty(Lokad.Svg.Fonts.SvgFontRequestDiscovery.Discover(svg, CancellationToken.None));
    }

    [Fact]
    public void Inspection_checks_structure_and_preserves_cancellation()
    {
        SvgRenderLimits limits = new(1_000_000, 4096, 100, 10_000, 4096, 1, 10, 10_000, 8, 8);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.InspectDimensions("<svg xmlns='http://www.w3.org/2000/svg'><g/></svg>"u8.ToArray(), options, CancellationToken.None)).ErrorCode);
        using CancellationTokenSource source = new();
        source.Cancel();
        Assert.Equal(source.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.InspectDimensions("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), options, source.Token)).CancellationToken);
    }
}
