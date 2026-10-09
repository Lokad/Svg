using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskContentReplayTests
{
    [Fact]
    public void Canceled_ordered_mask_replay_preserves_source_curves_and_identical_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "mask-content-order", "two-reference-mask-leaves.svg"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgSceneStorage scene = Assert.IsType<SvgSceneStorage>(root.SceneStorage);
            SvgVectorContent content = Assert.IsType<SvgVectorContent>(Assert.Single(scene.MaskContents).VectorContent);
            SvgAuthoredPath[] paths = content.Scene.Geometries.Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            Assert.Equal(2, paths.Length);
            SvgAuthoredPathCommand[][] commands = paths.Select(path => path.Commands.ToArray()).ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            int replayCount = 0;
            byte[] expected;
            using (SvgAnalyticPaintProbe.Arm((phase, _) =>
            {
                if (phase == SvgAnalyticPaintPhase.BeforeReplay) replayCount++;
            }))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            Assert.True(replayCount >= 2);
            foreach (int threshold in new[] { 1, replayCount })
            {
                using CancellationTokenSource cancellation = new();
                int observed = 0;
                using (SvgAnalyticPaintProbe.Arm((phase, _) =>
                {
                    if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++observed == threshold) cancellation.Cancel();
                }))
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    {
                        using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancellation.Token);
                    }).CancellationToken);
                Assert.Equal(threshold, observed);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            }
            for (int index = 0; index < paths.Length; index++) Assert.Equal(commands[index], paths[index].Commands.ToArray());
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("id='unused'")]
    public void Anonymous_or_unused_ordered_masks_release_retained_children(string identifier)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask " + identifier + "><rect width='8' height='8' fill='white'/><path d='M0 0H4V8H0Z' fill='black'/></mask></defs><rect width='8' height='8' fill='red'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            for (int offset = 0; offset < surface.Pixels.Length; offset += 4)
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, surface.Pixels.AsSpan(offset, 4).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<mask id='m'/>", "")]
    [InlineData("", "<mask id='m'/>")]
    public void Duplicate_ordered_mask_ids_release_partial_owners(string preceding, string following)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs>" + preceding + "<mask id='m'><rect width='8' height='8' fill='white'/><circle cx='4' cy='4' r='3' fill='black'/></mask>" + following + "</defs></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("<path d='M0 0H4V8H0Z' fill='black' clip-path='url(#c)'/>")]
    [InlineData("<path d='M0 0H4V8H0Z' fill='black' mask='url(#m)'/>")]
    public void Unsupported_mask_content_releases_the_promoted_scene(string finalChild)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect width='8' height='8' fill='white'/><path d='M0 0H2V8H0Z' fill='black'/>" + finalChild + "</mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Nested_mask_groups_keep_the_public_unsupported_outcome_and_release_retained_children()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect width='8' height='8' fill='white'/><path d='M0 0H2V8H0Z' fill='black'/><g><path d='M0 0H4V8H0Z' fill='black'/></g></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            Assert.True(root.HasUnsupportedChild);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
