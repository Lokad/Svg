using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPolygonOutputParityTests
{
    private const string ComboSvg =
        "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>" +
        "<linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>" +
        "<radialGradient id='r' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>" +
        "<pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs>" +
        "<polygon points='0,0 8,0 0,8' fill='red'/><polygon points='8,0 16,0 16,8' fill='url(#g)'/>" +
        "<polygon points='0,8 8,8 0,16' fill='url(#r)'/><polygon points='8,8 16,8 8,16' fill='url(#p)'/></svg>";

    [Fact]
    public void Four_polygon_paints_cover_all_rasterizers()
    {
        // One quadrant per polygon paint, so the parity input below
        // exercises the solid, linear, radial and pattern rasterizers.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(ComboSvg);
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(268_435_456L));
        using SvgRoot root = SvgRootParser.Parse(svg, limits, budget, CancellationToken.None);

        Assert.Equal(4, root.Geometries.Length);
        Assert.Equal(
            [SvgGeometryKind.PolygonFill, SvgGeometryKind.GradientPolygon, SvgGeometryKind.RadialPolygon, SvgGeometryKind.PatternPolygon],
            root.Geometries.Select(geometry => geometry.Kind).ToArray());
    }

    [Fact]
    public async Task Six_outputs_agree_on_four_polygon_paints()
    {
        byte[] svg = System.Text.Encoding.UTF8.GetBytes(ComboSvg);
        byte[] expected = SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();

        byte[] memory = new byte[expected.Length];
        int written = SvgConverter.ConvertToPng(svg, memory, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, memory);

        using MemoryStream stream = new();
        int streamed = SvgConverter.ConvertToPng(svg, stream, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.Length, streamed);
        Assert.Equal(expected, stream.ToArray());

        byte[] expectedAsync = (await SvgConverter.ConvertToPngAsync(svg, SvgRenderOptions.Default, CancellationToken.None)).ToArray();
        Assert.Equal(expected, expectedAsync);

        byte[] memoryAsync = new byte[expected.Length];
        int writtenAsync = await SvgConverter.ConvertToPngAsync(svg, memoryAsync, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.Length, writtenAsync);
        Assert.Equal(expected, memoryAsync);

        using MemoryStream streamAsync = new();
        int streamedAsync = await SvgConverter.ConvertToPngAsync(svg, streamAsync, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected.Length, streamedAsync);
        Assert.Equal(expected, streamAsync.ToArray());
    }
}
