using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgDeferredPaintLeafTests
{
    [Theory]
    [InlineData(false, false, SvgErrorCode.UnsupportedFeature)]
    [InlineData(false, true, SvgErrorCode.UnresolvedResource)]
    [InlineData(true, false, SvgErrorCode.UnsupportedFeature)]
    [InlineData(true, true, SvgErrorCode.UnresolvedResource)]
    public async Task Earlier_paint_failures_keep_priority_over_a_later_missing_font(bool oval, bool defined, SvgErrorCode expected)
    {
        string shape = oval ? "<circle cx='10' cy='10' r='8' fill='url(#g)'/>" : "<rect width='20' height='20' fill='url(#g)'/>";
        string definition = defined ? "<defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs>" : string.Empty;
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'>" + shape + "<text font-family='Missing'>x</text>" + definition + "</svg>");
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(Array.Empty<SvgFontProgramResolution>(), CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgException failure = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None));
        Assert.Equal(expected, failure.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        if (defined) Assert.NotNull(failure.FontDiagnostic);
    }

    [Theory]
    [InlineData("linear-rectangle-forward")]
    [InlineData("radial-axis-map-forward")]
    [InlineData("linear-reflected-path-forward")]
    [InlineData("linear-skew-bounds-forward")]
    [InlineData("expanded-paint-use-order-forward")]
    [InlineData("expanded-paint-forward-group-clip")]
    [InlineData("expanded-paint-covering-leaf-mask")]
    [InlineData("expanded-paint-forward-leaf-mask")]
    public void Deferred_source_and_paint_owners_release_at_every_work_denial_and_sampled_memory_denials(string id)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "deferred-paint-leaves", id + ".svg"));
        RenderBudget discovery = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, discovery, CancellationToken.None));
        Assert.Equal(0, discovery.Snapshot().CurrentBytes);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
            Assert.NotEmpty(root.Geometries.ToArray());
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 256) DenyMemory(limit);
        DenyMemory(peak - 1);
        RenderBudget retry = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, retry, null, CancellationToken.None))
        {
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidOperationException("Missing scene storage.");
            SvgAuthoredPath[] paths = root.Geometries.Concat(storage.MaskContents.SelectMany(mask => mask.Geometries)).Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            SvgAuthoredPathCommand[][] original = paths.Select(path => path.Commands.ToArray()).ToArray();
            long owned = retry.Snapshot().CurrentBytes;
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
            {
                Assert.Contains(surface.Pixels, value => value != 0);
                expected = surface.Pixels.ToArray();
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
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, null, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData("missing-reference-boundary")]
    [InlineData("cyclic-template-boundary")]
    public void Rejected_deferred_resources_release_source_attributes_and_paint_owners(string id)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "deferred-paint-leaves", id + ".svg"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Duplicate_forward_gradients_release_both_registered_and_pending_paints()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'><rect width='40' height='30' fill='url(#p)'/><defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><linearGradient id='p'/></defs></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Empty_forward_leaf_retains_a_valid_nonpainting_command()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='' fill='url(#p)'/><defs><linearGradient id='p'><stop stop-color='red'/></linearGradient></defs></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.All(root.Commands, command => Assert.InRange(command.GeometryIndex, 0, root.Geometries.Length - 1));
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.All(surface.Pixels, value => Assert.Equal((byte)0, value));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Singular_source_paint_placement_has_a_categorized_outcome_and_releases_owners()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'><g transform='matrix(1,1,1,1,0,0)'><path d='M4 4L40 8L24 32Z' fill='url(#p)'/></g><defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Linear_fallback_inverts_composed_source_coordinates()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgSceneBuilder scene = new(budget, CancellationToken.None))
        {
            SvgGradientTransform local = new(3, 0, 0, 2, 4, -5);
            SvgAffineTransform reference = new(2, 0.5, 0.25, 3, 7, 11);
            SvgLinearPaintMap map = scene.RetainLinearPaintMap(new(0, 0, 1, 0, local, reference, true));
            (double x, double y) = local.Apply(0.25, 0.75);
            (x, y) = reference.ApplyToUserPoint(x, y);
            Assert.Equal(0.25, map.Position(x, y), 13);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Whole_geometric_pattern_releases_partial_owners_before_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "geometric-pattern.svg"));
        SvgRenderOptions options = new(new SvgViewport(284, 284), 1, SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(20_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, measured, CancellationToken.None))
        {
            Assert.Equal(284, surface.Width);
            Assert.Equal(284, surface.Height);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, deniedMemory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public void Cancelled_deferred_paint_replay_preserves_source_commands_and_identical_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "deferred-paint-leaves", "expanded-paint-forward-group-clip.svg"));
        SvgRenderOptions options = SvgRenderOptions.Default;
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(20_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgAuthoredPath[] paths = root.Geometries.Select(geometry => geometry.AuthoredPath).OfType<SvgAuthoredPath>().ToArray();
            Assert.NotEmpty(paths);
            SvgAuthoredPathCommand[][] commands = paths.Select(path => path.Commands.ToArray()).ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            foreach (int threshold in new[] { 1, 5 })
            {
                using CancellationTokenSource cancellation = new();
                int observed = 0;
                using (SvgAnalyticPaintProbe.Arm((phase, _) =>
                {
                    if (phase == SvgAnalyticPaintPhase.BeforeReplay && ++observed == threshold) cancellation.Cancel();
                }))
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    {
                        using SvgConverter.RasterizedSurface denied = SvgConverter.Rasterize(root, options, budget, cancellation.Token);
                    }).CancellationToken);
                Assert.Equal(threshold, observed);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            }
            for (int index = 0; index < paths.Length; index++) Assert.Equal(commands[index], paths[index].Commands.ToArray());
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
