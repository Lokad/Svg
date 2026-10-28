using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgGlyphStrikeSamplingTests
{
    [Theory]
    [InlineData(1, 0, 0, 1, true)]
    [InlineData(-1, 0, 0, 1, true)]
    [InlineData(0, 1, -1, 0, true)]
    [InlineData(0, -1, -1, 0, true)]
    [InlineData(1, 0, .25, 1, false)]
    [InlineData(.8, .6, -.6, .8, false)]
    public void Outline_alignment_includes_quarter_turns_and_reflections(double a, double b, double c, double d, bool aligned)
    {
        SvgAffineTransform map = new(a, b, c, d, 0, 0);
        SvgGlyphStrikeTransform strike = SvgGlyphStrikeTransform.Create(12.5, map);
        Assert.Equal(aligned, strike.HasAxisAlignedOutline);
    }

    [Theory]
    [InlineData(14.125f, 14.25f)]
    [InlineData(14.375f, 14.5f)]
    [InlineData(14.625f, 14.75f)]
    [InlineData(14.875f, 15f)]
    [InlineData(-14.125f, -14f)]
    [InlineData(-14.375f, -14.25f)]
    public void Ordinary_baselines_keep_the_existing_quarter_phase(float value, float expected)
        => Assert.Equal(expected, SvgGlyphStrikeTransform.RoundXOrigin(value, SvgAffineTransform.Identity));

    [Theory]
    [InlineData(14.25f, 14f)]
    [InlineData(14.5f, 15f)]
    [InlineData(14.75f, 15f)]
    [InlineData(-14.25f, -14f)]
    [InlineData(-14.5f, -14f)]
    [InlineData(-14.75f, -15f)]
    public void Vertical_baselines_ignore_the_device_x_subpixel_field(float value, float expected)
        => Assert.Equal(expected, SvgGlyphStrikeTransform.RoundXOrigin(value, new(0, 1, -1, 0, 0, 0)));

    [Fact]
    public void Float_addition_at_the_half_bucket_keeps_native_tie_rounding()
    {
        SvgAffineTransform vertical = new(0, 1, -1, 0, 0, 0);
        Assert.Equal(1f, SvgGlyphStrikeTransform.RoundXOrigin(MathF.BitDecrement(.5f), vertical));
        Assert.Equal(0f, SvgGlyphStrikeTransform.RoundXOrigin(MathF.BitDecrement(MathF.BitDecrement(.5f)), vertical));
        Assert.Equal(.25f, SvgGlyphStrikeTransform.RoundXOrigin(MathF.BitDecrement(.125f), SvgAffineTransform.Identity));
        Assert.Equal(0f, SvgGlyphStrikeTransform.RoundXOrigin(MathF.BitDecrement(MathF.BitDecrement(.125f)), SvgAffineTransform.Identity));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.MaxValue)]
    public void Nonfinite_rounded_origins_keep_a_domain_failure(float value)
        => Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphStrikeTransform.RoundXOrigin(value, SvgAffineTransform.Identity)).ErrorCode);
}
