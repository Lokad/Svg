using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRadialSourceBindingTests
{
    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Binding_an_axis_resolved_paint_keeps_definition_coordinates_and_local_map_separate()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='80'><defs><radialGradient id='r' gradientUnits='userSpaceOnUse' cx='40' cy='35' r='27' gradientTransform='matrix(1 0 0 2 0 -10)'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient></defs><rect width='96' height='80' fill='url(#r)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradient original = Assert.Single(root.Geometries).Radial.Gradient;
        SvgRadialGradientSource definition = Assert.IsType<SvgRadialGradientSource>(original.SourceDefinition);
        Assert.False(original.HasGeneralTransform);
        Assert.Equal((40d, 60d, 27d), (original.CenterX, original.CenterY, original.Radius));

        SvgAffineTransform placement = new(.5, 0, .25, 1, 8, 12);
        SvgRadialGradient bound = SvgGradientSyntax.BindRadialSourceMap(original,
            new(0, 0, 96, 80, default, true, null), placement, CancellationToken.None);

        Assert.Same(definition, bound.SourceDefinition);
        Assert.Equal((40d, 35d, 27d, 40d, 35d, 0d),
            (bound.CenterX, bound.CenterY, bound.Radius, bound.FocalX, bound.FocalY, bound.FocalRadius));
        Assert.Equal((1d, 1d), (bound.ReferenceScaleX, bound.ReferenceScaleY));
        Assert.True(bound.HasGeneralTransform);
        Assert.Equal(new SvgGradientTransform(2, 0, -.5, .5, -10, -1), bound.Inverse);
        SvgGradientPaintMatrices matrices = Assert.IsType<SvgGradientPaintMatrices>(bound.NativePaintMatrices);
        Assert.Equal(new SvgShaderMatrix(1, 0, 0, 0, 2, -10), matrices.Local);
        Assert.Equal(placement, matrices.Referencing);
        Assert.Equal((40d, 60d, 27d), (original.CenterX, original.CenterY, original.Radius));
        Assert.False(original.HasGeneralTransform);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Axis_placed_paths_bind_definition_coordinates_before_drawing_placement()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='80'><defs><radialGradient id='r' gradientUnits='userSpaceOnUse' cx='40' cy='35' r='27' gradientTransform='matrix(1 0 0 2 0 -10)'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient></defs><path d='M0 0 H80 V70 H0 Z' fill='url(#r)' transform='translate(5 4)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradient gradient = Assert.Single(root.Geometries).RadialPolygon.Gradient;
        Assert.True(gradient.HasGeneralTransform);
        Assert.Equal((40d, 35d, 27d), (gradient.CenterX, gradient.CenterY, gradient.Radius));
        Assert.Equal(new SvgGradientTransform(1, 0, 0, .5, -5, 3), gradient.Inverse);
        SvgGradientPaintMatrices matrices = Assert.IsType<SvgGradientPaintMatrices>(gradient.NativePaintMatrices);
        Assert.Equal(new SvgShaderMatrix(1, 0, 0, 0, 2, -10), matrices.Local);
        Assert.Equal(SvgAffineTransform.FromScaleOffset(1, 1, 5, 4), matrices.Referencing);
    }
}
