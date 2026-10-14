using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineOvalStrokeTests
{
    [Theory]
    [InlineData("affine-orbit-diagram")]
    [InlineData("nonuniform-dashed")]
    [InlineData("dashed-radial")]
    [InlineData("affine-pattern-dashes")]
    public void Affine_oval_strokes_preserve_source_and_release_material_storage_after_denial_and_retry(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "affine-oval-strokes", identity + ".svg"));
        byte[] unchanged = input.ToArray();
        SvgRenderOptions options = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new(8_000_000);
        ParserWorkCounter work = new(1_000_000);
        byte[] expected;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, work, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Contains(expected, value => value != 0);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { work.Used / 2, work.Used - 1 })
        {
            RenderBudget denied = new(8_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, new(limit), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        RenderBudget tight = new(Math.Max(1, measured.Snapshot().PeakBytes - 4_096));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, tight, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            Assert.Equal(expected, surface.Pixels);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("round")]
    [InlineData("square")]
    public void Unsupported_dashed_oval_shader_caps_release_the_preceding_owner_before_output(string cap)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='32' height='32' fill='red'/><ellipse cx='16' cy='16' rx='12' ry='8' fill='none' stroke='url(#g)' stroke-width='2' stroke-dasharray='4 2' stroke-linecap='" + cap + "'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using MemoryStream output = new();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgConverter.ConvertToPng(input, output, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
    }
}
