using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgShadowTintMechanismTests
{
    [Theory]
    [InlineData(false, 0f, 0, 0, 0, 0)]
    [InlineData(false, .1f, 7, 11, 18, 26)]
    [InlineData(false, .25f, 16, 28, 44, 64)]
    [InlineData(false, .5f, 32, 56, 88, 128)]
    [InlineData(false, .7f, 45, 79, 124, 179)]
    [InlineData(false, 1f, 64, 112, 176, 255)]
    [InlineData(true, 0f, 0, 0, 0, 0)]
    [InlineData(true, .1f, 1, 4, 11, 26)]
    [InlineData(true, .25f, 3, 10, 28, 64)]
    [InlineData(true, .5f, 7, 21, 56, 128)]
    [InlineData(true, .7f, 9, 29, 78, 179)]
    [InlineData(true, 1f, 13, 41, 111, 255)]
    public void Native_unpremultiplied_color_and_alpha_admission_precede_packed_tint(bool linear, float opacity,
        byte red, byte green, byte blue, byte alpha)
    {
        SvgShaderPaint tint = SvgFilterPlayback.PrepareShadowShaderTint(new(new(64, 112, 176, 255), opacity),
            linear ? SvgFilterColorSpace.LinearRgb : SvgFilterColorSpace.Srgb);
        Assert.Equal(new SvgConstantShaderPaint(red, green, blue, alpha), tint.AsConstant(1));
    }

    [Fact]
    public void Pending_linear_color_filter_preserves_tint_precision_until_coverage()
    {
        SvgShaderPaint tint = SvgFilterPlayback.PrepareShadowShaderTint(new(new(64, 112, 176, 255), .25f), SvgFilterColorSpace.LinearRgb);
        const float weight = 120f / 255f;
        byte[] pixel = new byte[4];
        new SvgShaderPaint(tint.Red * weight, tint.Green * weight, tint.Blue * weight, tint.Alpha * weight).Blend(pixel, 255);
        // Packing the tint first loses the low red lane (1 instead of 2).
        Assert.Equal(new byte[] { 2, 5, 13, 30 }, pixel);
    }
}
