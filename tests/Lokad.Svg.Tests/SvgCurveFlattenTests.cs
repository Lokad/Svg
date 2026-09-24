using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCurveFlattenTests
{
    [Fact]
    public void Straight_quadratic_emits_single_end_segment()
    {
        SvgDevicePoint[] flattened = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(8, 0), 0.5);

        Assert.Equal([new SvgDevicePoint(8, 0)], flattened);
    }

    [Fact]
    public void Quadratic_one_subdivision_pins_midpoint()
    {
        SvgDevicePoint[] flattened = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), 2.0);

        Assert.Equal([new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 0)], flattened);
    }

    [Fact]
    public void Quadratic_full_flatness_emits_end_only()
    {
        SvgDevicePoint[] flattened = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), 8.0);

        Assert.Equal([new SvgDevicePoint(8, 0)], flattened);
    }

    [Fact]
    public void Quadratic_finer_flatness_never_shrinks_output()
    {
        SvgDevicePoint end = new(8, 0);
        SvgDevicePoint[] coarse = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), end, 8.0);
        SvgDevicePoint[] middle = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), end, 2.0);
        SvgDevicePoint[] fine = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), end, 0.5);

        Assert.Single(coarse);
        Assert.Equal([new SvgDevicePoint(4, 4), new SvgDevicePoint(8, 0)], middle);
        Assert.True(fine.Length >= middle.Length);
        Assert.Equal(end, coarse[^1]);
        Assert.Equal(end, middle[^1]);
        Assert.Equal(end, fine[^1]);
    }

    [Fact]
    public void Quadratic_point_curve_emits_end()
    {
        SvgDevicePoint[] flattened = FlattenQuad(new SvgDevicePoint(3, 7), new SvgDevicePoint(3, 7), new SvgDevicePoint(3, 7), 0.5);

        Assert.Equal([new SvgDevicePoint(3, 7)], flattened);
    }

    [Fact]
    public void Quadratic_loop_subdivides_and_ends_exactly()
    {
        SvgDevicePoint[] flattened = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 0), 1.0);

        Assert.Equal([new SvgDevicePoint(4, 0), new SvgDevicePoint(0, 0)], flattened);
    }

    [Fact]
    public void Straight_cubic_emits_single_end_segment()
    {
        SvgDevicePoint[] flattened = FlattenCube(new SvgDevicePoint(0, 0), new SvgDevicePoint(2, 0), new SvgDevicePoint(6, 0), new SvgDevicePoint(8, 0), 0.5);

        Assert.Equal([new SvgDevicePoint(8, 0)], flattened);
    }

    [Fact]
    public void Cubic_one_subdivision_pins_midpoint()
    {
        SvgDevicePoint[] flattened = FlattenCube(new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), 3.0);

        Assert.Equal([new SvgDevicePoint(6, 4), new SvgDevicePoint(0, 8)], flattened);
    }

    [Fact]
    public void Cubic_full_flatness_emits_end_only()
    {
        SvgDevicePoint[] flattened = FlattenCube(new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(8, 8), new SvgDevicePoint(0, 8), 8.0);

        Assert.Equal([new SvgDevicePoint(0, 8)], flattened);
    }

    [Fact]
    public void Cubic_loop_terminates_with_exact_end()
    {
        SvgDevicePoint[] flattened = FlattenCube(new SvgDevicePoint(0, 0), new SvgDevicePoint(2, 4), new SvgDevicePoint(2, -4), new SvgDevicePoint(4, 0), 0.5);

        Assert.True(flattened.Length >= 2);
        Assert.Equal(new SvgDevicePoint(4, 0), flattened[^1]);
    }

    [Fact]
    public void Straight_quadratic_accepts_tiny_flatness()
    {
        SvgDevicePoint[] flattened = FlattenQuad(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(8, 0), double.Epsilon);

        Assert.Equal([new SvgDevicePoint(8, 0)], flattened);
    }

    [Fact]
    public void Policy_rejects_invalid_bounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(double.NaN, 64, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(double.PositiveInfinity, 64, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(0, 64, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(-1, 64, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(0.5, 0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(0.5, SvgCurveFlatten.MaximumSupportedDepth + 1, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SvgFlattenPolicy(0.5, 64, 0));
    }

    [Fact]
    public void Flatten_rejects_default_policy()
    {
        SvgDevicePoint[] buffer = new SvgDevicePoint[16];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(8, 0), default, buffer, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgCurveFlatten.FlattenCubic(new SvgDevicePoint(0, 0), new SvgDevicePoint(2, 0), new SvgDevicePoint(6, 0), new SvgDevicePoint(8, 0), default, buffer, CancellationToken.None));
    }

    [Fact]
    public void Segment_cap_throws_LimitExceeded()
    {
        SvgFlattenPolicy policy = new(0.5, 64, 1);
        SvgDevicePoint[] buffer = new SvgDevicePoint[64];

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), policy, buffer, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Buffer_capacity_throws_LimitExceeded()
    {
        SvgFlattenPolicy policy = new(0.5, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[1];

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), policy, buffer, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Depth_cap_throws_LimitExceeded()
    {
        SvgFlattenPolicy policy = new(0.5, 1, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 8), new SvgDevicePoint(8, 0), policy, buffer, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Nonfinite_control_throws_ArgumentOutOfRange()
    {
        SvgFlattenPolicy policy = new(0.5, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];

        Assert.Throws<ArgumentOutOfRangeException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(double.NaN, 0), new SvgDevicePoint(8, 0), policy, buffer, CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => SvgCurveFlatten.FlattenCubic(new SvgDevicePoint(0, 0), new SvgDevicePoint(2, 0), new SvgDevicePoint(6, double.PositiveInfinity), new SvgDevicePoint(8, 0), policy, buffer, CancellationToken.None));
    }

    [Fact]
    public void Overflowed_deviation_throws_LimitExceeded()
    {
        SvgFlattenPolicy policy = new(0.5, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(1e308, 1e308), new SvgDevicePoint(8, 0), policy, buffer, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Degenerate_overflow_throws_LimitExceeded()
    {
        SvgFlattenPolicy policy = new(0.5, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(-1e308, 0), new SvgDevicePoint(1e308, 0), new SvgDevicePoint(-1e308, 0), policy, buffer, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Precancelled_token_throws()
    {
        SvgFlattenPolicy policy = new(0.5, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgCurveFlatten.FlattenQuadratic(new SvgDevicePoint(0, 0), new SvgDevicePoint(4, 0), new SvgDevicePoint(8, 0), policy, buffer, cancelled.Token));
    }

    private static SvgDevicePoint[] FlattenQuad(SvgDevicePoint start, SvgDevicePoint control, SvgDevicePoint end, double flatness)
    {
        SvgFlattenPolicy policy = new(flatness, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];
        int count = SvgCurveFlatten.FlattenQuadratic(start, control, end, policy, buffer, CancellationToken.None);
        return buffer[..count];
    }

    private static SvgDevicePoint[] FlattenCube(SvgDevicePoint start, SvgDevicePoint firstControl, SvgDevicePoint secondControl, SvgDevicePoint end, double flatness)
    {
        SvgFlattenPolicy policy = new(flatness, 64, 256);
        SvgDevicePoint[] buffer = new SvgDevicePoint[256];
        int count = SvgCurveFlatten.FlattenCubic(start, firstControl, secondControl, end, policy, buffer, CancellationToken.None);
        return buffer[..count];
    }
}