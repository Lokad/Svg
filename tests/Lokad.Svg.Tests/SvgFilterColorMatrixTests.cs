using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgFilterColorMatrixTests
{
    [Theory]
    [InlineData("1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 1 0", .125f, .25f, .375f, .5f)]
    [InlineData("1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 .5 0", .0625f, .125f, .1875f, .25f)]
    [InlineData("0 0 0 1 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0", .25f, 0, 0, .5f)]
    [InlineData("1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  1 0 0 0 0", .0625f, .125f, .1875f, .25f)]
    [InlineData("2 0 0 0 .5  0 0 0 0 -.5  0 0 2 0 0  0 0 0 1 0", .5f, 0, .5f, .5f)]
    public void Matrix_evaluates_straight_channels_and_repremultiplies_after_clamping(string values,
        float red, float green, float blue, float alpha)
    {
        SvgFilterColorMatrix matrix = SvgFilterColorMatrixSyntax.Parse("matrix", values, CancellationToken.None);
        Assert.Equal(new SvgShaderPaint(red, green, blue, alpha), matrix.Apply(new(.125f, .25f, .375f, .5f)));
    }

    [Fact]
    public void Transparent_input_uses_bias_without_dividing_by_zero()
    {
        SvgFilterColorMatrix matrix = SvgFilterColorMatrixSyntax.Parse(null,
            "0 0 0 0 .25  0 0 0 0 .5  0 0 0 0 1  0 0 0 0 .5", CancellationToken.None);
        Assert.True(matrix.AffectsTransparentBlack);
        Assert.Equal(new SvgShaderPaint(.125f, .25f, .5f, .5f), matrix.Apply(default));
    }

    [Fact]
    public void Positive_subnormal_alpha_does_not_create_nonfinite_channels_before_alpha_bias()
    {
        SvgFilterColorMatrix matrix = SvgFilterColorMatrixSyntax.Parse(null,
            "1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 1 .5", CancellationToken.None);
        Assert.Equal(new SvgShaderPaint(0, 0, 0, .5f), matrix.Apply(new(float.Epsilon, float.Epsilon, float.Epsilon, float.Epsilon)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Color_transfer_shares_the_finite_unpremultiplication_boundary(bool linear)
        => Assert.Equal(new SvgShaderPaint(0, 0, 0, float.Epsilon), SvgFilterColorTransfer.Convert(
            new(float.Epsilon, float.Epsilon, float.Epsilon, float.Epsilon),
            linear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb));

    [Theory]
    [InlineData(-.5f, false)]
    [InlineData(0f, false)]
    [InlineData(.0001f, true)]
    [InlineData(2f, true)]
    public void Only_positive_alpha_bias_can_expand_transparent_black(float bias, bool expands)
    {
        SvgFilterColorMatrix matrix = new([1, 0, 0, 1, 1, 0, 1, 0, 1, 1, 0, 0, 1, 1, 1, 0, 0, 0, 1, bias]);
        Assert.Equal(expands, matrix.AffectsTransparentBlack);
        Assert.Equal(expands, matrix.Apply(default).Alpha > 0);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.25f)]
    public void Output_bounds_distinguish_input_ink_from_alpha_generated_across_the_primitive_region(float bias)
    {
        SvgFilterPrimitive primitive = new(SvgFilterPrimitiveKind.ColorMatrix, SvgFilterInput.SourceGraphic, 0, 0, 0, 0, default)
        { ColorMatrix = new([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, bias]) };
        using SvgFilterResource resource = new(new(new(0, false), new(0, false), new(16, false), new(16, false), SvgFilterUnits.UserSpace),
            SvgFilterUnits.UserSpace, SvgFilterColorSpace.Srgb, new([primitive], null), null);
        SvgPaintBounds source = new(4.25f, 5.5f, 8.75f, 9.25f);
        SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(16, 16), new(0, 0, 16, 16));
        RenderBudget scratch = new(new HostMemoryBudget(10_000));
        SvgPaintBounds actual = SvgFilterPlayback.SourceLayerBounds(resource, new(1, 1), context, source,
            SvgFilterBoundsKind.Output, scratch, CancellationToken.None);
        Assert.Equal(bias > 0 ? context.FilterRegion : source, actual);
        Assert.Equal(0, scratch.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("matrix")]
    [InlineData("saturate")]
    [InlineData("hueRotate")]
    public void Omitted_values_keep_the_identity_default(string? type)
        => Assert.Equal(SvgFilterColorMatrix.Identity, SvgFilterColorMatrixSyntax.Parse(type, null, CancellationToken.None));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(21)]
    public void Matrix_arity_is_checked_at_the_definition_boundary(int count)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            SvgFilterColorMatrixSyntax.Parse("matrix", string.Join(' ', Enumerable.Repeat("0", count)), CancellationToken.None)).ErrorCode);

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("1px")]
    [InlineData("1 2")]
    public void Scalar_modes_require_one_finite_unitless_number(string value)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() =>
            SvgFilterColorMatrixSyntax.Parse("saturate", value, CancellationToken.None)).ErrorCode);

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Internal_coefficients_cannot_bypass_finite_admission(float value)
        => Assert.Throws<ArgumentException>(() => new SvgFilterColorMatrix([value, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Numeric_token_limit_is_per_coefficient(bool exceeds)
    {
        string number = "0." + new string('0', SvgSyntaxLimits.MaximumValueTokenCharacters - 2 + (exceeds ? 1 : 0));
        if (exceeds) Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgFilterColorMatrixSyntax.Parse("saturate", number, CancellationToken.None)).ErrorCode);
        else Assert.False(SvgFilterColorMatrixSyntax.Parse("saturate", number, CancellationToken.None).AffectsTransparentBlack);
    }

    [Fact]
    public void Luminance_to_alpha_discards_color_and_ignores_its_inapplicable_values()
    {
        SvgFilterColorMatrix matrix = SvgFilterColorMatrixSyntax.Parse("luminanceToAlpha", "unused", CancellationToken.None);
        SvgShaderPaint result = matrix.Apply(new(.125f, .25f, .375f, .5f));
        Assert.Equal(0, result.Red); Assert.Equal(0, result.Green); Assert.Equal(0, result.Blue);
        Assert.InRange(result.Alpha, .46489f, .46491f);
        Assert.False(matrix.AffectsTransparentBlack);
    }

    [Fact]
    public void Cancellation_is_checked_before_default_or_ignored_values()
    {
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            SvgFilterColorMatrixSyntax.Parse("luminanceToAlpha", null, cancelled.Token)).CancellationToken);
    }
}
