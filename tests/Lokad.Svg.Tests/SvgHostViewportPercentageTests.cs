using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed class SvgHostViewportPercentageTests
{
    [Theory]
    [InlineData("width='8' height='6'", 16, 12)]
    [InlineData("width='8px' height='6px'", 16, 12)]
    [InlineData("style='width:8px;height:6px'", 16, 12)]
    [InlineData("width='8'", 16, 12)]
    [InlineData("height='6'", 16, 12)]
    [InlineData("width='50%' height='6'", 8, 12)]
    [InlineData("width='8' height='50%'", 16, 6)]
    [InlineData("width='50%' height='50%'", 8, 6)]
    [InlineData("", 16, 12)]
    [InlineData("width='8' height='6' viewBox='0 0 16 12'", 16, 12)]
    public void Child_percentages_use_the_same_viewport_as_logical_sizing(string attributes, int width, int height)
    {
        SvgRenderOptions options = new(new SvgViewport(16, 12), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] input = Input(attributes, "<rect width='100%' height='100%' fill='red'/>");
        SvgDocumentDimensions metadata = SvgConverter.InspectDimensions(input, options, CancellationToken.None);
        Assert.Equal(new SvgViewport(width, height), metadata.LogicalViewport);
        Assert.Equal(Render(Input(attributes, $"<rect width='{width}' height='{height}' fill='red'/>"), options), Render(input, options));
    }

    [Theory]
    [InlineData("<rect width='100%' height='100%' fill='red'/>")]
    [InlineData("<svg width='100%' height='100%'><rect width='16' height='12' fill='red'/></svg>")]
    [InlineData("<circle cx='50%' cy='50%' r='3' fill='red'/>")]
    [InlineData("<ellipse cx='50%' cy='50%' rx='3' ry='2' fill='red'/>")]
    [InlineData("<defs><rect id='r' width='2' height='2'/></defs><use href='#r' x='50%' y='50%' fill='red'/>")]
    [InlineData("<defs><clipPath id='c'><rect width='100%' height='100%'/></clipPath></defs><rect width='16' height='12' fill='red' clip-path='url(#c)'/>")]
    [InlineData("<defs><mask id='m' maskContentUnits='userSpaceOnUse'><rect width='100%' height='100%' fill='white'/></mask></defs><rect width='16' height='12' fill='red' mask='url(#m)'/>")]
    public void Host_viewport_bases_reach_shapes_nested_viewports_instances_and_resources(string content)
    {
        SvgRenderOptions options = new(new SvgViewport(16, 12), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.Equal(Render(Input("", content), options), Render(Input("width='8' height='6'", content), options));
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public void Raster_scaling_and_fitting_do_not_change_logical_percentage_bases(double scale, bool bounded)
    {
        SvgRenderOptions options = new(new SvgViewport(16, 12), scale, SvgColor.Transparent, SvgRenderLimits.Default, null, bounded ? new(8, 6, null) : null);
        const string content = "<rect width='100%' height='100%' fill='red'/>";
        Assert.Equal(Render(Input("", content), options), Render(Input("width='8' height='6'", content), options));
    }

    [Fact]
    public void Later_stylesheet_sizing_and_inspection_keep_authored_provenance()
    {
        SvgRenderOptions options = new(new SvgViewport(16, 12), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] input = Input("id='root'", "<rect width='100%' height='100%' fill='red'/><style>#root{width:8px;height:6px}</style>");
        SvgDocumentDimensions metadata = SvgConverter.InspectDimensions(input, options, CancellationToken.None);
        Assert.Equal(8, metadata.Width?.Value);
        Assert.Equal(6, metadata.Height?.Value);
        Assert.Equal(SvgDimensionSource.Viewport, metadata.WidthSource);
        Assert.Equal(SvgDimensionSource.Viewport, metadata.HeightSource);
        Assert.False(metadata.WidthUsesFallbackBase);
        Assert.False(metadata.HeightUsesFallbackBase);
        Assert.Equal(Render(Input("", "<rect width='16' height='12' fill='red'/>"), options), Render(input, options));
    }

    [Fact]
    public void Absolute_child_coordinates_are_not_scaled_to_the_host_viewport()
    {
        SvgRenderOptions options = new(new SvgViewport(16, 12), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        const string content = "<rect width='8' height='6' fill='red'/>";
        Assert.Equal(Render(Input("", content), options), Render(Input("width='8' height='6'", content), options));
    }

    [Fact]
    public async Task Owned_destination_stream_and_prepared_outputs_share_the_host_viewport()
    {
        SvgRenderOptions options = new(new SvgViewport(16, 12), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] input = Input("width='8' height='6'", "<rect width='100%' height='100%' fill='red'/>");
        byte[] expected = Render(Input("", "<rect width='16' height='12' fill='red'/>"), options);
        Assert.Equal(expected, Render(input, options));
        byte[] destination = new byte[expected.Length + 64];
        int count = SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None);
        Assert.Equal(expected, destination.AsSpan(0, count).ToArray());
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        count = await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None);
        Assert.Equal(expected, destination.AsSpan(0, count).ToArray());
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());

        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse("""
            { "packId": "unused", "files": [{ "relativePath": "unused.ttf", "byteSize": 1,
              "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }],
              "families": [{ "requestedFamily": "Unused", "resolvedFamily": "Unused", "relativeFontFile": "unused.ttf" }] }
            """u8.ToArray(), "unused");
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new UnusedFontProvider(), options, CancellationToken.None);
        Assert.Equal(expected, (await prepared.ConvertToPngAsync(CancellationToken.None)).ToArray());
    }

    private sealed class UnusedFontProvider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => throw new InvalidOperationException("A font-free document must not acquire fonts.");
    }

    private static byte[] Input(string attributes, string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" + content + "</svg>");

    private static byte[] Render(byte[] input, SvgRenderOptions options)
        => SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
}
