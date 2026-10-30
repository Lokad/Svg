using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgGradientLengthTests
{
    [Theory]
    [InlineData("-.5", -.5)]
    [InlineData("0", 0)]
    [InlineData("2147483647", 2147483647)]
    public void Numbers_keep_their_value_across_unit_and_viewport_changes(string token, double expected)
    {
        SvgGradientLength length = SvgGradientLength.Parse(token);
        Assert.Equal(SvgGradientLengthUnit.Number, length.Unit);
        Assert.Equal(expected, length.Resolve(false, null));
        Assert.Equal(expected, length.Resolve(true, 64));
    }

    [Theory]
    [InlineData("-50%", -.5, -32)]
    [InlineData("0%", 0, 0)]
    [InlineData("150%", 1.5, 96)]
    public void Percentages_keep_their_authored_units_until_resolution(string token, double objectValue, double userValue)
    {
        SvgGradientLength length = SvgGradientLength.Parse(token);
        Assert.Equal(SvgGradientLengthUnit.Percentage, length.Unit);
        Assert.Equal(objectValue, length.Resolve(false, null));
        Assert.Equal(userValue, length.Resolve(true, 64));
    }

    [Fact]
    public void Percentage_resolution_preserves_native_float_multiplication_before_division()
    {
        // Six fresh native DOM measurements agree on the float result. A numeric
        // radius one float below it can paint the same small image, so image
        // equality alone cannot protect this intermediate boundary.
        SvgGradientLength length = SvgGradientLength.Parse("50%");
        Assert.Equal(25.298223495483398, length.Resolve(true, 50.59644317626953));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.MaxValue)]
    [InlineData(double.NaN)]
    public void Resolution_keeps_empty_bases_and_rejects_nonfinite_products(double basis)
    {
        SvgGradientLength length = SvgGradientLength.Parse("50%");
        if (basis == 0) Assert.Equal(0, length.Resolve(true, basis));
        else Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => length.Resolve(true, basis)).ErrorCode);
    }

    [Fact]
    public void Missing_coordinates_inherit_before_defaults_are_applied()
    {
        SvgGradientLength missing = SvgGradientLength.Parse(null);
        Assert.False(missing.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => missing.Resolve(false, null));
        Assert.Equal(SvgGradientLength.Parse("25%"), missing.Inherit(SvgGradientLength.Parse("25%")));
        Assert.Equal(SvgErrorCode.UnsupportedFeature,
            Assert.Throws<SvgException>(() => SvgGradientLength.Parse("25%").Resolve(true, null)).ErrorCode);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity%")]
    [InlineData("2147483648")]
    [InlineData("2em")]
    [InlineData("25oops%")]
    public void Invalid_or_unimplemented_lengths_stay_explicit(string token)
    {
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgGradientLength.Parse(token)).ErrorCode);
    }
}
