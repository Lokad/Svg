using System.Xml;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgTextPositionLengthTests
{
    [Theory]
    [InlineData("96", 96)]
    [InlineData("96PX", 96)]
    [InlineData("1in", 96)]
    [InlineData("2.54cm", 96)]
    [InlineData("25.4mm", 96)]
    [InlineData("101.6q", 96)]
    [InlineData("72pt", 96)]
    [InlineData("6pc", 96)]
    [InlineData(".5em", 12)]
    [InlineData("-2EM", -48)]
    [InlineData("1e-1em", 2.4)]
    public void Lengths_share_fixed_units_and_the_declaration_font_size(string token, double expected)
        => Assert.Equal(expected, new SvgTextPositionBasis(24).Read(token, SvgTextCoordinateKind.Length), 12);

    [Theory]
    [InlineData("-0")]
    [InlineData("-0px")]
    [InlineData("-0em")]
    [InlineData("-0cm")]
    public void Length_resolution_preserves_signed_zero(string token)
        => Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(new SvgTextPositionBasis(24).Read(token, SvgTextCoordinateKind.Length)));

    [Theory]
    [InlineData("1 em")]
    [InlineData("1 px")]
    [InlineData("1\u00A0em")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e309")]
    [InlineData("1e308em")]
    [InlineData("1e308in")]
    [InlineData("1ex")]
    [InlineData("1%")]
    [InlineData("1rem")]
    [InlineData("calc(1px)")]
    public void Unsupported_units_noncontiguous_tokens_and_overflow_reject(string token)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            new SvgTextPositionBasis(24).Read(token, SvgTextCoordinateKind.Length)).ErrorCode);

    [Theory]
    [InlineData("1em")]
    [InlineData("1px")]
    [InlineData("1pt")]
    [InlineData("1deg")]
    public void Rotation_keeps_its_unitless_grammar(string token)
        => Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() =>
            new SvgTextPositionBasis(24).Read(token, SvgTextCoordinateKind.Rotation)).ErrorCode);

    [Fact]
    public void Numeric_extremes_do_not_narrow_early_and_unit_products_must_remain_finite()
    {
        string maximum = double.MaxValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        SvgTextPositionBasis basis = new(double.MaxValue);
        Assert.Equal(double.MaxValue, basis.Read(maximum, SvgTextCoordinateKind.Length));
        Assert.Equal(double.MaxValue, basis.Read(maximum + "px", SvgTextCoordinateKind.Length));
        Assert.Equal(double.MaxValue, basis.Read("1em", SvgTextCoordinateKind.Length));
        Assert.Throws<SvgException>(() => basis.Read("2em", SvgTextCoordinateKind.Length));
    }

    [Fact]
    public void Root_list_validation_admits_the_former_em_rejection_and_keeps_original_offsets()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<text x='1 2em'/>"));
        reader.Read();
        SvgTextStyle style = SvgTextSyntax.Parse(reader);
        Assert.Equal(1, style.X);
        RenderBudget budget = new(new HostMemoryBudget(10_000));
        using (OwnedParserArray<SvgTextPositionAdjustment> positions = style.PositionLists.Build("A😀", [], budget, CancellationToken.None))
            Assert.Equal(new double?[] { 1, 32 }, positions.Items.Select(position => position.X));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void The_former_single_span_em_rejection_resolves_its_inherited_font_size()
    {
        using XmlReader reader = XmlReader.Create(new StringReader("<tspan y='1em'/>"));
        reader.Read();
        Assert.True(SvgTextSyntax.TryParseChildStyle(reader, new(SvgTextWhitespaceMode.Default, 0), default, CancellationToken.None, out SvgTextSpanStyle span));
        Assert.Equal(new SvgTextPositionOffset(null, 16, null, null), span.PositionOffset);
        Assert.Equal(16, span.PositionLists.Basis.FontSize);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("dx")]
    [InlineData("dy")]
    public void Root_and_span_axes_use_their_own_computed_size(string attribute)
    {
        using XmlReader root = XmlReader.Create(new StringReader($"<text font-size='20' {attribute}='1em 2em'/>"));
        root.Read();
        Assert.Equal(20, SvgTextSyntax.Parse(root).PositionLists.Basis.FontSize);
        using XmlReader child = XmlReader.Create(new StringReader($"<tspan style='font-size:24' {attribute}='1em 2em'/>"));
        child.Read();
        Assert.True(SvgTextSyntax.TryParseChildStyle(child, new(SvgTextWhitespaceMode.Default, 0), default, CancellationToken.None, out SvgTextSpanStyle span));
        Assert.Equal(24, span.Font.FontSize);
        RenderBudget budget = new(new HostMemoryBudget(10_000));
        using (OwnedParserArray<SvgTextPositionAdjustment> positions = span.PositionLists.Build("AB", [], budget, CancellationToken.None))
        {
            Assert.Equal(new double[] { 24, 48 }, positions.Items.Select(position => attribute switch
                { "x" => position.X ?? double.NaN, "y" => position.Y ?? double.NaN, "dx" => position.Dx, _ => position.Dy }));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Continuation_and_descendant_overrides_do_not_rebind_the_ancestor_font_basis()
    {
        SvgTextPositionLists parent = new("1em 2em 3em", null, ".5em 1em 2em", null, "0 10", new(20)) { CharacterOffset = 1 };
        SvgTextPositionSpanCursor child = new(new(0, 1, new("1em", null, "1em", null, null, new(8)), null));
        SvgTextPositionValue value = child.Next();
        RenderBudget budget = new(new HostMemoryBudget(10_000));
        using (OwnedParserArray<SvgTextPositionAdjustment> positions = parent.Build("😀A", [new(0, value.X, null, value.Dx ?? 0, 0) { OverridesDx = true }], budget, CancellationToken.None))
        {
            Assert.Equal(new SvgTextPositionAdjustment(0, 8, null, 8, 0) { Rotation = 10 }, positions.Items[0]);
            Assert.Equal(new SvgTextPositionAdjustment(2, 60, null, 40, 0) { Rotation = 10 }, positions.Items[1]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
