using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRootImageMapTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(.75)]
    [InlineData(1)]
    [InlineData(1.5)]
    public void Root_layout_retains_the_native_viewbox_map_before_uniform_output_scaling(double scale)
    {
        SvgViewBox box = new(0, 0, 24, 16, SvgViewBoxAlignment.Mid, SvgViewBoxAlignment.Mid, SvgViewBoxFit.Meet);
        SvgRootImageMap baseline = SvgRootImageMap.Resolve(box, new(17.4, 11.4), 1);
        SvgRootImageMap actual = SvgRootImageMap.Resolve(box, new(17.4, 11.4), scale);
        // Independent DOM/image controls in Research/svgweb/root-viewport-review.json
        // retain this 1/64-layout viewBox map and its painted counterexample.
        Assert.Equal(.7119140625, actual.Layout.A);
        Assert.Equal(.7119140625, actual.Layout.D);
        Assert.Equal(.15234375f, (float)actual.Layout.E);
        Assert.Equal(0, actual.Layout.F);
        Assert.Equal(baseline.Layout, actual.Layout);
        Assert.Equal(baseline.Projection.A * scale, actual.Projection.A, 14);
        Assert.Equal(baseline.Projection.D * scale, actual.Projection.D, 14);
        Assert.Equal(new SvgClipBox(0, 0, 17 * scale, 11 * scale), actual.Boundary);
    }

    [Fact]
    [Trait("Layer", "Rendering")]
    public void A_single_overflowing_paint_keeps_the_root_image_boundary()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='17' height='11'><rect x='-2' y='-2' width='30' height='30' fill='white'/></svg>"u8.ToArray();
        byte[] pixels = SvgRasterTestHelper.RenderPixels(input,
            new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default), out int width, out int height);
        Assert.Equal((26, 17), (width, height));
        Assert.Equal(new byte[] { 255, 255, 255, 128 }, pixels.AsSpan((10 * width + 25) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 255, 255, 255, 64 }, pixels.AsSpan((16 * width + 25) * 4, 4).ToArray());
    }

    [Fact]
    [Trait("Layer", "Rendering")]
    public void Subpixel_natural_dimensions_preserve_the_admitted_scaled_canvas()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='.4' height='1'><rect width='10' height='10' fill='white'/></svg>"u8.ToArray();
        byte[] pixels = SvgRasterTestHelper.RenderPixels(input,
            new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default), out int width, out int height);
        Assert.Equal((1, 2), (width, height));
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 }, pixels);
    }
}
