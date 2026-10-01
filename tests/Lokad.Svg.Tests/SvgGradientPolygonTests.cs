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
        SvgLinearGradient gradient = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0, 0, 1, 1, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1, false);
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] ends = [3];
        SvgRectangle box = new(0, 0, 8, 8, default, true, null);
        using SvgGradientPolygon polygon = SvgGradientPolygon.Create(points, ends, null, gradient, box, true, false, 255, SvgPolygonMapping.QuarterEdges, null, null, budget);

        SvgGeometry geometry = SvgGeometry.FromGradientPolygon(polygon);

        Assert.Equal(SvgGeometryKind.GradientPolygon, geometry.Kind);
        Assert.Same(polygon, geometry.GradientPolygon);
        Assert.Equal(box, geometry.GradientPolygon.Box);
        Assert.Equal(255, geometry.GradientPolygon.Opacity);
        Assert.True(geometry.GradientPolygon.HasFill);
    }

    [Fact]
    public void Similarity_covers_rotation_reflection_and_uniform_scales()
    {
        double cosine = Math.Cos(Math.PI / 4);
        double sine = Math.Sin(Math.PI / 4);

        Assert.True(new SvgGradientTransform(1, 0, 0, 1, 0, 0).IsSimilarity());
        Assert.True(new SvgGradientTransform(cosine, sine, -sine, cosine, 0, 0).IsSimilarity());
        Assert.True(new SvgGradientTransform(-1, 0, 0, 1, 5, 7).IsSimilarity());
        Assert.True(new SvgGradientTransform(2, 0, 0, 2, 0, 0).IsSimilarity());
    }

    [Fact]
    public void Similarity_rejects_skew_and_rotation_with_nonuniform_scale()
    {
        double cosine = Math.Cos(Math.PI / 4);
        double sine = Math.Sin(Math.PI / 4);

        Assert.False(new SvgGradientTransform(1, 0, 0.5, 1, 0, 0).IsSimilarity());
        Assert.False(new SvgGradientTransform(2 * cosine, sine, -2 * sine, cosine, 0, 0).IsSimilarity());
        Assert.False(new SvgGradientTransform(2, 0, 0, 3, 0, 0).IsSimilarity());
    }

    [Fact]
    public void Radial_polygon_retains_contours_circle_and_box()
    {
        RenderBudget budget = new(1_000);
        SvgGradientStop[] stops = [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        SvgRadialGradient circle = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1, false);
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] ends = [3];
        SvgRectangle box = new(0, 0, 8, 8, default, true, null);
        using SvgRadialPolygon polygon = SvgRadialPolygon.Create(points, ends, null, circle, box, true, false, 255, SvgPolygonMapping.QuarterEdges, null, null, budget);

        SvgGeometry geometry = SvgGeometry.FromRadialPolygon(polygon);

        Assert.Equal(SvgGeometryKind.RadialPolygon, geometry.Kind);
        Assert.Same(polygon, geometry.RadialPolygon);
        Assert.Equal(0.5, geometry.RadialPolygon.Gradient.Radius);
        Assert.True(geometry.RadialPolygon.HasFill);
    }
}
