using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgUseStrokeSourceTests
{
    [Theory]
    [InlineData(false, "miter", 112)]
    [InlineData(true, "miter", 207)]
    [InlineData(false, "round", 112)]
    [InlineData(true, "round", 207)]
    public void An_integer_path_instance_paints_its_stroke_when_the_fill_is_disabled(bool parentOpacity, string join, int alpha)
    {
        string stroke = " fill='none' stroke='#5689c1' stroke-width='2' stroke-linejoin='" + join + "'";
        string opacity = parentOpacity ? "" : " opacity='0.4375'";
        string instance = "<use href='#p'" + stroke + opacity + "/>";
        string direct = "<path d='M4 5 H35 V29 H4 Z'" + stroke + opacity + "/>";
        if (parentOpacity)
        {
            instance = "<g opacity='0.8125'>" + instance + "</g>";
            direct = "<g opacity='0.8125'>" + direct + "</g>";
        }
        byte[] source = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'><defs><path id='p' d='M4 5 H35 V29 H4 Z'/></defs>" + instance + "</svg>");
        byte[] twin = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'>" + direct + "</svg>");
        RenderBudget budget = new(1_000_000), twinBudget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(source, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgRoot directScene = SvgRootParser.Parse(twin, SvgRenderLimits.Default, twinBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface rendered = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface expected = SvgConverter.Rasterize(directScene, SvgRenderOptions.Default, twinBudget, CancellationToken.None))
        {
            Assert.Equal(alpha, rendered.Pixels[(4 * rendered.Width + 8) * 4 + 3]);
            Assert.Equal(0, rendered.Pixels[(10 * rendered.Width + 10) * 4 + 3]);
            Assert.Equal(expected.Pixels, rendered.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, twinBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("miter")]
    [InlineData("round")]
    public void Retained_instance_stroke_sources_release_owners_on_parser_and_raster_denial(string join)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='41' height='35'><defs><path id='p' d='M4 5 H35 V29 H4 Z'/></defs><use href='#p' fill='none' stroke='#5689c1' stroke-width='2' stroke-linejoin='" + join + "'/></svg>");
        RenderBudget measured = new(1_000_000);
        long totalWork;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, measured, CancellationToken.None))
            totalWork = surface.WorkUnits;
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096)
        {
            RenderBudget budget = new(limit);
            SvgException denied = Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        RenderBudget edge = new(peak - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, edge, null, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, edge, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, edge.Snapshot().CurrentBytes);
        foreach (long limit in new[] { totalWork - 1, totalWork, totalWork + 1 })
        {
            SvgRenderLimits defaults = SvgRenderLimits.Default;
            SvgRenderLimits limits = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                checked((int)limit), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
            RenderBudget budget = new(1_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, limits, budget, null, CancellationToken.None))
            {
                SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
                if (limit < totalWork)
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, options, budget, CancellationToken.None)).ErrorCode);
                else
                {
                    using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
                    Assert.Equal(totalWork, surface.WorkUnits);
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceledBudget = new(1_000_000);
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceledBudget, null, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, canceledBudget.Snapshot().CurrentBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceledBudget, null, CancellationToken.None))
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, SvgRenderOptions.Default, canceledBudget, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceledBudget.Snapshot().CurrentBytes);
    }
}
