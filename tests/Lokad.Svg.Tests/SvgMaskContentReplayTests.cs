using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskContentReplayTests
{
    [Theory]
    [InlineData("<rect x='2' y='2' width='8' height='4' fill='red' mask='url(#m)'/>", 2, 4)]
    [InlineData("<use href='#r' x='2' y='2' mask='url(#m)'/>", 2, 4)]
    [InlineData("<g mask='url(#m)'><use href='#r' x='2' y='2'/></g>", 2, 4)]
    [InlineData("<rect x='2' y='2' width='8' height='4' fill='red' transform='translate(20 0) scale(-1 1)' mask='url(#m)'/>", 14, 4)]
    [InlineData("<rect x='2' y='2' width='0' height='4' fill='red' mask='url(#m)'/>", 2, 0)]
    public void Object_box_content_percentages_bind_to_leaf_group_use_and_empty_targets(string target, int left, int paintedWidth)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='12'><defs>"
            + "<rect id='r' width='8' height='4' fill='red'/><mask id='m' maskContentUnits='objectBoundingBox'>"
            + "<rect width='50%' height='100%' fill='white'/></mask></defs>" + target
            + "<rect x='22' y='10' width='2' height='2' fill='blue'/></svg>");
        RenderBudget sceneBudget = new(1_000_000), rasterBudget = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, rasterBudget, CancellationToken.None))
            for (int y = 0; y < 12; y++) for (int x = 0; x < 24; x++)
            {
                int offset = (y * 24 + x) * 4;
                byte red = x >= left && x < left + paintedWidth && y is >= 2 and < 6 ? (byte)255 : (byte)0;
                byte blue = x >= 22 && y >= 10 ? (byte)255 : (byte)0;
                Assert.Equal(red, surface.Pixels[offset]); Assert.Equal(0, surface.Pixels[offset + 1]);
                Assert.Equal(blue, surface.Pixels[offset + 2]); Assert.Equal(Math.Max(red, blue), surface.Pixels[offset + 3]);
            }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes); Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
    }

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
    public void Nested_mask_groups_replay_in_order_and_release_retained_children()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect width='8' height='8' fill='white'/><path d='M0 0H2V8H0Z' fill='black'/><g><path d='M0 0H4V8H0Z' fill='black'/></g></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.False(root.HasUnsupportedChild);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int offset = (y * 8 + x) * 4;
                    Assert.Equal(x < 4 ? (byte)0 : (byte)255, surface.Pixels[offset]);
                    Assert.Equal(x < 4 ? (byte)0 : (byte)255, surface.Pixels[offset + 3]);
                }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("group-alias")]
    [InlineData("nested-opacity")]
    [InlineData("mapped-strokes")]
    [InlineData("forward-use")]
    [InlineData("anonymous-and-siblings")]
    public void Retained_mask_groups_release_material_storage_on_denial_and_cancellation(string identity)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "mask-group-documents", identity + ".svg"));
        byte[] unchanged = input.ToArray();
        RenderBudget measured = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None, 1, 1, null))
        {
            long retained = measured.Snapshot().CurrentBytes;
            byte[] pixels;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None)) pixels = surface.Pixels.ToArray();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            {
                using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, cancellation.Token);
            }).CancellationToken);
            Assert.Equal(retained, measured.Snapshot().CurrentBytes);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None);
            Assert.Equal(pixels, retry.Pixels);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 16_383, measured.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new(limit);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, denied, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("<g id='p'><use href='#p'/></g>")]
    [InlineData("<g id='p'/><g id='p'/>")]
    public void Invalid_mask_group_reference_graphs_release_partial_source_owners(string children)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'>" + children + "</mask></defs></svg>");
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("maskUnits='userSpaceOnUse' x='2' y='2' width='8' height='8'")]
    [InlineData("maskUnits='objectBoundingBox' x='.125' y='.125' width='.5' height='.5'")]
    public void Placed_spatial_mask_regions_map_their_authored_unit_mode(string region)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m' " + region + "><g><rect width='16' height='16' fill='white'/></g></mask></defs><g transform='translate(5 7)'><rect width='16' height='16' fill='red' mask='url(#m)'/></g></svg>");
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    byte alpha = x >= 7 && x < 15 && y >= 9 && y < 17 ? (byte)255 : (byte)0;
                    Assert.Equal(alpha, surface.Pixels[(y * 32 + x) * 4]);
                    Assert.Equal(alpha, surface.Pixels[(y * 32 + x) * 4 + 3]);
                }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
