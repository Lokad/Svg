using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPaintOrderTests
{
    [Theory]
    [InlineData("paint-order='fill fill'")]
    [InlineData("paint-order='stroke markers stroke'")]
    [InlineData("paint-order='normal fill'")]
    [InlineData("paint-order='bogus'")]
    [InlineData("paint-order=''")]
    [InlineData("style='paint-order:stroke!important;paint-order:fill fill'")]
    [InlineData("style='paint-order:fill,stroke'")]
    public void Invalid_order_has_an_explicit_outcome_and_releases_owners(string attributes)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'><circle cx='24' cy='24' r='14' " + attributes + "/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("style='opacity:.5'", false, SvgErrorCode.UnsupportedFeature)]
    [InlineData("style='opacity:.5'", true, SvgErrorCode.UnsupportedFeature)]
    [InlineData("fill-opacity='NaN'", false, SvgErrorCode.MalformedInput)]
    [InlineData("style='fill-rule:bogus'", false, SvgErrorCode.UnsupportedFeature)]
    [InlineData("style='unknown:1'", true, SvgErrorCode.UnsupportedFeature)]
    public void Pattern_presentation_is_validated_even_without_children(string attributes, bool empty, SvgErrorCode expected)
    {
        string definition = "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse' " + attributes + (empty ? "/>" : "><rect width='4' height='8'/></pattern>");
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'><defs>" + definition + "</defs><rect width='48' height='48' fill='url(#p)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("path-opacity")]
    [InlineData("pattern-circle")]
    public void Cancellation_after_ordered_native_paint_preserves_the_scene_for_identical_retry(string identity)
    {
        byte[] input = identity == "pattern-circle"
            ? System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'><defs><pattern id='p' width='48' height='48' patternUnits='userSpaceOnUse' paint-order='stroke'><circle cx='24' cy='24' r='14' fill='#39c' stroke='#c43' stroke-width='8'/></pattern></defs><rect width='48' height='48' fill='url(#p)'/></svg>")
            : File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "paint-order", identity + ".svg"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface baseline = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None)) expected = baseline.Pixels.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            int replayed = 0;
            using (SvgAnalyticPaintProbe.Arm((phase, _) =>
            {
                if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++replayed == 1) cancellation.Cancel();
            }))
                Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token)).CancellationToken);
            Assert.Equal(1, replayed);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
