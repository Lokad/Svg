using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterLengthTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData(".1", 0.10000000149011612)]
    [InlineData("33554427", 33_554_428)]
    [InlineData("33554428", 33_554_428)]
    [InlineData("33554429", 33_554_428)]
    [InlineData("33554430", 33_554_428)]
    [InlineData("33554432px", 33_554_428)]
    [InlineData("2147483648", 33_554_428)]
    [InlineData("1e300", 33_554_428)]
    [InlineData("-33554428", -33_554_428)]
    [InlineData("-33554429", -33_554_428)]
    [InlineData("-33554430", -33_554_430)]
    [InlineData("-33554431", -33_554_430)]
    [InlineData("-33554432px", -33_554_430)]
    [InlineData("-1e300", -33_554_430)]
    public void Fixed_region_and_primitive_lengths_are_bounded_before_coordinate_resolution(string token, double expected)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgFilterResource resource = Read(token, budget))
        {
            SvgFilterLength length = new(expected, false);
            Assert.Equal(new(length, length, length, length, SvgFilterUnits.UserSpace), resource.Region);
            Assert.Equal(new SvgFilterPrimitiveRegion(length, length, length, length), resource.Primitives.Items[0].Region);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(".1%", .1)]
    [InlineData("33554432%", 33_554_432)]
    [InlineData("-33554432%", -33_554_432)]
    [InlineData("1e300%", 1e300)]
    public void Percentage_lengths_keep_their_viewport_relative_domain(string token, double expected)
    {
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgFilterResource resource = Read(token, budget))
        {
            SvgFilterLength length = new(expected, true);
            Assert.Equal(new(length, length, length, length, SvgFilterUnits.UserSpace), resource.Region);
            Assert.Equal(new SvgFilterPrimitiveRegion(length, length, length, length), resource.Primitives.Items[0].Region);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("translate(33554432 -33554432)", 33_554_428, -33_554_430)]
    [InlineData("matrix(1 0 0 1 33554432 -33554432)", 33_554_432, -33_554_432)]
    [InlineData("translate(33554428 -33554428)", 33_554_428, -33_554_428)]
    [InlineData("translate(33554432 0) translate(-33554428 0)", 0, 0)]
    [InlineData("matrix(1 0 0 1 33554432 0) translate(-33554428 0)", 4, 0)]
    [InlineData("scale(2) translate(33554432 0)", 67_108_856, 0)]
    public void Translate_lengths_resolve_before_composition_while_matrix_coefficients_keep_their_number_domain(string source, double x, double y)
    {
        SvgAffineTransform transform = SvgGeometrySyntax.ParseTransform(source, CancellationToken.None);
        Assert.Equal((x, y), (transform.E, transform.F));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Filter_object_boxes_survive_coincident_placed_endpoints(bool ownFilter)
    {
        const string placement = " transform='matrix(1 0 0 1 -67108848 0)' filter='url(#f)'";
        string shape = ownFilter ? "<rect width='2' height='2'" + placement + "/>"
            : "<g" + placement + "><rect width='2' height='2'/></g>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>"
            + "<defs><filter id='f'><feFlood flood-color='green'/></filter></defs>" + shape + "</svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgDrawCommand filter = Assert.Single(scene.Commands, command => command.Kind == SvgDrawCommandKind.BeginGroup && command.RegionKind == SvgLayerRegionKind.Filter);
            SvgFilterArena arena = scene.FilterResources ?? throw new InvalidOperationException();
            SvgFilterBinding binding = arena.BindingAt(filter.MaskContentIndex);
            Assert.Equal(binding.ObjectBounds.Left, binding.ObjectBounds.Right);
            Assert.Equal(new SvgPaintBounds(0, 0, 2, 2), binding.ParameterObjectBounds);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgFilterResource Read(string token, RenderBudget budget)
    {
        string region = " x='" + token + "' y='" + token + "' width='" + token + "' height='" + token + "'";
        using XmlReader reader = XmlReader.Create(new StringReader("<filter xmlns='http://www.w3.org/2000/svg' id='f' filterUnits='userSpaceOnUse'" + region
            + "><feFlood" + region + "/></filter>"));
        Assert.True(reader.ReadToFollowing("filter"));
        using SvgFilterDefinitionBuilder builder = SvgFilterDefinitionBuilder.Create(reader, budget, CancellationToken.None);
        Assert.True(reader.ReadToFollowing("feFlood"));
        builder.Add(reader, true);
        return builder.Finish();
    }
}
