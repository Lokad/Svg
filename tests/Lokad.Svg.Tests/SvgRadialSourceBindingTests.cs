using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRadialSourceBindingTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<rect x='2' y='3' width='80' height='60' fill='url(#r)'/>")]
    [InlineData("<rect x='2' y='3' width='80' height='60' rx='7' fill='url(#r)'/>")]
    [InlineData("<ellipse cx='42' cy='33' rx='40' ry='30' fill='url(#r)'/>")]
    [InlineData("<path d='M2 3 H82 V63 H2 Z' fill='url(#r)'/>")]
    public void Primitive_and_path_factories_keep_the_same_unplaced_definition(string shape)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='80'><defs><radialGradient id='r' gradientUnits='userSpaceOnUse' cx='19.3125' cy='15.625' r='3.75' gradientTransform='matrix(.8125 0 0 1.3125 -3.375 -5.875)' spreadMethod='reflect'><stop stop-color='#315379'/><stop offset='1' stop-color='#97b5d3'/></radialGradient></defs>"
            + shape + "</svg>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgRadialGradient gradient = geometry.Kind == SvgGeometryKind.RadialGradient ? geometry.Radial.Gradient : geometry.RadialPolygon.Gradient;
        SvgRadialGradientSource definition = Assert.IsType<SvgRadialGradientSource>(gradient.SourceDefinition);
        Assert.Equal((19.3125, 15.625, 3.75, 19.3125, 15.625, 0d),
            (gradient.CenterX, gradient.CenterY, gradient.Radius, gradient.FocalX, gradient.FocalY, gradient.FocalRadius));
        Assert.Equal((definition.CenterX, definition.CenterY, definition.Radius), (gradient.CenterX, gradient.CenterY, gradient.Radius));
        Assert.Equal((1d, 1d), (gradient.ReferenceScaleX, gradient.ReferenceScaleY));
        SvgGradientPaintMatrices maps = Assert.IsType<SvgGradientPaintMatrices>(gradient.NativePaintMatrices);
        Assert.Equal(new SvgShaderMatrix(.8125f, 0, -3.375f, 0, 1.3125f, -5.875f), maps.Local);
        Assert.Equal(SvgAffineTransform.Identity, maps.Referencing);
        Assert.True(SvgShaderCoordinates.UsesRadialFactory(gradient));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Rebinding_a_primitive_keeps_definition_coordinates_and_local_map_separate()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='80'><defs><radialGradient id='r' gradientUnits='userSpaceOnUse' cx='40' cy='35' r='27' gradientTransform='matrix(1 0 0 2 0 -10)'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient></defs><rect width='96' height='80' fill='url(#r)'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradient original = Assert.Single(root.Geometries).Radial.Gradient;
        SvgRadialGradientSource definition = Assert.IsType<SvgRadialGradientSource>(original.SourceDefinition);
        Assert.True(original.HasGeneralTransform);
        Assert.Equal((40d, 35d, 27d), (original.CenterX, original.CenterY, original.Radius));

        SvgGradientPaintMatrices originalMaps = Assert.IsType<SvgGradientPaintMatrices>(original.NativePaintMatrices);
        Assert.Equal(new SvgShaderMatrix(1, 0, 0, 0, 2, -10), originalMaps.Local);
        Assert.Equal(SvgAffineTransform.Identity, originalMaps.Referencing);
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
        Assert.Equal((40d, 35d, 27d), (original.CenterX, original.CenterY, original.Radius));
        Assert.True(original.HasGeneralTransform);
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
