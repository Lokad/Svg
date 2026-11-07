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

    [Fact]
    public void Compact_output_addressing_stays_separate_from_native_construction_bounds()
    {
        RenderBudget budget = new();
        using (RenderBudget.BudgetReservation capacity = budget.Reserve(300 * 150 * 4, SvgMemoryCategory.Surface))
        using (SvgCpuClipCoverage coverage = new(new SvgCpuRasterGrid(300, 150), budget, true))
        {
            SvgAnalyticClipBounds storage = new(112, 37, 188, 113);
            coverage.SetBounds(storage, storage);
            coverage.SetCanvasAlpha(112, 37, 197);
            coverage.SetCanvasAlpha(187, 112, 251);
            Assert.Equal(197, coverage.GetCanvasAlpha(112, 37));
            Assert.Equal(251, coverage.GetCanvasAlpha(187, 112));
            Assert.Equal(0, coverage.GetCanvasAlpha(75, 0));
            Assert.Equal(0, coverage.GetCanvasAlpha(188, 37));
            Assert.Equal(0, coverage.GetCanvasAlpha(112, 113));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(512, 512)]
    public void Compact_output_lookup_uses_the_addressable_bitmap_including_tiny_single_bitmaps(int width, int height)
    {
        RenderBudget budget = new();
        SvgCpuRasterGrid grid = new(width, height, true);
        using (RenderBudget.BudgetReservation capacity = budget.Reserve(width * height * 4, SvgMemoryCategory.Surface))
        using (SvgCpuClipCoverage coverage = new(grid, budget, true))
        {
            SvgAnalyticClipBounds bitmap = grid.GetOwningBitmap(width - 1, height - 1);
            Assert.Equal(new(0, 0, width, height), bitmap);
            coverage.SetBounds(bitmap, bitmap);
            coverage.SetCanvasAlpha(width - 1, height - 1, 137);
            Assert.Equal(137, coverage.GetCanvasAlpha(width - 1, height - 1));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Compact_clip_keeps_output_samples_separate_from_neighboring_bitmap_borders()
    {
        RenderBudget budget = new();
        using (RenderBudget.BudgetReservation capacity = budget.Reserve(512 * 512 * 4, SvgMemoryCategory.Surface))
        using (SvgCpuClipCoverage coverage = new(new SvgCpuRasterGrid(512, 512), budget, true))
        {
            SvgAnalyticClipBounds left = new(0, 0, 256, 256);
            SvgAnalyticClipBounds right = new SvgCpuRasterGrid(512, 512).GetOwningBitmap(255, 10);
            coverage.SetBounds(left, left);
            coverage.SetBounds(right, right);
            coverage.SetCanvasAlpha(255, 10, 193);
            coverage.SetBorderAlpha(left, 255, 10, 41);
            SvgRasterClipMask mask = new(coverage);
            Assert.False(mask.IsEmpty);
            Assert.Equal(193, mask.GetAlpha(512, 255, 10));
            Assert.Equal(193, mask.GetAlphaAtOffset(512, (10 * 512 + 255) * 4));
            Assert.Equal(41, coverage.GetAlpha(left, mask, 512, 255, 10));
            Assert.Equal(0, mask.GetAlpha(512, 20, 500));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Compact_clip_intersects_parent_alpha_independently_for_output_and_border_samples()
    {
        RenderBudget budget = new();
        using (RenderBudget.BudgetReservation capacity = budget.Reserve(512 * 512 * 4, SvgMemoryCategory.Surface))
        using (SvgCpuClipCoverage coverage = new(new SvgCpuRasterGrid(512, 512), budget, true))
        {
            SvgAnalyticClipBounds left = new(0, 0, 256, 256);
            SvgAnalyticClipBounds right = new SvgCpuRasterGrid(512, 512).GetOwningBitmap(255, 10);
            coverage.SetBounds(left, left);
            coverage.SetBounds(right, right);
            coverage.SetCanvasAlpha(255, 10, 193);
            coverage.SetCanvasAlpha(256, 10, 255);
            coverage.SetBorderAlpha(left, 255, 10, 41);
            SvgRasterClipMask parent = new(new SvgClipBox(255.5, 0, 20, 20));
            byte inherited = parent.GetAlpha(512, 255, 10);
            Assert.InRange(inherited, (byte)1, (byte)254);
            SvgAnalyticClipBounds bounds = coverage.IntersectCanvas(parent, 512, new(0, 0, 512, 512), CancellationToken.None, out bool rectangle);
            Assert.Equal(new(255, 10, 257, 11), bounds);
            Assert.False(rectangle);
            coverage.IntersectAndTrim(null, parent, [], 512, CancellationToken.None);
            SvgRasterClipMask mask = new(coverage);
            Assert.Equal((193 * inherited + 127) / 255, mask.GetAlpha(512, 255, 10));
            Assert.Equal((41 * inherited + 127) / 255, coverage.GetAlpha(left, mask, 512, 255, 10));
            Assert.Equal(255, mask.GetAlpha(512, 256, 10));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Small_symbol_viewports_do_not_allocate_a_canvas_tape_for_each_instance()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='512' height='512'>"
            + "<defs><symbol id='point' viewBox='0 0 12 12'><circle cx='6' cy='6' r='4'/></symbol></defs>"
            + "<use href='#point' x='16' y='16' width='12' height='12'/>"
            + "<use href='#point' x='40' y='16' width='12' height='12'/>"
            + "<use href='#point' x='64' y='16' width='12' height='12'/>"
            + "<use href='#point' x='88' y='16' width='12' height='12'/></svg>");
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, new RenderBudget(), null, CancellationToken.None);
        SvgRenderOptions options = new(new SvgViewport(512, 512), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        for (int i = 0; i < 3; i++)
        {
            using SvgConverter.RasterizedSurface warmup = SvgConverter.Rasterize(scene, options, new RenderBudget(), CancellationToken.None);
        }
        RenderBudget budget = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None))
            Assert.Equal(512 * 512 * 4, surface.Pixels.Length);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 3_000_000, "Four small viewport clips allocated " + allocated + " bytes.");
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

}
