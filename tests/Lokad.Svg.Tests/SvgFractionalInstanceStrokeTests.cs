using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFractionalInstanceStrokeTests
{
    [Theory]
    [InlineData("0.375", "translate(2 1)", "x='8.125' y='6.375' width='18.5' height='14.25' stroke-width='.375'")]
    [InlineData("2.5", "translate(2 1) scale(.5)", "x='5.0625' y='3.6875' width='9.25' height='7.125' stroke-width='1.25'")]
    [InlineData("2.5", "translate(23 3) scale(-.75 .75)", "x='4.53125' y='7.03125' width='13.875' height='10.6875' stroke-width='1.875'")]
    [InlineData("2.5", "scale(1.25)", "x='7.65625' y='6.71875' width='23.125' height='17.8125' stroke-width='3.125'")]
    public void Forward_inherited_fractional_stroke_matches_direct_mapped_geometry(string width, string transform, string mappedRectangle)
    {
        const string rectangle = "x='6.125' y='5.375' width='18.5' height='14.25' fill='none'";
        string presentation = "stroke='#bc9334' stroke-width='" + width + "' transform='" + transform + "' opacity='.625'";
        byte[] instance = Document("<use href='#s' " + presentation + "/><defs><rect id='s' " + rectangle + "/></defs>");
        byte[] inline = Document("<rect " + mappedRectangle + " stroke='#bc9334' fill='none' opacity='.625'/>");
        Assert.Equal(SvgConverter.ConvertToPng(inline, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(instance, SvgRenderOptions.Default, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Fractional_instance_denial_releases_owned_storage_and_allows_retry()
    {
        byte[] input = Document("<use href='#s' stroke='red' stroke-width='2.5'/><defs><rect id='s' x='2.125' y='2.375' width='24.5' height='20.25' fill='none'/></defs>");
        byte[] unchanged = input.ToArray();
        RenderBudget baseline = new RenderBudget(new HostMemoryBudget(8_000_000));
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, baseline, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, baseline, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(8_000_000));
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, denied, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("1500000000", "scale(2)")]
    [InlineData("2.5", "scale(2 1)")]
    public void Effective_width_overflow_and_nonuniform_placement_remain_bounded(string width, string transform)
    {
        byte[] input = Document("<defs><rect id='s' x='2' y='2' width='4' height='4' fill='none'/></defs><use href='#s' stroke='red' stroke-width='" + width + "' transform='" + transform + "'/>");
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
    }

    private static byte[] Document(string content) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='28'>" + content + "</svg>");
}
