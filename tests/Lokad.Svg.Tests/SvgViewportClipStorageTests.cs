using System.Text;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgViewportClipStorageTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(2048)]
    public void Fractional_viewport_chains_render_without_recursive_clip_calls(int depth)
    {
        byte[] input = Chain(depth, "0.0001");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(20_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan((2 * 16 + 2) * 4, 4).ToArray());
            Assert.Equal(0, surface.Pixels[(8 * 16 + 8) * 4 + 3]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.375")]
    public void Scoped_clip_denials_preserve_borrowed_storage_and_retry(string shift)
    {
        byte[] input = Chain(12, shift), unchanged = input.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            {
                byte[] expected;
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                {
                    expected = surface.Pixels.ToArray();
                }
                long retained = budget.Snapshot().CurrentBytes;
                foreach (long available in new[] { 1L, 1024, 2048, 4096, 8192 })
                {
                    using (RenderBudget.BudgetReservation pressure = budget.Reserve((2_000_000L - budget.Snapshot().CurrentBytes) - available, SvgMemoryCategory.Surface))
                    {
                        long held = budget.Snapshot().CurrentBytes;
                        try { using SvgConverter.RasterizedSurface attempted = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None); }
                        catch (SvgException error) { Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode); }
                        Assert.Equal(held, budget.Snapshot().CurrentBytes);
                    }
                    Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                }
                using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                Assert.Equal(expected, retry.Pixels);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public void Cancellation_during_clip_replay_unwinds_owners_and_restores_the_borrowed_scene()
    {
        byte[] input = Chain(12, "0.375");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            {
                long retained = budget.Snapshot().CurrentBytes, firstReplay = 0;
                using CancellationTokenSource cancellation = new();
                using (SvgAnalyticPaintProbe.Arm((phase, _) =>
                {
                    if (phase != SvgAnalyticPaintPhase.BeforeReplay) return;
                    long current = budget.Snapshot().CurrentBytes;
                    if (firstReplay == 0) firstReplay = current;
                    else if (current > firstReplay + 16 * 16 * 4) cancellation.Cancel();
                }))
                {
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token)).CancellationToken);
                }
                Assert.True(cancellation.IsCancellationRequested);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Chain(int depth, string shift)
    {
        StringBuilder input = new("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><use href='#s0'/>");
        for (int index = 0; index < depth; index++)
            input.Append("<symbol id='s").Append(index).Append("'><use href='#s").Append(index + 1).Append("' x='").Append(shift).Append("'/></symbol>");
        input.Append("<symbol id='s").Append(depth).Append("'><rect width='8' height='8' fill='red'/></symbol></svg>");
        return System.Text.Encoding.UTF8.GetBytes(input.ToString());
    }
}
