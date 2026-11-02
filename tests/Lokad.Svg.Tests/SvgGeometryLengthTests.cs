using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgGeometryLengthTests
{
    [Theory]
    [InlineData("1in", 96)]
    [InlineData("2.54cm", 96)]
    [InlineData("25.4mm", 96)]
    [InlineData("72pt", 96)]
    [InlineData("6pc", 96)]
    [InlineData("101.6q", 96)]
    [InlineData("6PT", 8)]
    [InlineData("-.75pt", -1)]
    [InlineData("0cm", 0)]
    [InlineData("1px", 1)]
    [InlineData("1610612735.25pt", int.MaxValue)]
    [InlineData("-1610612736pt", int.MinValue)]
    public void Fixed_geometry_coordinates_share_units_and_apply_their_own_resolved_range(string token, double expected)
        => Assert.Equal(expected, SvgGeometrySyntax.ParseFixedCoordinate(token));

    [Theory]
    [InlineData("1610612736pt")]
    [InlineData("-1610612736.75pt")]
    [InlineData("1e308in")]
    [InlineData("1 cm")]
    [InlineData("1\u00a0cm")]
    [InlineData("1cmcm")]
    [InlineData("1em")]
    [InlineData("1ex")]
    [InlineData("1%")]
    [InlineData("NaNcm")]
    [InlineData("Infinitycm")]
    public void Fixed_geometry_keeps_contiguous_tokens_finite_values_and_contextual_units(string token)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            SvgGeometrySyntax.ParseFixedCoordinate(token)).ErrorCode);

    [Fact]
    public void Fixed_units_preserve_signed_zero()
        => Assert.Equal(BitConverter.DoubleToInt64Bits(-0d), BitConverter.DoubleToInt64Bits(SvgGeometrySyntax.ParseFixedCoordinate("-0cm")));

    [Fact]
    public void Local_placement_does_not_feed_a_unit_suffix_to_the_native_number_reader()
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (BudgetedParserStringStore strings = new(budget))
        using (StringReader input = new("<use href='#p' x='.25in' y='6pt' width='2.54cm' height='1pc'/>"))
        using (XmlReader reader = XmlReader.Create(input))
        {
            reader.MoveToContent();
            SvgLocalReferenceSyntax syntax = SvgLocalReferenceSyntax.Parse(reader, strings, 100, 100, CancellationToken.None);
            Assert.Equal(24, syntax.X); Assert.Equal(8, syntax.Y);
            Assert.Equal(96, syntax.Width); Assert.Equal(16, syntax.Height);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Physical_circle_radius_is_resolved_without_a_viewport_percentage_basis()
    {
        using StringReader input = new("<circle r='20cm'/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        SvgEllipseSyntax circle = SvgEllipseSyntax.ParseCircle(reader, new(null, null));
        Assert.Equal(20 * (96d / 2.54), circle.RadiusX);
        Assert.Equal(circle.RadiusX, circle.RadiusY);
    }

    [Fact]
    public void Inert_root_positions_keep_their_finite_domain_without_a_geometry_range_cap()
    {
        using StringReader input = new("<svg x='1e100cm' y='-2in' width='24' height='16'/>");
        using XmlReader reader = XmlReader.Create(input);
        reader.MoveToContent();
        SvgRootSyntax root = SvgRootSyntax.Parse(reader, CancellationToken.None);
        Assert.Equal(new SvgRootLength(24, false), root.Width);
        Assert.Equal(new SvgRootLength(16, false), root.Height);
    }

    [Fact]
    public void Rescaled_native_placement_mantissas_keep_the_validated_fallback_with_units()
    {
        RenderBudget budget = new(new HostMemoryBudget(100_000));
        using (BudgetedParserStringStore strings = new(budget))
        using (StringReader input = new("<use href='#p' x='" + new string('0', 100) + "1in'/>"))
        using (XmlReader reader = XmlReader.Create(input))
        {
            reader.MoveToContent();
            Assert.Equal(96, SvgLocalReferenceSyntax.Parse(reader, strings, null, null, CancellationToken.None).X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
