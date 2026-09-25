using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGradientPolygonTests
{
    [Fact]
    public void Gradient_polygon_retains_contours_paint_and_box()
    {
        RenderBudget budget = new(1_000);
        SvgGradientStop[] stops = [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        SvgLinearGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1);
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] ends = [3];
        SvgRectangle box = new(0, 0, 8, 8, default, true, null);
        using SvgGradientPolygon polygon = SvgGradientPolygon.Create(points, ends, null, gradient, box, true, false, 255, SvgPolygonMapping.QuarterEdges, null, budget);

        SvgGeometry geometry = SvgGeometry.FromGradientPolygon(polygon);

        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Same(polygon, geometry.GradientPolygon);
        Assert.Equal(box, geometry.GradientPolygon.Box);
        Assert.Equal(255, geometry.GradientPolygon.Opacity);
        Assert.True(geometry.GradientPolygon.HasFill);
    }

    [Fact]
    public void Radial_polygon_retains_contours_circle_and_box()
    {
        RenderBudget budget = new(1_000);
        SvgGradientStop[] stops = [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        SvgRadialGradient circle = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops);
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] ends = [3];
        SvgRectangle box = new(0, 0, 8, 8, default, true, null);
        using SvgRadialPolygon polygon = SvgRadialPolygon.Create(points, ends, null, circle, box, true, false, 255, SvgPolygonMapping.QuarterEdges, null, budget);

        SvgGeometry geometry = SvgGeometry.FromRadialPolygon(polygon);

        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        Assert.Same(polygon, geometry.RadialPolygon);
        Assert.Equal(0.5, geometry.RadialPolygon.Gradient.Radius);
        Assert.True(geometry.RadialPolygon.HasFill);
    }
}