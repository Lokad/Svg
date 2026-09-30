using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

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

    [Fact]
    public void WeldDustJoints_collapses_mapping_dust_to_the_earlier_value()
    {
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(8, 8), new(8 + 1e-13, 8), new(0, 8)];
        int[] ends = [5];
        int[] weldedEnds = new int[1];
        int total = SvgOrthogonalGeometryLowering.WeldDustJoints(points, ends, weldedEnds, new ParserWorkCounter(100), CancellationToken.None);
        Assert.Equal(4, total);
        Assert.Equal([4], weldedEnds);
        Assert.Equal(new SvgDevicePoint(8, 8), points[2]);
    }

    [Fact]
    public void WeldDustJoints_preserves_bitwise_equal_joints_for_the_zero_length_path()
    {
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(8, 0), new(0, 8)];
        int[] ends = [4];
        int[] weldedEnds = new int[1];
        int total = SvgOrthogonalGeometryLowering.WeldDustJoints(points, ends, weldedEnds, new ParserWorkCounter(100), CancellationToken.None);
        Assert.Equal(4, total);
        Assert.Equal([4], weldedEnds);
    }

    [Fact]
    public void WeldDustJoints_ignores_separate_features_above_epsilon()
    {
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(8 + 1e-6, 8), new(0, 8)];
        int[] ends = [4];
        int[] weldedEnds = new int[1];
        int total = SvgOrthogonalGeometryLowering.WeldDustJoints(points, ends, weldedEnds, new ParserWorkCounter(100), CancellationToken.None);
        Assert.Equal(4, total);
        Assert.Equal(new SvgDevicePoint(8 + 1e-6, 8), points[2]);
    }

    [Fact]
    public void WeldDustJoints_closes_dust_gaps_at_contour_end()
    {
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(8, 8), new(0, 8), new(1e-13, 1e-13)];
        int[] ends = [5];
        int[] weldedEnds = new int[1];
        int total = SvgOrthogonalGeometryLowering.WeldDustJoints(points, ends, weldedEnds, new ParserWorkCounter(100), CancellationToken.None);
        Assert.Equal(4, total);
        Assert.Equal([4], weldedEnds);
    }

    [Fact]
    public void WeldDustJoints_compacts_degenerate_dust_loops_for_existing_guards()
    {
        SvgDevicePoint[] points = [new(0, 0), new(1e-13, 0), new(0, 8)];
        int[] ends = [3];
        int[] weldedEnds = new int[1];
        int total = SvgOrthogonalGeometryLowering.WeldDustJoints(points, ends, weldedEnds, new ParserWorkCounter(100), CancellationToken.None);
        Assert.Equal(2, total);
        Assert.Equal([2], weldedEnds);
        Assert.Equal(new SvgDevicePoint(0, 8), points[1]);
    }

    [Fact]
    public void WeldDustJoints_tracks_ends_across_contours()
    {
        SvgDevicePoint[] points = [new(0, 0), new(8, 0), new(0, 8), new(20, 20), new(28, 20), new(28, 28), new(28 + 1e-13, 28), new(20, 28)];
        int[] ends = [3, 8];
        int[] weldedEnds = new int[2];
        int total = SvgOrthogonalGeometryLowering.WeldDustJoints(points, ends, weldedEnds, new ParserWorkCounter(100), CancellationToken.None);
        Assert.Equal(7, total);
        Assert.Equal([3, 7], weldedEnds);
        Assert.Equal(new SvgDevicePoint(28, 28), points[5]);
    }
}
