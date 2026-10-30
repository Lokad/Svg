using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRadialInheritanceTests
{
    private const string Stops = "<stop stop-color='#e04020'/><stop offset='1' stop-color='#2040e0'/>";

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("cx='.6'", .6, .5, .45, .35, .4, .05)]
    [InlineData("cy='.6'", .5, .6, .45, .35, .4, .05)]
    [InlineData("r='.35'", .5, .5, .35, .35, .4, .05)]
    [InlineData("fx='.4'", .5, .5, .45, .4, .4, .05)]
    [InlineData("fy='.55'", .5, .5, .45, .35, .55, .05)]
    [InlineData("fr='.1'", .5, .5, .45, .35, .4, .1)]
    public void Coordinates_merge_independently_before_transform_and_forward_alias_resolution(
        string attributes, double cx, double cy, double radius, double fx, double fy, double fr)
    {
        const string transform = "matrix(1 .25 .125 1 .0625 .03125)";
        byte[] input = Document("<radialGradient id='g' href='#middle' " + attributes + "/>"
            + "<radialGradient id='middle' href='#base'/>"
            + "<radialGradient id='base' cx='.5' cy='.5' r='.45' fx='.35' fy='.4' fr='.05' gradientTransform='"
            + transform + "'>" + Stops + "</radialGradient>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradientSource source = Assert.IsType<SvgRadialGradientSource>(Assert.Single(root.Geometries).Radial.Gradient.SourceDefinition);
        Assert.Equal((cx, cy, radius, fx, fy, fr),
            (source.CenterX, source.CenterY, source.Radius, source.FocalX, source.FocalY, source.FocalRadius));
        Assert.Equal(new SvgGradientTransform(1, .25, .125, 1, .0625, .03125), source.Transform);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void An_unspecified_focus_follows_the_overridden_center()
    {
        byte[] input = Document("<radialGradient id='base'>" + Stops + "</radialGradient>"
            + "<radialGradient id='g' href='#base' cx='.65'/>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradient gradient = Assert.Single(root.Geometries).Radial.Gradient;
        SvgRadialGradientSource source = Assert.IsType<SvgRadialGradientSource>(gradient.SourceDefinition);
        Assert.False(source.HasFocalX);
        Assert.Equal(.65, source.FocalX);
        Assert.Equal(.65, gradient.FocalX);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void A_changed_focus_owns_new_definition_storage_and_aliases_borrow_it()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='8'><defs>"
            + "<radialGradient id='base' cx='.5' cy='.5' r='.5' fx='.4'>" + Stops + "</radialGradient>"
            + "<radialGradient id='changed' href='#base' fx='.3'/><radialGradient id='alias' href='#changed'/>"
            + "</defs><rect width='8' height='8' fill='url(#base)'/><rect x='8' width='8' height='8' fill='url(#changed)'/>"
            + "<rect x='16' width='8' height='8' fill='url(#alias)'/></svg>");
        RenderBudget budget = new(new HostMemoryBudget(268_435_456));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            Assert.Equal(3, root.Geometries.Length);
            SvgRadialGradientSource original = Assert.IsType<SvgRadialGradientSource>(root.Geometries[0].Radial.Gradient.SourceDefinition);
            SvgRadialGradientSource changed = Assert.IsType<SvgRadialGradientSource>(root.Geometries[1].Radial.Gradient.SourceDefinition);
            Assert.NotSame(original, changed);
            Assert.Same(changed, root.Geometries[2].Radial.Gradient.SourceDefinition);
            Assert.Equal(.4, original.FocalX);
            Assert.Equal(.3, changed.FocalX);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void A_partial_focus_resolves_inherited_user_space_percentages()
    {
        byte[] input = Document("<radialGradient id='base' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='16'>"
            + Stops + "</radialGradient><radialGradient id='g' href='#base' fx='50%'/>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(16, Assert.Single(root.Geometries).Radial.Gradient.FocalX);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("radialGradient", "gradientUnits='userSpaceOnUse' cx='16'", 16, 16, 16)]
    [InlineData("linearGradient", "cx='.4'", .4, .5, .5)]
    public void Partial_vectors_keep_inherited_units_and_cross_type_defaults(string parent, string attributes, double x, double y, double radius)
    {
        byte[] input = Document("<" + parent + " id='base'>" + Stops + "</" + parent + ">"
            + "<radialGradient id='g' href='#base' " + attributes + "/>");
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, CancellationToken.None);
        SvgRadialGradientSource source = Assert.IsType<SvgRadialGradientSource>(Assert.Single(root.Geometries).Radial.Gradient.SourceDefinition);
        Assert.Equal((x, y, radius), (source.CenterX, source.CenterY, source.Radius));
    }

    private static byte[] Document(string definitions)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + definitions + "</defs><rect width='32' height='32' fill='url(#g)'/></svg>");
}
