using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgDashSourceOwnershipTests
{
    [Theory]
    [InlineData(1, "butt")]
    [InlineData(4, "round")]
    public void Bitmap_replay_refusal_and_cancellation_preserve_the_prepared_source_for_exact_retry(int width, string cap)
    {
        const long allowance = 64_000_000;
        HostMemoryBudget host = new(allowance);
        RenderBudget budget = new(host);
        using (SvgRoot root = SvgRootParser.Parse(Input(width, cap), SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            Assert.NotNull(path.DashSource);
            SvgAuthoredPathCommand[] commands = path.Commands.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            // Deny the measured overlapping raster owners, after scene preparation.
            host.Limit = budget.Snapshot().PeakBytes - 1;
            SvgException refused = Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.LimitExceeded, refused.ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            host.Limit = allowance;
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancelled.Token);
            });
            Assert.Equal(cancelled.Token, error.CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(commands, path.Commands.ToArray());
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(0, host.Reserved);
    }

    [Fact]
    public void Placed_copy_and_transfer_keep_an_independent_dash_source_after_the_original_is_released()
    {
        RenderBudget budget = new(new HostMemoryBudget(64_000_000));
        SvgAuthoredPath copied;
        using (SvgRoot root = SvgRootParser.Parse(Input(1, "butt"), SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgAuthoredPath original = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            copied = original.Copy(SvgAffineTransform.FromTranslation(3, 5), budget, CancellationToken.None);
            Assert.NotNull(copied.DashSource);
            Assert.NotSame(original.DashSource, copied.DashSource);
            long retained = budget.Snapshot().CurrentBytes;
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => original.Copy(SvgAffineTransform.Identity, budget, cancelled.Token));
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        using (copied)
        using (SvgAuthoredPath moved = copied.Transfer())
        {
            long retained = budget.Snapshot().CurrentBytes;
            copied.Dispose();
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            SvgAuthoredDashSource source = Assert.IsType<SvgAuthoredDashSource>(moved.DashSource);
            Assert.True(source.TryFilter(moved.SceneMap, new(new(0, 0, 256, 256), new(0, 0)), budget, CancellationToken.None, out OwnedParserArray<SvgAuthoredPathCommand> filtered));
            using (filtered)
            {
                Assert.NotEmpty(filtered.Items);
                Assert.Equal(new SvgDevicePoint(17, 7), filtered.Items[0].First);
                Assert.True(filtered.Items.Where(command => command.Verb == SvgAuthoredPathVerb.Line).Max(command => command.First.Y) < 256);
            }
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(int width, string cap) => TextEncoding.UTF8.GetBytes(
        "<svg xmlns='http://www.w3.org/2000/svg' width='600' height='384'><line x1='17' y1='7' x2='17' y2='367' stroke='#385e96' stroke-width='"
        + width + "' stroke-linecap='" + cap + "' stroke-dasharray='1 2'/></svg>");
}
