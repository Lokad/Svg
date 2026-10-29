using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSingleStopGradientTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void A_single_stop_keeps_browser_alpha_and_gradient_factory_dither(bool radial, bool instance, bool degenerate)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(Input(radial, instance, degenerate), SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            // Zero-size factories use a constant shader; positive-size gradients dither.
            // Both paths were captured independently in gradient-paint-replay.
            Assert.Equal(new byte[] { 87, 138, 193, 94 }, Pixel(4, 5));
            Assert.Equal(new byte[] { 87, degenerate ? (byte)138 : (byte)136, 193, 94 }, Pixel(5, 5));
            Assert.Equal(new byte[] { 87, 138, degenerate ? (byte)193 : (byte)195, 94 }, Pixel(5, 6));
            Assert.Equal(new byte[] { degenerate ? (byte)87 : (byte)84, degenerate ? (byte)138 : (byte)136, 193, 94 }, Pixel(10, 10));
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(2, 2));

            byte[] Pixel(int x, int y) => surface.Pixels.AsSpan((y * surface.Width + x) * 4, 4).ToArray();
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A_single_stop_releases_owners_on_memory_and_cancellation_denial(bool radial, bool instance)
    {
        byte[] input = Input(radial, instance, false);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
            {}
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 1024)
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(limit));
            SvgException denied = Assert.Throws<SvgException>(() =>
            {
                using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new RenderBudget(new HostMemoryBudget(1_000_000));
        OperationCanceledException exception = Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, canceled, CancellationToken.None))
        {
            long before = canceled.Snapshot().CurrentBytes;
            exception = Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, canceled, cancellation.Token));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Equal(before, canceled.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
    }

    private static byte[] Input(bool radial, bool instance, bool degenerate)
    {
        string kind = radial ? "radialGradient" : "linearGradient";
        string coordinates = degenerate ? radial ? " r='0'" : " x1='.25' x2='.25' y1='.5' y2='.5'" : "";
        string rectangle = "x='4' y='5' width='31' height='24'";
        string shape = instance ? "<use href='#p' fill='url(#g)'/>" : "<rect " + rectangle + " fill='url(#g)'/>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='61' height='47'><defs><" + kind + " id='g'" + coordinates + "><stop stop-color='#5689c1' stop-opacity='.37'/></" + kind + "><rect id='p' " + rectangle + "/></defs>" + shape + "</svg>");
    }
}
