using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListViewportTests
{
    [Theory]
    [InlineData(16777215, 16, 16777215, 17)]
    [InlineData(2147483648, 1024, 2147483648, 1024)]
    [InlineData(-2147483519, 1024, -2147483520, 1024)]
    [InlineData(1073741824, 16, 1073741824, 0)]
    public void Viewport_retains_independently_rounded_source_endpoints(double x, double width, double expectedLeft, double expectedWidth)
    {
        SvgViewBox box = new(x, 0, width, 32, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        SvgUseReference reference = new("s", 4, 0, 24, 32, SvgAffineTransform.Identity,
            SvgAffineTransform.Identity, 255, true, null, null, null);
        SvgAffineViewportClip clip = SvgReferenceResolver.SymbolViewport(box, reference, null);
        Assert.Equal(new SvgClipBox(expectedLeft, 0, expectedWidth, 32), clip.Box);
        Assert.False(clip.IsBinary);
    }

    [Fact]
    public void Nested_symbol_placement_rounds_the_viewbox_map_before_canvas_concatenation()
    {
        SvgViewBox box = new(16777215, 0, 16, 32, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        SvgAffineTransform parent = new(0.875, 0, 0, 0.875, 2, 1);
        SvgUseReference reference = new("s", 4, 0, 24, 32, parent,
            SvgAffineTransform.Identity, 255, true, null, null, null);
        SvgAffineViewportClip clip = SvgReferenceResolver.SymbolViewport(box, reference, null);
        Assert.Equal(new SvgAffineTransform(1.3125, 0, 0, 0.875, -22020089.1875, 1), clip.Map);
        SvgViewportDrawMap stage = Assert.IsType<SvgViewportDrawMap>(clip.DrawMap);
        Assert.Equal(clip.Map, stage.Source);
        SvgAffineTransform device = stage.Prepare(SvgAffineTransform.Identity, clip.Map);
        Assert.Equal(new SvgAffineTransform(1.3125, 0, 0, 0.875, -22020088, 1), device);
        Assert.Equal(new SvgDevicePoint(6, 4.5), SvgAuthoredPath.MapPoint(device, new(16777215, 4), new(0, 0)));
    }

    [Theory]
    [InlineData(0.5, 0.35f, 2.4f, 1.9f)]
    [InlineData(1.5, 1.05f, 7.2f, 5.7f)]
    public void Output_placement_precedes_viewbox_rounding(double outputScale, float expectedScale, float expectedX, float expectedY)
    {
        SvgViewBox box = new(-4, -4, 40, 40, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        SvgUseReference reference = new("s", 2, 1, 28, 28, SvgAffineTransform.Identity,
            SvgAffineTransform.Identity, 255, true, null, null, null);
        SvgAffineViewportClip clip = SvgReferenceResolver.SymbolViewport(box, reference, null);
        SvgViewportDrawMap stage = Assert.IsType<SvgViewportDrawMap>(clip.DrawMap);
        SvgAffineTransform actual = stage.Prepare(SvgAffineTransform.FromScale(outputScale, outputScale), clip.Map);
        Assert.Equal(new SvgAffineTransform(expectedScale, 0, 0, expectedScale, expectedX, expectedY), actual);
    }

    [Fact]
    public void Source_viewport_retention_keeps_the_existing_transform_admission_limit()
    {
        SvgViewBox box = new(2147483648, 0, 16, 32, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        SvgUseReference reference = new("s", 4, 0, 24, 32, SvgAffineTransform.Identity,
            SvgAffineTransform.Identity, 255, true, null, null, null);
        Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => SvgReferenceResolver.SymbolViewport(box, reference, null)).ErrorCode);
    }
}
