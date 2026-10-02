using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgQuadSpanRetentionTests
{
    [Fact]
    public void Quad_path_retains_control_through_device_transform()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><g transform='translate(3,4)'><path d='M8,48 Q32,8 56,48 Z' fill='blue'/></g></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        SvgQuadSpan span = Assert.Single(geometry.Polygon.QuadSpans);
        Assert.Equal(0, span.Contour);
        Assert.Equal(new SvgDevicePoint(35, 12), span.Control);
        Assert.Equal(new SvgDevicePoint(11, 52), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(59, 52), geometry.Polygon.Points[span.EndIndex]);
    }

    [Fact]
    public void Quad_path_without_transform_retains_exact_endpoints()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 Q32,8 56,48 Z' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgQuadSpan span = Assert.Single(geometry.Polygon.QuadSpans);
        Assert.Equal(new SvgDevicePoint(32, 8), span.Control);
        Assert.Equal(new SvgDevicePoint(8, 48), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(56, 48), geometry.Polygon.Points[span.EndIndex]);
    }

    [Fact]
    public void Duplicate_quad_contours_drop_spans_through_merge()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M0,8 Q4,0 8,8 Z M0,8 Q4,0 8,8 Z' fill='blue'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Empty(geometry.Polygon.QuadSpans);
    }

    [Fact]
    public void Line_path_carries_no_spans()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L4,8 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Empty(geometry.Polygon.QuadSpans);
    }

    [Fact]
    public void Evenodd_quad_contour_retains_spans_without_crossings()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,48 Q32,8 56,48 Z' fill='blue' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);

        SvgGeometry geometry = Assert.Single(root.Geometries);
        Assert.Single(geometry.Polygon.QuadSpans);
    }
    [Fact]
    public void Dusty_path_remaps_spans_across_weld_compaction()
    {
        SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 L0.0000000004,0 L8,0 Q12,8 8,12 L0,12 Z' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgGeometry geometry = Assert.Single(root.Geometries);
        SvgQuadSpan span = Assert.Single(geometry.Polygon.QuadSpans);
        Assert.Equal(new SvgDevicePoint(12, 8), span.Control);
        Assert.Equal(new SvgDevicePoint(8, 0), geometry.Polygon.Points[span.StartIndex]);
        Assert.Equal(new SvgDevicePoint(8, 12), geometry.Polygon.Points[span.EndIndex]);
    }
}
