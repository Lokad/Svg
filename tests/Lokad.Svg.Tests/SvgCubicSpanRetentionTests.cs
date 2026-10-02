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
    public void Closing_dust_drops_final_span_across_weld_compaction()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 C2,-4 6,-4 8,0 L8,8 C6,8 2,8 0.0000000004,0.0000000004 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgCubicSpan span = Assert.Single(geometry.Polygon.CubicSpans);
        Assert.Equal(new SvgDevicePoint(2, -4), span.FirstControl);
        Assert.Equal(new SvgDevicePoint(6, -4), span.SecondControl);
        Assert.Equal(new SvgDevicePoint(0, 0), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(8, 0), geometry.Polygon.Points[span.EndIndex]);
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
