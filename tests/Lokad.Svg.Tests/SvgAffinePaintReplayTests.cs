using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffinePaintReplayTests
{
    [Fact]
    public void Patterns_share_native_content_across_ordinary_reflected_and_rotated_placement()
    {
        byte[] input = Document(Pattern + "<path d='M2 2H16V16H2Z' fill='url(#p)' transform='translate(35 0) scale(-1 1)'/><path d='M2 2H16V16H2Z' fill='url(#p)' transform='rotate(19 20 20)'/><path d='M2 2H16V16H2Z' fill='url(#p)'/>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgPatternPolygon[] paints = scene.Geometries.Where(geometry => geometry.Kind == SvgGeometryKind.PatternPolygon).Select(geometry => geometry.PatternPolygon).ToArray();
            Assert.Equal(3, paints.Length);
            Assert.NotNull(paints[0].VectorContent);
            Assert.Same(paints[0].VectorContent, paints[1].VectorContent);
            Assert.Same(paints[0].VectorContent, paints[2].VectorContent);
            using SvgConverter.RasterizedSurface output = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Contains(output.Pixels, value => value != 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Affine_paint_denial_cancellation_and_retry_preserve_owners_and_source(bool pattern)
    {
        byte[] input = pattern
            ? Document(Pattern + "<path d='M2 3Q14 -2 36 6L34 30Q8 36 2 3Z' fill='url(#p)' transform='translate(41.25 .625) scale(-1 1)'/>")
            : Document("<defs><linearGradient id='p' gradientUnits='userSpaceOnUse' x2='40' gradientTransform='matrix(1 .2 .3 2 0 0)'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><path id='s' d='M2 3L20 1 22 20 3 22Z' fill='url(#p)'/></defs><use href='#s' transform='translate(4.25 2.375) rotate(19)'/>");
        byte[] unchanged = input.ToArray();
        SvgRenderOptions options = new(null, 8, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None)) expected = surface.Pixels.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            bool reached = false;
            using (SvgAnalyticPaintProbe.Arm((phase, count) => { reached = true; cancellation.Cancel(); }))
            {
                OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(scene, options, budget, cancellation.Token);
                });
                Assert.Equal(cancellation.Token, error.CancellationToken);
            }
            Assert.True(reached);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new RenderBudget(new HostMemoryBudget(Math.Max(1, budget.Snapshot().PeakBytes - 1_024)));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, deniedMemory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    private const string Pattern = "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='6' height='4'><rect x='1' y='1' width='2' height='2' fill='blue'/></pattern></defs>";
    private static byte[] Document(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'>" + body + "</svg>");
}
