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
        Assert.Equal(baseline.Destination, actual.Destination);
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

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(16.245, 16.554, 0.9849183559417725f, 1.0269420146942139f, -1008.556396484375f, -1051.588623046875f)]
    [InlineData(16.522625, 21.60075, 1.0288922786712646f, 1.0184831619262695f, -1053.585693359375f, -1042.9267578125f)]
    [InlineData(19.60075, 19.60075, 1.020369052886963f, 1.020369052886963f, -1044.85791015625f, -1044.85791015625f)]
    public void Recorded_root_matrix_matches_native_before_image_destination(double width, double height,
        float expectedScaleX, float expectedScaleY, float expectedTranslateX, float expectedTranslateY)
    {
        // Independent numerical LayerTree observations, including the identity
        // destination control, are in Research/svgweb/root-image-projection.json.
        SvgRootImageMap root = SvgRootImageMap.Resolve(null, new(width, height), 1);
        SvgAffineTransform recorded = root.Projection.Compose(root.Layout).ComposeChecked(SvgAffineTransform.FromTranslation(-1024, -1024));
        Assert.Equal(expectedScaleX, (float)recorded.A);
        Assert.Equal(expectedScaleY, (float)recorded.D);
        Assert.Equal(expectedTranslateX, (float)recorded.E);
        Assert.Equal(expectedTranslateY, (float)recorded.F);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(16.245, 16.554, 1.0f, 1.0f)]
    [InlineData(16.522625, 21.60075, 0.9999998807907104f, 0.9999999403953552f)]
    [InlineData(19.60075, 19.60075, 1.0000001192092896f, 1.0000001192092896f)]
    public void Image_destination_keeps_native_float_cancellation(double width, double height, float expectedX, float expectedY)
    {
        SvgRootImageMap root = SvgRootImageMap.Resolve(null, new(width, height), 1);
        Assert.Equal(expectedX, root.Destination.A);
        Assert.Equal(expectedY, root.Destination.D);
        Assert.Equal(SvgAffineTransform.Identity with { A = expectedX, D = expectedY }, root.Destination);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Uniform_density_joins_the_recorded_viewbox_scene_before_native_narrowing()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='80' viewBox='0 0 100 100'><g transform='rotate(20 90 75)'><path d='M10 75 A40 40 0 0 1 90 75' fill='#EEE'/></g></svg>"u8.ToArray();
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries.ToArray()).AuthoredPath);
        SvgDrawingMap drawing = SvgRootImageMap.Resolve(scene.ViewBox, new(80, 80), 15).Drawing;
        // Native LayerTree matrix for the unchanged dome-solid-path-capture-1200.svg.
        // Its external image matrix is identity; density joins the recorded scene.
        Assert.Equal(new SvgAffineTransform(11.276311874389648, 4.104241847991943,
            -4.104241847991943, 11.276311874389648, 372.9501037597656, -315.1051025390625), drawing.MapScene(path.SceneMap));
    }
}
