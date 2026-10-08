using Lokad.Svg.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgOpacityLayerTests
{
    [Theory]
    [InlineData("group", "none", 127)]
    [InlineData("root", "none", 126)]
    [InlineData("group", "backward", 191)]
    [InlineData("group", "forward", 191)]
    public async Task Isolated_opacity_matches_browser_pixels_through_all_six_outputs(string scope, string mask, byte whiteLane)
    {
        string definition = mask == "none" ? "" : "<defs><mask id='m'><rect width='4' height='4' fill='#808080'/></mask></defs>";
        string attributes = "opacity='0.5'" + (mask == "none" ? "" : " mask='url(#m)'");
        string content = "<rect width='4' height='4' fill='blue'/>";
        string inputText = scope == "root"
            ? "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4' " + attributes + ">" + content + "</svg>"
            : "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'>" + (mask == "backward" ? definition : "") + "<g " + attributes + ">" + content + "</g>" + (mask == "forward" ? definition : "") + "</svg>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(inputText);
        SvgRenderOptions options = new(null, 1, new SvgColor(255, 255, 255, 255), SvgRenderLimits.Default);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RenderBudget decodeBudget = new(options.Limits.MaximumAccountedBytes);
        DecodedSolidImage decoded = PngImageDecoder.DecodeSolidColor(expected, options.Limits, decodeBudget, new ParserWorkCounter(options.Limits.MaximumWorkUnits), CancellationToken.None);
        Assert.Equal(new SvgColor(whiteLane, whiteLane, 255, 255), decoded.Color);
        Assert.Equal(0, decodeBudget.Snapshot().CurrentBytes);
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }
}
