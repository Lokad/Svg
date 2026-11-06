using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterCaptureDemandTests
{
    [Theory]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='1000' result='unused'/>", false)]
    [InlineData("<feOffset in='SourceAlpha' dx='10000' result='unused'/>", false)]
    [InlineData("<feDropShadow in='SourceAlpha' stdDeviation='100' result='unused'/>", false)]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='1000' result='unused'/>", true)]
    public void Unread_halos_do_not_enlarge_source_capture(string unread, bool liveBlur)
    {
        string selected = liveBlur ? "<feGaussianBlur in='SourceGraphic' stdDeviation='1.2'/>"
            : "<feColorMatrix in='SourceGraphic' type='saturate' values='.5'/>";
        SvgRenderLimits limits = new(4096, 128, 16384, 65536, 100, 100);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        RenderBudget parser = new(new HostMemoryBudget(1_048_576));
        using HostMemoryBudget host = new(1_048_576);
        RenderBudget raster = new(host);
        byte[] input = Input(unread + selected), unchanged = input.ToArray();
        byte[] expected;
        using (SvgRoot control = SvgRootParser.Parse(Input(selected), limits, parser, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(control, options, raster, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        using (SvgRoot root = SvgRootParser.Parse(input, limits, parser, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, raster, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, host.Reserved);
        Assert.Equal(0, raster.Snapshot().CurrentBytes);
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Unread_shadow_keeps_its_deviation_limit_after_capture_planning()
    {
        byte[] input = Input("<feDropShadow in='SourceAlpha' stdDeviation='136' result='unused'/>"
            + "<feColorMatrix in='SourceGraphic' type='saturate' values='.5'/>");
        RenderBudget parser = new(new HostMemoryBudget(1_048_576));
        using HostMemoryBudget host = new(1_048_576);
        RenderBudget raster = new(host);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            SvgException error = Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
            Assert.Equal("Drop-shadow deviations above 135 device pixels per axis are not implemented.", error.Message);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(" x='80' y='80' width='1' height='1'")]
    [InlineData(" width='0' height='0'")]
    public void Culled_unread_shadow_keeps_its_public_deviation_error(string region)
    {
        byte[] input = Input("<feDropShadow in='SourceAlpha' stdDeviation='136' result='unused'" + region + "/><feFlood flood-color='red'/>"),
            unchanged = input.ToArray();
        SvgException error = Assert.Throws<SvgException>(() =>
        {
            _ = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal("Drop-shadow deviations above 135 device pixels per axis are not implemented.", error.Message);
        Assert.Equal(unchanged, input);
    }

    private static byte[] Input(string graph) => TextEncoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><filter id='f' filterUnits='userSpaceOnUse' primitiveUnits='userSpaceOnUse' x='0' y='0' width='64' height='64' color-interpolation-filters='sRGB'>"
        + graph + "</filter></defs><rect x='4' y='4' width='56' height='56' fill='#1965bd' filter='url(#f)'/></svg>");
}
