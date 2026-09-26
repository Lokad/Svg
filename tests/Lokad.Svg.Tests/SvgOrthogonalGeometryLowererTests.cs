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
    public void Sampled_outline_capacity_scales_with_loops_and_segments()
    {
        Assert.Equal(1033, SvgOrthogonalGeometryLowering.SampledOutlineCapacity(1, 1024));
        Assert.Equal(8202, SvgOrthogonalGeometryLowering.SampledOutlineCapacity(2, 4096));
        Assert.Equal(16396, SvgOrthogonalGeometryLowering.SampledOutlineCapacity(4, 4096));
    }

    [Fact]
    public void Minification_never_coarsens_flatness()
    {
        Assert.Equal(0.25, SvgOrthogonalGeometryLowering.ScaledFlatness(0.5), precision: 10);
    }
}
