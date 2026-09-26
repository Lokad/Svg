using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgOrthogonalGeometryLowererTests
{
    [Fact]
    public void Unit_magnification_keeps_nominal_flatness()
    {
        Assert.Equal(0.25, SvgOrthogonalGeometryLowering.ScaledFlatness(1.0), precision: 10);
    }

    [Fact]
    public void Magnification_refines_flatness_proportionally()
    {
        Assert.Equal(0.015625, SvgOrthogonalGeometryLowering.ScaledFlatness(16.0), precision: 10);
    }

    [Fact]
    public void Minification_never_coarsens_flatness()
    {
        Assert.Equal(0.25, SvgOrthogonalGeometryLowering.ScaledFlatness(0.5), precision: 10);
    }
}
