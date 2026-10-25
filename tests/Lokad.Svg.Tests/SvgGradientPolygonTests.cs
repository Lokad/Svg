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
        SvgRadialGradient circle = new(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 0.5, 0.5, 0.5, 0.5, 0.5, 0, 0, 1, false, false, SvgGradientSpread.Pad, stops, 1, 1, false, SvgGradientTransform.Identity, false);
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
    [Fact]
    public void Gradient_transform_compose_applies_inner_first()
    {
        SvgGradientTransform outer = new(2, 0, 0, 3, 5, 7);
        SvgGradientTransform inner = new(1, 0, 0, 1, -2, 4);

        SvgGradientTransform composed = SvgGradientTransform.Compose(outer, inner);
        (double innerX, double innerY) = inner.Apply(11, 13);
        (double expectedX, double expectedY) = outer.Apply(innerX, innerY);
        (double actualX, double actualY) = composed.Apply(11, 13);

        Assert.Equal(expectedX, actualX);
        Assert.Equal(expectedY, actualY);
    }

    [Fact]
    public void Gradient_transform_try_invert_round_trips_general_matrix()
    {
        SvgGradientTransform forward = new(-4.228, -8.251, 14.01, -7.18, 5688, 455.1);

        Assert.True(forward.TryInvert(out SvgGradientTransform inverse));
        (double forwardX, double forwardY) = forward.Apply(204.9, -244);
        (double x, double y) = inverse.Apply(forwardX, forwardY);

        Assert.Equal(204.9, x, 6);
        Assert.Equal(-244, y, 6);
    }

    [Fact]
    public void Gradient_transform_try_invert_rejects_singular_matrix()
    {
        SvgGradientTransform singular = new(1, 2, 2, 4, 0, 0);

        Assert.False(singular.TryInvert(out _));
    }

    [Fact]
    public void Shift_general_radial_composes_translation_exactly()
    {
        SvgRadialGradient circle = GeneralCircle(new SvgGradientTransform(0.5, 0, 0, 0.25, 0, 0));

        SvgRadialGradient shifted = SvgGradientSyntax.ShiftGeneralRadial(circle, 8, -3);

        Assert.Equal(new SvgGradientTransform(0.5, 0, 0, 0.25, -4, 0.75), shifted.Inverse);
    }

    [Fact]
    public void Scale_general_radial_composes_axis_map_exactly()
    {
        SvgRadialGradient circle = GeneralCircle(new SvgGradientTransform(0.5, 0, 0, 0.25, 0, 0));

        SvgRadialGradient scaled = SvgGradientSyntax.ScaleGeneralRadial(circle, 2, 0.5, 8, -4);

        Assert.Equal(new SvgGradientTransform(0.25, 0, 0, 0.5, -2, 2), scaled.Inverse);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void Scale_general_radial_marks_collapsed_reference_axes_empty(double scaleX, double scaleY)
    {
        SvgRadialGradient circle = GeneralCircle(SvgGradientTransform.Identity);

        SvgRadialGradient collapsed = SvgGradientSyntax.ScaleGeneralRadial(circle, scaleX, scaleY, 0, 0);

        Assert.True(collapsed.IsEmpty);
        Assert.False(circle.IsEmpty);
        Assert.Same(circle.Stops, collapsed.Stops);
    }

    private static SvgRadialGradient GeneralCircle(SvgGradientTransform inverse)
    {
        SvgGradientStop[] stops = [new SvgGradientStop(0, new SvgColor(255, 0, 0, 255)), new SvgGradientStop(1, new SvgColor(0, 0, 255, 255))];
        return new SvgRadialGradient(new SvgColor(255, 0, 0, 255), new SvgColor(0, 0, 255, 255), 4, 4, 4, 4, 4, 0, 0, 1, false, true, SvgGradientSpread.Pad, stops, 1, 1, false, inverse, true);
    }
    [Fact]
    public void Map_user_space_radial_general_composes_element_and_frame()
    {
        SvgRadialGradient circle = GeneralCircle(new SvgGradientTransform(0.5, 0, 0, 0.25, 0, 0));
        SvgAffineTransform element = SvgAffineTransform.FromTranslation(8, -3);
        SvgViewportFrame frame = new(4, -2, 2, 0.5, null, null, new SvgClipBox(0, 0, 64, 64), 0);

        SvgRadialGradient mapped = SvgGradientSyntax.MapRadialReference(element, circle, frame);

        Assert.Equal(4, mapped.CenterX);
        (double x, double y) = mapped.Inverse.Apply(36, 8.5);
        Assert.Equal(4, x);
        Assert.Equal(6, y);
    }
}
