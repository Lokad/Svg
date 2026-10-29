using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternTileCacheTests
{
    [Fact]
    public void Cache_borrows_matching_tiles_across_destination_rectangles_and_releases_owners()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        SvgPatternGeometry source = Pattern(new(0, 0, 4, 4, new(0, 0, 0, 255), true, null), "same");
        SvgConverter.RasterizedSurface surface = Surface(budget);
        using (SvgPatternTileCache cache = SvgPatternTileCache.Retain(source, source, surface, budget))
        {
            SvgPatternGeometry otherDestination = Pattern(new(4, 4, 2, 2, new(0, 0, 0, 255), true, null), "same");
            Assert.True(cache.MatchesSource(otherDestination));
            Assert.True(cache.TryBorrow(otherDestination, out SvgConverter.RasterizedSurface borrowed));
            Assert.Same(surface.Pixels, borrowed.Pixels);
            using (SvgPatternTileLease lease = new(borrowed, false)) { }
            Assert.True(budget.Snapshot().CurrentBytes > 64);
            cache.Dispose();
            Assert.False(cache.TryBorrow(source, out _));
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("origin")]
    [InlineData("placement")]
    [InlineData("viewBox")]
    [InlineData("sourceBox")]
    [InlineData("imageMap")]
    [InlineData("localMap")]
    [InlineData("paint")]
    [InlineData("content")]
    [InlineData("opacity")]
    public void Different_effective_tile_inputs_do_not_share(string change)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        SvgRectangle fill = new(0, 0, 8, 8, new(0, 0, 0, 255), true, null);
        SvgPatternGeometry source = Pattern(fill, "same"), changed = Pattern(fill, change);
        using SvgPatternTileCache cache = SvgPatternTileCache.Retain(source, source, Surface(budget), budget);
        Assert.False(cache.MatchesSource(changed));
        Assert.False(cache.TryBorrow(changed, out _));
    }

    [Fact]
    public void Different_vector_content_owners_do_not_share_even_with_matching_geometry()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        using SvgVectorContent first = SvgVectorContent.CreatePending(budget);
        using SvgVectorContent second = SvgVectorContent.CreatePending(budget);
        SvgRectangle fill = new(0, 0, 8, 8, new(0, 0, 0, 255), true, null);
        SvgPatternGeometry a = new(default, 0, 0, 4, 4, fill, 255, null) { VectorContent = first };
        SvgPatternGeometry b = new(default, 0, 0, 4, 4, fill, 255, null) { VectorContent = second };
        using SvgPatternTileCache cache = SvgPatternTileCache.Retain(a, a, Surface(budget), budget);
        Assert.False(cache.MatchesSource(b));
        Assert.False(cache.TryBorrow(b, out _));
    }

    [Fact]
    public void Refused_retention_leaves_the_surface_with_its_original_owner()
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(200));
        SvgPatternGeometry source = Pattern(default, "same");
        SvgConverter.RasterizedSurface surface = Surface(budget);
        SvgException refusal = Assert.Throws<SvgException>(() => SvgPatternTileCache.Retain(source, source, surface, budget));
        Assert.Equal(SvgErrorCode.LimitExceeded, refusal.ErrorCode);
        Assert.Equal(64, budget.Snapshot().CurrentBytes);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
        using (SvgPatternTileLease lease = new(surface, true)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rectangle_bands_refuse_storage_without_evicting_and_remain_reusable()
    {
        using SvgRoot root = BandRoot();
        RenderBudget normal = new RenderBudget(new HostMemoryBudget(10_000)), tight = new RenderBudget(new HostMemoryBudget(384));
        SvgException refusal = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, tight, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, refusal.ErrorCode);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        Assert.Equal(1, tight.Snapshot().DeniedReservationCount);
        using (SvgConverter.RasterizedSurface rendered = SvgConverter.Rasterize(root, SvgRenderOptions.Default, normal, CancellationToken.None))
        {
            Assert.Equal(8, rendered.Width);
            Assert.Equal(8, rendered.Height);
            Assert.Equal(256, normal.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, normal.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Failure_after_retention_releases_the_surface_and_keeps_borrowed_input_for_retry()
    {
        using SvgRoot root = BandRoot();
        SvgGeometry original = root.Geometries[1];
        root.Geometries[1] = SvgGeometry.FromPatternFill(Pattern(original.Pattern.Fill with { Width = double.PositiveInfinity }, "same"));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(10_000));
        SvgException error = Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        root.Geometries[1] = original;
        using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.Equal(8, retry.Width);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Alternating_different_tiles_keep_the_unretained_work_and_pixels()
    {
        using SvgRoot root = BandRoot();
        for (int index = 0; index < root.Geometries.Length; index++)
            root.Geometries[index] = SvgGeometry.FromPatternFill(Pattern(root.Geometries[index].Pattern.Fill, index % 2 == 0 ? "same" : "width"));
        RenderBudget normal = new RenderBudget(new HostMemoryBudget(10_000)), tight = new RenderBudget(new HostMemoryBudget(512));
        using (SvgConverter.RasterizedSurface current = SvgConverter.Rasterize(root, SvgRenderOptions.Default, normal, CancellationToken.None))
        using (SvgConverter.RasterizedSurface original = SvgConverter.Rasterize(root, SvgRenderOptions.Default, tight, CancellationToken.None))
        {
            Assert.Equal(original.Pixels, current.Pixels);
            Assert.Equal(tight.Snapshot().PeakBytes, normal.Snapshot().PeakBytes);
        }
        Assert.Equal(0, normal.Snapshot().CurrentBytes);
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
    }

    [Fact]
    public void A_different_draw_ends_retention_without_changing_pixels()
    {
        using SvgRoot bands = BandRoot();
        SvgGeometry[] geometries = [bands.Geometries[0], bands.Geometries[1], SvgGeometry.FromRectangle(new(0, 0, 1, 1, new(0, 0, 255, 128), true, null)), bands.Geometries[2], bands.Geometries[3]];
        SvgDrawCommand[] commands = Enumerable.Range(0, geometries.Length).Select(index => new SvgDrawCommand(SvgDrawCommandKind.Geometry, index, 255, null, -1, 255)).ToArray();
        using SvgRoot root = new(new(8, false), new(8, false), null, geometries, commands, false, null);
        // An explicit inert draw between each operation disables consecutive tile
        // retention without using admission refusal to select a rendering route.
        SvgGeometry[] controlGeometries = geometries.Append(SvgGeometry.FromRectangle(new(0, 0, 0, 0, SvgColor.Transparent, true, null))).ToArray();
        SvgDrawCommand inert = new(SvgDrawCommandKind.Geometry, geometries.Length, 255, null, -1, 255);
        SvgDrawCommand[] controlCommands = commands.SelectMany(command => new[] { command, inert }).ToArray();
        using SvgRoot control = new(new(8, false), new(8, false), null, controlGeometries, controlCommands, false, null);
        RenderBudget normal = new RenderBudget(new HostMemoryBudget(10_000)), unretained = new RenderBudget(new HostMemoryBudget(10_000));
        using (SvgConverter.RasterizedSurface current = SvgConverter.Rasterize(root, SvgRenderOptions.Default, normal, CancellationToken.None))
        using (SvgConverter.RasterizedSurface original = SvgConverter.Rasterize(control, SvgRenderOptions.Default, unretained, CancellationToken.None))
        {
            Assert.Equal(original.Pixels, current.Pixels);
        }
        Assert.Equal(0, normal.Snapshot().CurrentBytes);
        Assert.Equal(0, unretained.Snapshot().CurrentBytes);
    }

    private static SvgConverter.RasterizedSurface Surface(RenderBudget budget)
        => new(4, 4, new byte[64], budget.Reserve(64, SvgMemoryCategory.Surface));

    private static SvgPatternGeometry Pattern(SvgRectangle fill, string change)
        => new(new SvgRectangle(0, 0, 2, 2, change == "content" ? new(0, 0, 255, 255) : new(255, 0, 0, 255), true, null), change == "origin" ? 1 : 0, 0, change == "width" ? 8 : 4, change == "height" ? 8 : 4, fill, change == "opacity" ? (byte)128 : (byte)255, null)
        {
            ShaderPlacement = change == "placement" ? SvgAffineTransform.FromScaleOffset(2, 1, 0, 0) : SvgAffineTransform.Identity,
            ContentViewBox = change == "viewBox" ? new(0, 0, 4, 4, default, default, default) : null,
            RasterSourceBox = change == "sourceBox" ? new(0, 0, 4, 4, default, default, default) : null,
            NativeImageMap = change == "imageMap" ? new(2, 0, 0, 0, 2, 0) : null,
            NativeImageLocal = change == "localMap" ? new(1, 0, 1, 0, 1, 0) : null,
            ContentPaint = change == "paint" ? new(new(255, 0, 0, 255), .5f) : null,
        };

    private static SvgRoot BandRoot()
    {
        SvgRectangle[] fills = [new(0, 0, 8, 2, default, true, null), new(0, 6, 8, 2, default, true, null), new(0, 2, 2, 4, default, true, null), new(6, 2, 2, 4, default, true, null)];
        SvgGeometry[] geometries = fills.Select(fill => SvgGeometry.FromPatternFill(Pattern(fill, "same"))).ToArray();
        SvgDrawCommand[] commands = Enumerable.Range(0, geometries.Length).Select(index => new SvgDrawCommand(SvgDrawCommandKind.Geometry, index, 255, null, -1, 255)).ToArray();
        return new(new(8, false), new(8, false), null, geometries, commands, false, null);
    }
}
