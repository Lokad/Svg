using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAffineRadialPaintTests
{
    [Fact]
    public void Tiled_curved_clip_releases_border_owners_after_late_denial_and_partial_replay_cancellation()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='620' height='620' viewBox='0 0 100 100'><defs><clipPath id='c'><path d='M10 75 A40 40 0 0 1 90 75'/></clipPath></defs><g transform='rotate(20 90 75)' clip-path='url(#c)'><circle cx='50%' cy='75%' r='40%' fill='#eee'/></g></svg>"u8.ToArray();
        const long maximumBytes = 20_000_000;
        RenderBudget budget = new(maximumBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            long owned = budget.Snapshot().CurrentBytes;
            byte[] expected;
            long rasterWork;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            {
                expected = surface.Pixels.ToArray();
                rasterWork = surface.WorkUnits;
                Assert.Contains(expected, value => value != 0);
            }
            long temporaryPeak = budget.Snapshot().PeakBytes - owned;
            foreach (long available in new[] { temporaryPeak / 2, temporaryPeak - 1 })
            {
                using RenderBudget.BudgetReservation padding = budget.Reserve(maximumBytes - owned - available, SvgMemoryCategory.Surface);
                long padded = budget.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            SvgRenderLimits defaults = SvgRenderLimits.Default;
            SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                checked((int)(rasterWork - 1)), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(owned, budget.Snapshot().CurrentBytes);
            foreach (int threshold in new[] { 1, 5_000 })
            {
                using CancellationTokenSource cancellation = new();
                int observed = 0;
                using (SvgAnalyticPaintProbe.Arm((phase, _) =>
                {
                    if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++observed == threshold) cancellation.Cancel();
                }))
                {
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    {
                        using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token);
                    }).CancellationToken);
                }
                Assert.Equal(threshold, observed);
                Assert.Equal(owned, budget.Snapshot().CurrentBytes);
            }
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels);
            Assert.Equal(owned, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<radialGradient id='p' fx='31%' fy='42%'><stop stop-color='#eee'/><stop offset='1' stop-color='#aaa'/></radialGradient>", "<circle cx='16' cy='16' r='11' fill='url(#p)' transform='rotate(23 16 16)'/>")]
    [InlineData("<radialGradient id='p' gradientTransform='matrix(1 .12 .18 .9 .02 -.04)'><stop stop-color='#eee'/><stop offset='1' stop-color='#aaa'/></radialGradient>", "<path d='M4 24 C4 4 28 4 28 24Z' fill='url(#p)' transform='matrix(.8 .2 -.1 .9 4 2)'/>")]
    [InlineData("<radialGradient id='p' fx='31%' fy='42%'><stop stop-color='#eee'/><stop offset='1' stop-color='#aaa'/></radialGradient><clipPath id='c'><path d='M5 24 A11 11 0 0 1 27 24'/></clipPath>", "<g transform='rotate(12 16 16)' clip-path='url(#c)'><circle cx='16' cy='24' r='11' fill='url(#p)'/></g>")]
    public void Affine_paint_and_clip_replay_releases_owners_on_denial_and_preserves_source_for_retry(string definitions, string shape)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + shape + "</svg>");
        RenderBudget measured = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, CancellationToken.None))
        {
            SvgAuthoredPath[] paths = root.Geometries.ToArray().Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            SvgAuthoredPathCommand[][] original = paths.Select(path => path.Commands.ToArray()).ToArray();
            long owned = retry.Snapshot().CurrentBytes;
            byte[] expected;
            long rasterWork;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            {
                expected = surface.Pixels.ToArray();
                Assert.Contains(expected, value => value != 0);
                rasterWork = surface.WorkUnits;
            }
            SvgRenderLimits defaults = SvgRenderLimits.Default;
            foreach (long limit in new[] { 0L + 1, (0L + rasterWork) / 2, rasterWork - 1 })
            {
                SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                    defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                    checked((int)limit), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), retry, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(owned, retry.Snapshot().CurrentBytes);
            }
            using (retry.Reserve(1_000_000 - owned - 1024, SvgMemoryCategory.Surface))
            {
                long padded = retry.Snapshot().CurrentBytes;
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                {
                    using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None);
                }).ErrorCode);
                Assert.Equal(padded, retry.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, cancellation.Token);
            }).CancellationToken);
            for (int index = 0; index < paths.Length; index++) Assert.Equal(original[index], paths[index].Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels);
            Assert.Equal(owned, retry.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        void DenyMemory(long limit)
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("<circle cx='16' cy='16' r='0' fill='url(#p)' transform='rotate(23 16 16)'/>")]
    [InlineData("<g clip-path='url(#c)' transform='rotate(12 16 16)'><circle cx='100' cy='100' r='11' fill='url(#p)'/></g>")]
    public void Empty_affine_paint_and_disjoint_curved_clip_remain_transparent(string shape)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><radialGradient id='p' fx='31%' fy='42%'><stop stop-color='#eee'/><stop offset='1' stop-color='#aaa'/></radialGradient><clipPath id='c'><path d='M5 24 A11 11 0 0 1 27 24'/></clipPath></defs>" + shape + "</svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
