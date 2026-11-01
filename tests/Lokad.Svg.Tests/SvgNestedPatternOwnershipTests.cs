using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgNestedPatternOwnershipTests
{
    [Fact]
    public void Nested_tile_memory_refusal_releases_owners_and_the_same_allowance_can_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>"
            + "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='url(#q)'/></pattern>"
            + "<pattern id='q' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4' fill='red'/></pattern>"
            + "</defs><rect width='8' height='8' fill='url(#p)'/></svg>");
        const long allowance = 8_000_000;
        RenderBudget measured = new(new HostMemoryBudget(allowance));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long held = allowance - measured.Snapshot().PeakBytes + 1;
        RenderBudget budget = new(new HostMemoryBudget(allowance));
        using (RenderBudget.BudgetReservation occupied = budget.Reserve(held, SvgMemoryCategory.Temporary))
        {
            Assert.Equal(SvgErrorCode.LimitExceeded,
                Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
            Assert.Equal(held, budget.Snapshot().CurrentBytes);
        }
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface image = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels.AsSpan(0, 4).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
