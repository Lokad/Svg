using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEllipticalArcTests
{
    [Fact]
    public void Zero_radius_falls_back_to_line()
    {
        SvgDevicePoint start = new(1, 2);
        SvgDevicePoint end = new(7, 8);

        Assert.Equal(SvgNormalizedArc.CreateLine(end), Arc(start, 0, 4, 0, false, true, end));
        Assert.Equal(SvgNormalizedArc.CreateLine(end), Arc(start, 4, 0, 0, true, false, end));
    }

    [Fact]
    public void Negative_radii_match_positive_radii()
    {
        SvgDevicePoint start = new(0, 0);
        SvgDevicePoint end = new(8, 0);

        Assert.Equal(Arc(start, 4, 4, 0, false, true, end), Arc(start, -4, -4, 0, false, true, end));
    }

    [Fact]
    public void Identical_endpoints_omit_arc()
    {
        SvgDevicePoint point = new(3, 5);

        Assert.Equal(SvgNormalizedArc.CreateLine(point), Arc(point, 4, 4, 0, false, true, point));
        Assert.Equal(SvgNormalizedArc.CreateLine(point), Arc(point, 4, 4, 30, true, false, point));
    }

    [Fact]
    public void Small_radii_scale_up_exactly()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(0, 0), 2, 2, 0, false, true, new SvgDevicePoint(8, 0));

        Assert.Equal(SvgArcKind.Arc, arc.Kind);
        Assert.Equal(4.0, arc.RadiusX);
        Assert.Equal(4.0, arc.RadiusY);
        Assert.Equal(new SvgDevicePoint(4, 0), arc.Center);
        Assert.Equal(System.Math.PI, arc.StartAngleRadians);
        Assert.Equal(System.Math.PI, arc.SweepRadians);
        Assert.Equal(new SvgDevicePoint(8, 0), arc.End);
    }

    [Fact]
    public void Large_radii_keep_values_on_small_arc()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(0, 0), 8, 8, 0, false, true, new SvgDevicePoint(8, 0));

        Assert.Equal(SvgArcKind.Arc, arc.Kind);
        Assert.Equal(8.0, arc.RadiusX);
        Assert.Equal(8.0, arc.RadiusY);
        Assert.Equal(new SvgDevicePoint(4, 4.0 * System.Math.Sqrt(3.0)), arc.Center);
        Assert.Equal(System.Math.PI / 3.0, arc.SweepRadians, precision: 12);
    }

    [Fact]
    public void Large_arc_selects_far_center()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(0, 0), 8, 8, 0, true, true, new SvgDevicePoint(8, 0));

        Assert.Equal(SvgArcKind.Arc, arc.Kind);
        Assert.Equal(new SvgDevicePoint(4, -4.0 * System.Math.Sqrt(3.0)), arc.Center);
        Assert.Equal(5.0 * System.Math.PI / 3.0, arc.SweepRadians, precision: 12);
    }

    [Fact]
    public void Semicircle_positive_sweep_is_exact()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(8, 0), 8, 8, 0, true, true, new SvgDevicePoint(-8, 0));

        Assert.Equal(SvgArcKind.Arc, arc.Kind);
        Assert.Equal(new SvgDevicePoint(0, 0), arc.Center);
        Assert.Equal(0.0, arc.StartAngleRadians);
        Assert.Equal(System.Math.PI, arc.SweepRadians);
    }

    [Fact]
    public void Semicircle_negative_sweep_is_exact()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(8, 0), 8, 8, 0, true, false, new SvgDevicePoint(-8, 0));

        Assert.Equal(new SvgDevicePoint(0, 0), arc.Center);
        Assert.Equal(0.0, arc.StartAngleRadians);
        Assert.Equal(-System.Math.PI, arc.SweepRadians);
    }

    [Fact]
    public void Quarter_arc_positive_sweep_is_exact()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(8, 0), 8, 8, 0, false, true, new SvgDevicePoint(0, 8));

        Assert.Equal(new SvgDevicePoint(0, 0), arc.Center);
        Assert.Equal(0.0, arc.StartAngleRadians);
        Assert.Equal(System.Math.PI / 2.0, arc.SweepRadians);
    }

    [Fact]
    public void Quarter_arc_negative_sweep_wraps()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(8, 0), 8, 8, 0, false, false, new SvgDevicePoint(0, 8));

        Assert.Equal(new SvgDevicePoint(8, 8), arc.Center);
        Assert.Equal(-System.Math.PI / 2.0, arc.StartAngleRadians);
        Assert.Equal(-System.Math.PI / 2.0, arc.SweepRadians);
    }

    [Fact]
    public void Quarter_turn_rotation_clamps_and_centers()
    {
        SvgNormalizedArc arc = Arc(new SvgDevicePoint(0, 0), 8, 4, 90, false, false, new SvgDevicePoint(8, 0));

        Assert.Equal(SvgArcKind.Arc, arc.Kind);
        Assert.Equal(8.0, arc.RadiusX);
        Assert.Equal(4.0, arc.RadiusY);
        Assert.Equal(new SvgDevicePoint(4, 0), arc.Center);
    }

    [Fact]
    public void Nonfinite_inputs_are_rejected()
    {
        SvgDevicePoint point = new(1, 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => Arc(new SvgDevicePoint(double.NaN, 0), 4, 4, 0, false, true, point));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arc(point, double.PositiveInfinity, 4, 0, false, true, point));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arc(point, 4, 4, double.NaN, false, true, point));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arc(point, 4, 4, 0, false, true, new SvgDevicePoint(0, double.NegativeInfinity)));
    }

    [Fact]
    public void Extreme_span_overflows_LimitExceeded()
    {
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Arc(new SvgDevicePoint(1e308, 0), 8, 8, 0, false, true, new SvgDevicePoint(-1e308, 0))).ErrorCode);
    }

    [Fact]
    public void Huge_endpoint_overflows_correction_LimitExceeded()
    {
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Arc(new SvgDevicePoint(0, 0), 8, 8, 0, false, true, new SvgDevicePoint(1e308, 0))).ErrorCode);
    }

    private static SvgNormalizedArc Arc(SvgDevicePoint start, double radiusX, double radiusY, double rotationDegrees, bool largeArc, bool sweep, SvgDevicePoint end)
    {
        return SvgEllipticalArc.Normalize(start, radiusX, radiusY, rotationDegrees, largeArc, sweep, end);
    }
}