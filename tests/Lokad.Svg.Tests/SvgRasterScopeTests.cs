using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgRasterScopeTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(9, 9)]
    public void Cropped_storage_does_not_rebase_its_logical_bitmap(int x, int y)
    {
        SvgCpuRasterContext tile = new(new(0, 0, 8, 8), new(0, 0, 8, 8));
        SvgCpuRasterScope scope = new(new(2, 3, 6, 7), true) { NativeOrigin = new(x, y) };
        SvgCpuRasterContext bound = scope.Bind(tile);
        Assert.Equal(scope.Bounds, bound.BitmapClip);
        Assert.Equal(scope.Bounds, bound.Interior);
        Assert.Equal(new SvgCpuBitmapOrigin(x, y), bound.Origin);
    }

    [Fact]
    public void Native_clip_classification_does_not_expand_addressable_storage()
    {
        SvgCpuRasterContext tile = new(new(0, 0, 8, 8), new(0, 0, 8, 8));
        SvgAnalyticClipBounds native = new(-10, -11, 20, 21);
        SvgCpuRasterScope scope = new(new(2, 3, 6, 7), true)
        { NativeOrigin = new(-1, 0), NativeClipBounds = native };
        SvgCpuRasterContext bound = scope.Bind(tile);
        Assert.Equal(new SvgAnalyticClipBounds(2, 3, 6, 7), bound.BitmapClip);
        Assert.Equal(bound.BitmapClip, bound.Interior);
        Assert.Equal(new SvgCpuBitmapOrigin(-1, 0), bound.Origin);
        Assert.Equal(SvgAnalyticPaintClip.Binary,
            SvgAnalyticRectangleClip.Classify(.25, .25, 7.75, 7.75, bound.BitmapClip, SvgClipAntialiasing.All));
        Assert.Equal(SvgAnalyticPaintClip.Antialiased,
            SvgAnalyticRectangleClip.Classify(.25, .25, 7.75, 7.75, native, SvgClipAntialiasing.All));
    }

    [Theory]
    [InlineData(false, 260, 7)]
    [InlineData(true, 254, 0)]
    public void Narrowed_layers_and_source_content_retain_distinct_bitmap_origins(bool retained, int x, int y)
    {
        SvgCpuRasterContext tile = new(new(254, 0, 510, 256), new(255, 0, 509, 255));
        SvgCpuRasterScope scope = new(new(260, 7, 300, 200), retained);
        SvgCpuRasterContext bound = scope.Bind(tile);
        Assert.Equal(new SvgAnalyticClipBounds(260, 7, 300, 200), bound.BitmapClip);
        Assert.Equal(bound.BitmapClip, bound.Interior);
        Assert.Equal(new SvgCpuBitmapOrigin(x, y), bound.Origin);
    }

    [Fact]
    public void Grid_borders_keep_their_owned_interior_when_the_scope_does_not_narrow_them()
    {
        SvgCpuRasterContext tile = new(new(254, 0, 510, 256), new(255, 0, 509, 255));
        SvgCpuRasterContext bound = new SvgCpuRasterScope(new(0, 0, 600, 300), false).Bind(tile);
        Assert.Equal(tile, bound);
    }

    [Fact]
    public void Disjoint_scope_returns_empty_construction_and_paint_windows()
    {
        SvgCpuRasterContext tile = new(new(254, 0, 510, 256), new(255, 0, 509, 255));
        SvgCpuRasterContext bound = new SvgCpuRasterScope(new(0, 0, 100, 100), true).Bind(tile);
        Assert.Equal(bound.BitmapClip.Left, bound.BitmapClip.Right);
        Assert.Equal(bound.Interior.Left, bound.Interior.Right);
        Assert.Equal(tile.Origin, bound.Origin);
    }
}
