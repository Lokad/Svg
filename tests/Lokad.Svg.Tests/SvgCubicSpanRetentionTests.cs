using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCubicSpanRetentionTests
{
    [Fact]
    public void Cubic_records_both_controls_with_slice_indices()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.EnableCubicSpanRetention();
        builder.BeginContour(new SvgDevicePoint(0, 8));
        builder.AddCubic(new SvgDevicePoint(3, 0), new SvgDevicePoint(6, 0), new SvgDevicePoint(8, 8), new SvgFlattenPolicy(3.0, 64, 256), CancellationToken.None);
        builder.EndContour();

        SvgCubicSpan span = Assert.Single(builder.GetCubicSpans());
        Assert.Equal(0, span.Contour);
        Assert.Equal(new SvgDevicePoint(3, 0), span.FirstControl);
        Assert.Equal(new SvgDevicePoint(6, 0), span.SecondControl);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(new SvgDevicePoint(0, 8), contour[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(8, 8), contour[span.EndIndex]);
        Assert.True(span.EndIndex > span.StartIndex);
        Assert.Empty(builder.GetQuadSpans());
    }

    [Fact]
    public void Cubic_spans_stay_empty_without_retention_with_flattened_points_unchanged()
    {
        SvgContourBuilder plain = new(256, 4);
        plain.BeginContour(new SvgDevicePoint(0, 8));
        plain.AddCubic(new SvgDevicePoint(3, 0), new SvgDevicePoint(6, 0), new SvgDevicePoint(8, 8), new SvgFlattenPolicy(3.0, 64, 256), CancellationToken.None);
        plain.EndContour();
        Assert.Empty(plain.GetCubicSpans());

        SvgContourBuilder retained = new(256, 4);
        retained.EnableCubicSpanRetention();
        retained.BeginContour(new SvgDevicePoint(0, 8));
        retained.AddCubic(new SvgDevicePoint(3, 0), new SvgDevicePoint(6, 0), new SvgDevicePoint(8, 8), new SvgFlattenPolicy(3.0, 64, 256), CancellationToken.None);
        retained.EndContour();
        Assert.Single(retained.GetCubicSpans());
        Assert.Equal(plain.GetContour(0).ToArray(), retained.GetContour(0).ToArray());
    }

    [Fact]
    public void Cubic_path_retains_controls_through_device_transform()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g transform='translate(1,2)'><path d='M0,8 C3,0 6,0 8,8 Z' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        SvgCubicSpan span = Assert.Single(geometry.Polygon.CubicSpans);
        Assert.Equal(new SvgDevicePoint(4, 2), span.FirstControl);
        Assert.Equal(new SvgDevicePoint(7, 2), span.SecondControl);
        Assert.Equal(new SvgDevicePoint(1, 10), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(9, 10), geometry.Polygon.Points[span.EndIndex]);
        Assert.Empty(geometry.Polygon.QuadSpans);
    }

    [Fact]
    public void Mixed_quad_cubic_path_retains_both_channels()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,8 Q4,0 8,8 Z M0,0 C2,-4 6,-4 8,0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Single(geometry.Polygon.QuadSpans);
        Assert.Single(geometry.Polygon.CubicSpans);
    }

    [Fact]
    public void Duplicate_cubic_contours_drop_all_spans_through_merge()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,8 C3,0 6,0 8,8 Z M0,8 C3,0 6,0 8,8 Z' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Empty(geometry.Polygon.QuadSpans);
        Assert.Empty(geometry.Polygon.CubicSpans);
    }
    [Fact]
    public void Dusty_multi_contour_path_remaps_both_channels()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,8 Q4,0 8,8 Z M0,0 L0.0000000004,0 C2,-4 6,-4 8,0 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgQuadSpan quad = Assert.Single(geometry.Polygon.QuadSpans);
        Assert.Equal(new SvgDevicePoint(4, 0), quad.Control);
        SvgCubicSpan cubic = Assert.Single(geometry.Polygon.CubicSpans);
        Assert.Equal(new SvgDevicePoint(2, -4), cubic.FirstControl);
        Assert.Equal(new SvgDevicePoint(6, -4), cubic.SecondControl);
        Assert.Equal(new SvgDevicePoint(0, 0), geometry.Polygon.Points[cubic.StartIndex]);
        Assert.Equal(new SvgDevicePoint(8, 0), geometry.Polygon.Points[cubic.EndIndex]);
    }
    [Fact]
    public void Closing_dust_keeps_final_span_across_weld_compaction()
    {
        // Row 5 closing-span retention: the dust-divergent closing joint snaps bitwise shut instead of compacting, so the closing span survives with exact verb endpoints (the Ford seg73 span was lost here, walking 60px of curve as chords).
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 C2,-4 6,-4 8,0 L8,8 C6,8 2,8 0.0000000004,0.0000000004 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(2, geometry.Polygon.CubicSpans.Length);
        SvgCubicSpan span = geometry.Polygon.CubicSpans[0];
        Assert.Equal(new SvgDevicePoint(2, -4), span.FirstControl);
        Assert.Equal(new SvgDevicePoint(6, -4), span.SecondControl);
        Assert.Equal(new SvgDevicePoint(0, 0), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(8, 0), geometry.Polygon.Points[span.EndIndex]);
        SvgCubicSpan closing = geometry.Polygon.CubicSpans[1];
        Assert.Equal(new SvgDevicePoint(6, 8), closing.FirstControl);
        Assert.Equal(new SvgDevicePoint(2, 8), closing.SecondControl);
        Assert.Equal(new SvgDevicePoint(8, 8), geometry.Polygon.Points[closing.StartIndex]);
        Assert.Equal(new SvgDevicePoint(0, 0), geometry.Polygon.Points[closing.EndIndex]);
    }
    [Fact]
    public void Quarter_arc_records_kappa_controls_with_slice_indices()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.EnableCubicSpanRetention();
        builder.BeginContour(new SvgDevicePoint(10, 3));
        builder.AddArc(3, 3, 0, false, true, new SvgDevicePoint(13, 6), new SvgFlattenPolicy(0.25, 64, 256), CancellationToken.None);
        builder.EndContour();

        SvgCubicSpan span = Assert.Single(builder.GetCubicSpans());
        Assert.Equal(0, span.Contour);
        Assert.True(Math.Abs(span.FirstControl.X - 11.656854249492381) < 1e-9);
        Assert.True(Math.Abs(span.FirstControl.Y - 3.0) < 1e-9);
        Assert.True(Math.Abs(span.SecondControl.X - 13.0) < 1e-9);
        Assert.True(Math.Abs(span.SecondControl.Y - 4.343145750507619) < 1e-9);
        ReadOnlySpan<SvgDevicePoint> contour = builder.GetContour(0);
        Assert.Equal(new SvgDevicePoint(10, 3), contour[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(13, 6), contour[span.EndIndex]);
        Assert.Empty(builder.GetQuadSpans());
    }

    [Fact]
    public void Arc_spans_stay_empty_without_retention_with_sampled_points_unchanged()
    {
        SvgContourBuilder plain = new(256, 4);
        plain.BeginContour(new SvgDevicePoint(10, 3));
        plain.AddArc(3, 3, 0, false, true, new SvgDevicePoint(13, 6), new SvgFlattenPolicy(0.25, 64, 256), CancellationToken.None);
        plain.EndContour();
        Assert.Empty(plain.GetCubicSpans());

        SvgContourBuilder retained = new(256, 4);
        retained.EnableCubicSpanRetention();
        retained.BeginContour(new SvgDevicePoint(10, 3));
        retained.AddArc(3, 3, 0, false, true, new SvgDevicePoint(13, 6), new SvgFlattenPolicy(0.25, 64, 256), CancellationToken.None);
        retained.EndContour();
        Assert.Single(retained.GetCubicSpans());
        Assert.Equal(plain.GetContour(0).ToArray(), retained.GetContour(0).ToArray());
    }

    [Fact]
    public void Wide_arc_records_no_span_with_sampling_unchanged()
    {
        SvgContourBuilder builder = new(256, 4);
        builder.EnableCubicSpanRetention();
        builder.BeginContour(new SvgDevicePoint(0, 0));
        builder.AddArc(5, 5, 0, false, true, new SvgDevicePoint(10, 0), new SvgFlattenPolicy(0.25, 64, 256), CancellationToken.None);
        builder.EndContour();

        Assert.Empty(builder.GetCubicSpans());
        Assert.True(builder.GetContour(0).Length > 2);
    }

    [Fact]
    public void Rounded_rect_retains_four_arc_spans_with_tiled_endpoints()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='3' y='3' width='10' height='10' rx='3' fill='black'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Equal(4, geometry.Polygon.CubicSpans.Length);
        Assert.Empty(geometry.Polygon.QuadSpans);
        int previousEnd = -1;
        foreach (SvgCubicSpan span in geometry.Polygon.CubicSpans)
        {
            Assert.True(span.EndIndex > span.StartIndex);
            Assert.True(span.StartIndex >= previousEnd);
            previousEnd = span.EndIndex;
            Assert.True(span.StartIndex >= 0);
            Assert.True(span.EndIndex < geometry.Polygon.Points.Length);
        }
    }

    [Fact]
    public void Degenerate_rounded_rect_retains_no_arc_spans()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='3' y='3' width='10' height='10' rx='5' fill='black'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Empty(geometry.Polygon.CubicSpans);
        Assert.Empty(geometry.Polygon.QuadSpans);
    }

    [Fact]
    public void Cubic_controls_map_exactly_through_quarter_turn_matrix()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g transform='matrix(0,1,-1,0,0,0)'><path d='M8,48 C24,8 40,8 56,48 Z' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgCubicSpan span = Assert.Single(geometry.Polygon.CubicSpans);
        Assert.Equal(new SvgDevicePoint(-8, 24), span.FirstControl);
        Assert.Equal(new SvgDevicePoint(-8, 40), span.SecondControl);
        Assert.Equal(new SvgDevicePoint(-48, 8), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(-48, 56), geometry.Polygon.Points[span.EndIndex]);
        Assert.Empty(geometry.Polygon.QuadSpans);
    }
}
