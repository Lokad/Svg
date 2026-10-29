using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListViewportTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
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
    [Trait("Layer", "Mechanism")]
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
    [Trait("Layer", "Mechanism")]
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
    [Trait("Layer", "Mechanism")]
    public void Source_viewport_retention_keeps_the_existing_transform_admission_limit()
    {
        SvgViewBox box = new(2147483648, 0, 16, 32, SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
        SvgUseReference reference = new("s", 4, 0, 24, 32, SvgAffineTransform.Identity,
            SvgAffineTransform.Identity, 255, true, null, null, null);
        Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => SvgReferenceResolver.SymbolViewport(box, reference, null)).ErrorCode);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void Finite_intermediate_maps_can_exceed_the_complete_map_admission_range(double placement, double outputScale)
    {
        SvgAffineTransform source = SvgAffineTransform.FromScale(placement, placement);
        SvgViewportDrawMap stage = new(SvgAffineTransform.FromScale(placement * 1_000_000_000, placement * 1_000_000_000),
            SvgAffineTransform.FromScale(0.000000001, 0.000000001), source);
        Assert.Equal(SvgAffineTransform.FromScale(3, 3), stage.Prepare(SvgAffineTransform.FromScale(outputScale, outputScale), source));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Complete_viewport_draw_maps_keep_the_existing_range_limit()
    {
        SvgAffineTransform source = SvgAffineTransform.FromScale(1_000_000_000, 1_000_000_000);
        SvgViewportDrawMap stage = new(source, SvgAffineTransform.Identity, source);
        Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => stage.Prepare(SvgAffineTransform.FromScale(3, 3), source)).ErrorCode);
    }

    [Fact]
    [Trait("Layer", "Rendering")]
    public void Large_parent_and_small_viewbox_render_through_output_scaling()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><symbol id='s' viewBox='0 0 32 32'><rect width='32' height='32' fill='red'/></symbol></defs><use href='#s' width='.000000032' height='.000000032' transform='scale(1000000000)'/></svg>"u8.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(null, 3, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
        {
            Assert.Equal(96, surface.Width);
            Assert.Equal(96, surface.Height);
            byte[] expected = new byte[96 * 96 * 4];
            for (int index = 0; index < expected.Length; index += 4)
                expected[index] = expected[index + 3] = 255;
            Assert.Equal(expected, surface.Pixels);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
