using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskPaintRegionTests
{
    [Theory]
    [InlineData(0, 7, 0, 7, .25, .25, 7)]
    [InlineData(-.25, 8, -.25, 8, .25, -.75, 9)]
    [InlineData(0, 7, -1, 9, .25, -.75, 9)]
    [InlineData(0, 7, -1, 9, .75, -.25, 9)]
    public void Paint_enclosure_uses_separate_object_and_visual_bounds(
        double objectX, double objectWidth, double visualX, double visualWidth, double translation, double expectedX, double expectedWidth)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        {
            state.MaskRegions.TryAdd("mask", new(0, 0, 7, 8));
            SvgAffineTransform map = SvgAffineTransform.FromScaleOffset(1, 1, translation, 0);
            SvgRectangle objects = map.Apply(new SvgRectangle(objectX, 0, objectWidth, 8, default, false, null));
            SvgPaintBounds visual = new SvgPaintBounds((float)visualX, 0, (float)(visualX + visualWidth), 8).Map(map, CancellationToken.None);
            SvgClipBox sceneRegion = map.Apply(new SvgClipBox(0, 0, 7, 8));
            SvgClipBox actual = Assert.IsType<SvgClipBox>(SvgMaskTarget.SourcePaintRegion("mask", map, objects, visual, sceneRegion, state, CancellationToken.None));
            Assert.Equal(new SvgClipBox(expectedX, 0, expectedWidth, 8), actual);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retained_region_surface_is_accounted_and_memory_refusal_leaves_the_scene_replayable()
    {
        RenderBudget scene = new RenderBudget(new HostMemoryBudget(1_000_000)), measured = new RenderBudget(new HostMemoryBudget(1_000_000)), refused = new RenderBudget(new HostMemoryBudget(512)), retry = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Create(scene))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, measured, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            Assert.Equal(1024, measured.Snapshot().PeakBytes);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.Rasterize(root, SvgRenderOptions.Default, refused, CancellationToken.None)).ErrorCode);
            Assert.Equal(512, refused.Snapshot().PeakBytes);
            Assert.Equal(1, refused.Snapshot().DeniedReservationCount);
            Assert.Equal(0, refused.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, retry, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels.ToArray());
            Assert.Equal(0, retry.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, scene.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Retained_region_cancellation_releases_every_owner_before_retry()
    {
        RenderBudget scene = new RenderBudget(new HostMemoryBudget(1_000_000)), raster = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Create(scene))
        {
            byte[] expected;
            using (SvgConverter.RasterizedSurface measured = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None))
                expected = measured.Pixels.ToArray();
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, cancelled.Token));
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None))
                Assert.Equal(expected, surface.Pixels.ToArray());
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, scene.Snapshot().CurrentBytes);
    }

    private static SvgRoot Create(RenderBudget budget)
    {
        using SvgSceneBuilder builder = new(budget, CancellationToken.None);
        using SvgSceneBuilder maskBuilder = new(budget, CancellationToken.None);
        maskBuilder.AddGeometry(SvgGeometry.FromRectangle(new(0, 0, 8, 8, new(255, 255, 255, 186), true, null)));
        SvgVectorContent vector = SvgVectorContent.Create(maskBuilder.Build(), budget);
        try { builder.AdoptVectorContent(vector); }
        catch { vector.Dispose(); throw; }
        int content = builder.AddMaskContent(new([], 255, true) { VectorContent = vector, SourcePaintRegion = new(.25, 0, 7, 8) });
        builder.InsertCommands(0, [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, new(0, 0, 8, 8), content, 255)]);
        builder.AddGeometry(SvgGeometry.FromRectangle(new(0, 0, 8, 8, new(255, 0, 0, 17), true, null)));
        builder.EndOpacityScope();
        SvgSceneStorage storage = builder.Build();
        return new(new(8, false), new(8, false), null, storage.Geometries, storage.Commands, false, storage);
    }
}
