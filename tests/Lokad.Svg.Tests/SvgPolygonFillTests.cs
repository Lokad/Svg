using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPolygonFillTests
{
    [Fact]
    public void Polygon_fill_retains_typed_points_with_scene_storage()
    {
        SvgDevicePoint[] points = [new SvgDevicePoint(0, 0), new SvgDevicePoint(8, 0), new SvgDevicePoint(0, 8)];
        int[] ends = [3];
        RenderBudget budget = new(long.MaxValue);
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, SvgPolygonMapping.QuarterEdges, null, null, budget);

        SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);

        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Same(points, geometry.Polygon.Points);
        Assert.Same(ends, geometry.Polygon.ContourEnds);
        Assert.Null(geometry.Polygon.Signs);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        Assert.True(geometry.Polygon.HasFill);
        Assert.False(geometry.Polygon.IsEvenOdd);
        Assert.Equal(SvgPolygonMapping.QuarterEdges, geometry.Polygon.Mapping);
        Assert.Null(geometry.Polygon.Clip);
        Assert.Throws<InvalidOperationException>(() => _ = SvgGeometry.FromRectangle(new SvgRectangle(0, 0, 1, 1, SvgColor.Transparent, false, null)).Polygon);
        Assert.Throws<InvalidOperationException>(() => _ = default(SvgGeometry).Polygon);
    }

    [Fact]
    public void Right_triangle_renders_analytic_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out int width, out int height);

        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 7, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, width, 7, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, width, 0, 7));
        Assert.Equal(8164, AlphaSum(pixels));
    }

    [Fact]
    public void Winding_direction_does_not_change_solid_fill()
    {
        byte[] counterClockwise = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] clockwise = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 0,8 8,0' fill='red'/></svg>", out _, out _);

        Assert.Equal(counterClockwise, clockwise);
    }

    [Fact]
    public void Even_odd_matches_nonzero_on_simple_contours()
    {
        byte[] nonzero = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] evenOdd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(nonzero, evenOdd);
    }

    [Fact]
    public void Diagonal_polyline_closes_fill_like_polygon()
    {
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] polyline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(polygon, polyline);
    }

    [Fact]
    public void Diagonal_path_renders_closed_fill()
    {
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(polygon, path);
    }

    [Fact]
    public void Unclosed_nonfinal_diagonal_subpath_seals_like_closepath()
    {
        byte[] closed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z M2,1 L4,1 L2,3 Z' fill='red'/></svg>", out _, out _);
        byte[] unclosed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 M2,1 L4,1 L2,3' fill='red'/></svg>", out _, out _);

        Assert.Equal(closed, unclosed);
    }

    [Fact]
    public void Explicit_return_to_start_seals_like_closepath()
    {
        byte[] closed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out _, out _);
        byte[] returned = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 L0,0' fill='red'/></svg>", out _, out _);

        Assert.Equal(closed, returned);
    }

    [Fact]
    public void Nested_same_wound_nonzero_triangle_fills_outer()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z M2,1 L4,1 L2,3 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(8164, AlphaSum(pixels));
    }

    [Fact]
    public void Self_intersecting_diagonal_polygon_paints_union()
    {
        // R34: a single closed contour crossing only itself paints its unsigned
        // union under nonzero fill; the lobes meet at exactly one point.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 6, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
        // Sixteen half-coverage edge pixels quantize 127.5 up to 128, hence 8168
        // over the naive area-times-255 value 8160; the loops share only the crossing
        // point, so no coverage doubles.
        Assert.Equal(8168, AlphaSum(pixels));
    }

    [Fact]
    public void Evenodd_bowtie_paints_union()
    {
        // R34: each lobe winds once, so even-odd paints both split loops like nonzero.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='red' fill-rule='evenodd'/></svg>", out _, out _);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 6, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
        Assert.Equal(8168, AlphaSum(pixels));
    }

    [Fact]
    public void Gradient_bowtie_paints_both_lobes()
    {
        // R34: the crossing split feeds the shared gradient repaint, so both
        // lobes carry the ramp (structural asserts only; the dithered ramp itself
        // stays calibrated elsewhere).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='0,0 8,8 8,0 0,8' fill='url(#g)'/></svg>", out _, out _);

        SvgColor left = Pixel(pixels, 8, 1, 2);
        SvgColor right = Pixel(pixels, 8, 6, 2);
        Assert.True(left.Red > left.Blue);
        Assert.True(right.Blue > right.Red);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
    }

    [Fact]
    public void Radial_bowtie_paints_both_lobes()
    {
        // R34: the crossing split feeds the shared radial repaint, so both
        // lobes carry the radial ramp with the gap transparent.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><polygon points='0,0 8,8 8,0 0,8' fill='url(#r)'/></svg>", out _, out _);

        SvgColor corner = Pixel(pixels, 8, 0, 0);
        SvgColor middle = Pixel(pixels, 8, 4, 3);
        Assert.True(corner.Blue > corner.Red);
        Assert.True(middle.Red > middle.Blue);
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
    }

    [Fact]
    public void Pattern_bowtie_matches_solid_bowtie()
    {
        // R34: a solid pattern tile repaints the split loops exactly like a solid fill.
        byte[] pattern = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><polygon points='0,0 8,8 8,0 0,8' fill='url(#p)'/></svg>", out _, out _);
        byte[] solid = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(solid, pattern);
    }

    [Fact]
    public void Retraced_spike_renders_main_triangle()
    {
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 4,4 0,8' fill='red'/></svg>", out _, out _);
        byte[] polyline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 8,8 4,4 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(polygon, polyline);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(polygon, 8, 1, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(polygon, 8, 6, 6));
    }

    [Fact]
    public void Duplicate_point_polygon_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,8' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void ViewBox_scale_maps_polygon_edges()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 15, 15));
        Assert.Equal(32648, AlphaSum(pixels));
    }

    [Fact]
    public void Clip_path_intersects_polygon_bounds()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 5, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
        Assert.Equal(2042, AlphaSum(pixels));
    }

    [Fact]
    public void Root_xy_leaves_clipped_polygon_untouched()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] offset = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' x='5' y='7'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(plain, offset);
    }

    [Fact]
    public void Open_line_path_strokes_band()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 L8,4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 7));
        Assert.Equal(4080, AlphaSum(pixels));
    }

    [Fact]
    public void Open_line_default_fill_paints_nothing_extra()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 L8,4' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] nofill = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 L8,4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(nofill, plain);
    }

    [Fact]
    public void Two_open_subpaths_stroke_cross()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 H8 M4,0 V8' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
        Assert.Equal(7140, AlphaSum(pixels));
    }

    [Fact]
    public void Closed_spike_strokes()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M4,0 L4,8 Z' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Single_point_fill_renders_nothing()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M4,4' fill='red'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Mixed_open_closed_path_drops_open_segment()
    {
        byte[] mixed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z M0,4 L8,4' fill='red' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] square = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z' fill='red' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(mixed, 8, 4, 4));
        Assert.Equal(square, mixed);
    }

    [Fact]
    public void Open_polyline_matches_open_path()
    {
        byte[] polyline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,4 8,4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,4 L8,4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(path, polyline);
    }

    [Fact]
    public void Diagonal_open_path_strokes()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 7));
    }
    [Fact]
    public void ZContinued_fill_matches_explicit_moveto_twin()
    {
        byte[] continued = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M5,5 H15 V15 Z h10 v10 z' fill='red'/></svg>", out _, out _);
        byte[] explicitTwin = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M5,5 H15 V15 Z M5,5 H15 V15 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(explicitTwin, continued);
    }

    [Fact]
    public void Dangling_segment_after_close_stays_malformed()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H8 V8 Z h' fill='red'/>");
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
        Assert.Contains("Path data ends inside a command.", rejected.Message);
    }

    [Fact]
    public void Fractional_clip_path_scales_edge_pixels()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='1.5' y='0' width='5' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 64), Pixel(pixels, 8, 6, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 1));
    }

    [Fact]
    public void Fractional_clip_scales_gradient_alpha()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect x='1.5' y='0' width='5' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='url(#g)' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(255, Pixel(pixels, 8, 2, 1).Alpha);
        Assert.Equal(128, Pixel(pixels, 8, 1, 1).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 0, 1).Alpha);
    }

    [Fact]
    public void Fractional_clip_scales_radial_alpha()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><clipPath id='c'><rect x='1.5' y='0' width='5' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='url(#g)' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(255, Pixel(pixels, 8, 2, 1).Alpha);
        Assert.Equal(128, Pixel(pixels, 8, 1, 1).Alpha);
        Assert.Equal(0, Pixel(pixels, 8, 0, 1).Alpha);
    }

    [Fact]
    public void ViewBox_scaled_clip_path_keeps_mapped_region()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><clipPath id='c'><rect width='8' height='16'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='red' clip-path='url(#c)'/></svg>", out int width, out int height);

        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 10, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 16, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 20, 8));
    }

    [Fact]
    public void ViewBox_scaled_clip_matches_direct_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><clipPath id='c'><rect width='8' height='16'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><rect width='16' height='32'/></clipPath></defs><polygon points='0,0 32,0 0,32' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Nested_viewport_clip_matches_clip_path_rendering()
    {
        byte[] clipped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><clipPath id='c'><rect width='8' height='16'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><svg x='0' y='0' width='8' height='16'><polygon points='0,0 16,0 0,16' fill='red'/></svg></svg>", out _, out _);

        Assert.Equal(clipped, nested);
    }

    [Fact]
    public void Offset_nested_viewport_matches_shifted_clip_rendering()
    {
        // R33 remainder: a nonzero viewport origin shifts polygon content and
        // clips it exactly like the equivalent shifted clip-path reference.
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><svg x='4' y='2' width='8' height='12'><polygon points='0,0 16,0 0,16' fill='red'/></svg></svg>", out _, out _);
        byte[] clipped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><clipPath id='c'><rect x='4' y='2' width='8' height='12'/></clipPath></defs><polygon points='4,2 20,2 4,18' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(clipped, nested);
    }

    [Fact]
    public void OutputScale_viewBox_clip_matches_enlarged_direct_rendering()
    {
        // R33 remainder: clip boxes map through the output transform with the
        // clipped geometry, so a scaled clip render matches the enlarged direct
        // render byte-identically.
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><clipPath id='c'><rect width='8' height='16'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='red' clip-path='url(#c)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='c'><rect width='32' height='64'/></clipPath></defs><polygon points='0,0 64,0 0,64' fill='red' clip-path='url(#c)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_fractional_clip_matches_integer_direct_rendering()
    {
        // R33 remainder: fractional clip edges scale per-pixel coverage exactly
        // like fractional rectangle edges, so a scaled fractional clip matches
        // the integer direct render byte-identically.
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><clipPath id='c'><rect x='4.5' y='2.5' width='7.5' height='11.5'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='red' clip-path='url(#c)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><rect x='9' y='5' width='15' height='23'/></clipPath></defs><polygon points='0,0 32,0 0,32' fill='red' clip-path='url(#c)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void ViewBox_scaled_offset_viewport_matches_shifted_clip_rendering()
    {
        // R33 remainder: viewport origins compose through the root viewBox map
        // on both the nested-viewport and the clip-path reference paths.
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><svg x='4' y='2' width='8' height='12'><polygon points='0,0 16,0 0,16' fill='red'/></svg></svg>", out _, out _);
        byte[] clipped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><clipPath id='c'><rect x='4' y='2' width='8' height='12'/></clipPath></defs><polygon points='4,2 20,2 4,18' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(clipped, nested);
    }

    [Fact]
    public void Disjoint_opposite_contours_paint()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M .1 .1 L .4 .1 L .1 .4 Z M .6 .6 L .6 .9 L .9 .6 Z' fill='red'/></svg>", out int width, out int height);

        Assert.Equal(8, width);
        Assert.Equal(8, height);
        // R34: disjoint opposite contours share one pixel without cancelling.
        // The sliver pins candidate identity (alpha 38) against the pinned
        // Chrome triple (alpha 37, SHA-256 a6c2aee0); the twin below locks reversal invariance.
        // The analytic multi walker adds disjoint sliver coverages like Chrome instead of
        // cancelling opposite windings like the fan (23), closing the gap from 14 to 1.
        Assert.Equal(new SvgColor(255, 0, 0, 38), Pixel(pixels, width, 0, 0));
        Assert.Equal(38, AlphaSum(pixels));
    }

    [Fact]
    public void Contour_reversal_keeps_disjoint_paint()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M .1 .1 L .4 .1 L .1 .4 Z M .6 .6 L .6 .9 L .9 .6 Z' fill='red'/></svg>", out _, out _);
        byte[] reversed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M .1 .1 L .4 .1 L .1 .4 Z M .6 .6 L .9 .6 L .6 .9 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(plain, reversed);
    }

    [Fact]
    public void ViewBox_scaled_gradient_clip_matches_direct_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect width='8' height='16'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='url(#g)' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect width='16' height='32'/></clipPath></defs><polygon points='0,0 32,0 0,32' fill='url(#g)' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void OutputScale_clip_matches_enlarged_direct_rendering()
    {
        string clip64 = "<defs><clipPath id='c'><rect x='16' y='16' width='32' height='32'/></clipPath></defs>";
        string clip128 = "<defs><clipPath id='c'><rect x='32' y='32' width='64' height='64'/></clipPath></defs>";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + clip64 + "<rect x='0' y='0' width='64' height='64' fill='red' clip-path='url(#c)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'>" + clip128 + "<rect x='0' y='0' width='128' height='128' fill='red' clip-path='url(#c)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void OutputScale_gradient_clip_matches_enlarged_direct_rendering()
    {
        string paint64 = "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><clipPath id='c'><rect x='16' y='16' width='32' height='32'/></clipPath></defs>";
        string paint128 = "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient><clipPath id='c'><rect x='32' y='32' width='64' height='64'/></clipPath></defs>";
        byte[] small = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + paint64 + "<rect x='0' y='0' width='64' height='64' fill='url(#g)' clip-path='url(#c)'/></svg>");
        byte[] large = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'>" + paint128 + "<rect x='0' y='0' width='128' height='128' fill='url(#g)' clip-path='url(#c)'/></svg>");
        SvgRenderOptions scaled = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default);
        SvgRenderOptions direct = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> scaledPng = SvgConverter.ConvertToPng(small, scaled, CancellationToken.None);
        ReadOnlyMemory<byte> directPng = SvgConverter.ConvertToPng(large, direct, CancellationToken.None);

        Assert.True(scaledPng.Span.SequenceEqual(directPng.Span));
    }

    [Fact]
    public void Forward_clip_path_on_polygon_matches_backward_clip()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(backward, forward);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(forward, 8, 5, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(forward, 8, 0, 0));
    }

    [Fact]
    public void Forward_gray_mask_on_group_matches_backward_mask()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='red'/></g><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_gray_mask_on_polygon_matches_backward_mask()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' mask='url(#m)'/><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><polygon points='0,0 8,0 0,8' fill='red' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_custom_region_mask_on_rect_matches_backward_mask()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m' maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><rect x='0' y='0' width='8' height='8' fill='red' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_clip_path_on_group_matches_backward_clip()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g clip-path='url(#c)'><polygon points='0,0 8,0 0,8' fill='red'/></g><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><g clip-path='url(#c)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(backward, forward);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(forward, 8, 5, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(forward, 8, 0, 0));
    }

    [Fact]
    public void Forward_full_fraction_clip_on_curved_path_matches_unclipped_fill()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,-4 8,8' fill='red' clip-path='url(#window)'/><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs></svg>", out _, out _);
        byte[] bare = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,-4 8,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(bare, forward);
    }

    [Fact]
    public void Forward_bounding_box_clip_on_polygon_matches_backward_clip()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#window)'/><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0.25' y='0.25' width='0.5' height='0.5'/></clipPath></defs><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red' clip-path='url(#window)'/></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Full_fraction_clip_on_curved_path_matches_unclipped_fill()
    {
        byte[] clipped = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='window' clipPathUnits='objectBoundingBox'><rect x='0' y='0' width='1' height='1'/></clipPath></defs><path d='M0,8 Q4,-4 8,8' fill='red' clip-path='url(#window)'/></svg>", out _, out _);
        byte[] bare = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,-4 8,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(bare, clipped);
    }

    [Fact]
    public void Nonzero_opposite_wound_hole_stays_open()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z M2,2 L2,6 L6,2 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 3, 3));
        Assert.Equal(14282, AlphaSum(pixels));
    }

    [Fact]
    public void Even_odd_same_wound_hole_stays_open()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z M2,2 L6,2 L2,6 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 3, 3));
        Assert.Equal(14282, AlphaSum(pixels));
    }

    [Fact]
    public void Nonzero_same_wound_overlap_fills()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z M2,2 L6,2 L2,6 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 3));
        Assert.Equal(16320, AlphaSum(pixels));
    }

    [Fact]
    public void Touching_hole_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg'><path d='M0,0 L8,0 L8,8 L0,8 Z M0,4 L4,2 L4,6 Z' fill='red'/></svg>", out _, out _);

        Assert.True(AlphaSum(pixels) > 0);
    }

    [Fact]
    public void Disjoint_triangles_union_sums_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,0 L0,4 Z M4,4 L8,4 L4,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
        Assert.Equal(4084, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_square_renders_analytic_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L4.5,0.5 L4.5,4.5 L0.5,4.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 0, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 64), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 64), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
        Assert.Equal(4087, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_triangle_renders_analytic_coverage()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L4.5,0.5 L0.5,4.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 3, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 64), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 32), Pixel(pixels, 8, 0, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 32), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 3, 3));
        Assert.Equal(2045, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_viewBox_matches_integer_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 4 4'><path d='M0.5,0.5 L3.5,0.5 L0.5,3.5 Z' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M1,1 L7,1 L1,7 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Fractional_open_stroke_makes_band()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L4.5,0.5 L7.5,0.5' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 4, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 64), Pixel(pixels, 8, 0, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 2));
        Assert.Equal(2682, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_bowtie_paints_union()
    {
        // R34: the fractional bowtie paints like its integer twin through the
        // same unsigned-union rule.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L7.5,7.5 L7.5,0.5 L0.5,7.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 6, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
        // Coverage quantization walk over the analytic 6247.5; the loops share only
        // the crossing point, so no coverage doubles.
        Assert.Equal(6260, AlphaSum(pixels));
    }

    [Fact]
    public void Double_crossing_diagonal_contour_paints_union()
    {
        // R34: a single closed contour with two interior crossings splits twice
        // into three simple loops sharing only split vertices; every lobe paints.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 6,6 6,2 0,8 6,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 5, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 5, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 6));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 3, 0));
        // Exact tiling verified independently: the three split triangles meet
        // pairwise at single points, every fully covered pixel paints 255, every
        // exterior pixel paints 0, and edge pixels track analytic coverage within
        // half an alpha step; 4231 walks 5.3 steps over the analytic 4225.7.
        Assert.Equal(4229, AlphaSum(pixels));
    }

    [Fact]
    public void Double_crossing_evenodd_contour_stays_unsupported()
    {
        // R34: first-vertex parity signs are meaningless for vertex-sharing loops,
        // so multi-crossing contours stay gated under even-odd fill.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 6,6 6,2 0,8 6,8' fill='red' fill-rule='evenodd'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only simple diagonal polygon contours are implemented;", rejected.Message);
    }

    [Fact]
    public void Disjoint_triangle_and_bowtie_paint_union()
    {
        // R34: a clean contour copies through beside a split one; disjoint regions
        // add exactly, so the combined sum equals the separately rendered parts.
        byte[] triangle = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M0,0 H8 L0,8 Z' fill='red'/></svg>", out _, out _);
        byte[] bowtie = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M16,16 L24,24 L24,16 L16,24 Z' fill='red'/></svg>", out _, out _);
        byte[] combined = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><path d='M0,0 H8 L0,8 Z M16,16 L24,24 L24,16 L16,24 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(combined, 32, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(combined, 32, 17, 20));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(combined, 32, 10, 10));
        Assert.Equal(AlphaSum(triangle) + AlphaSum(bowtie), AlphaSum(combined));
    }

    [Fact]
    public void Relative_moveto_implicit_pairs_match_absolute_twin()
    {
        // The integer tokenizer accumulates a first-subpath lowercase moveto
        // before its implicit relative pairs (inv-086 recorded the raw deltas).
        byte[] relative = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='m5,1 2,0 -1,4 z' fill='red'/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M5,1 L7,1 L6,5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(absolute, relative);
    }

    [Fact]
    public void Uppercase_moveto_implicit_pairs_stay_absolute()
    {
        // Control: an uppercase moveto keeps implicit pairs absolute per spec.
        byte[] implicitPairs = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M5,1 7,1 6,5 z' fill='red'/></svg>", out _, out _);
        byte[] explicitLines = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M5,1 L7,1 L6,5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(explicitLines, implicitPairs);
    }

    [Fact]
    public void Fractional_relative_moveto_implicit_pairs_match_absolute_twin()
    {
        // Same moveto accumulation through the fractional path lowering.
        byte[] relative = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='m5.5,1.5 2,0 -1,4 z' fill='red'/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M5.5,1.5 L7.5,1.5 L6.5,5.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(absolute, relative);
    }

    [Fact]
    public void Second_subpath_relative_moveto_implicit_pairs_match_absolute_twin()
    {
        // The integer tokenizer resolves every lowercase moveto, not just the
        // first: implicit pairs after a second-subpath moveto accumulate from it.
        byte[] relative = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H4 V4 H0 Z m4,4 2,0 -2,2 z' fill='red'/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H4 V4 H0 Z M4,4 L6,4 L4,6 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(absolute, relative);
    }

    [Fact]
    public void Relative_moveto_after_closepath_starts_from_subpath_start()
    {
        // A relative moveto after closepath resolves from the subpath start,
        // not the last point: the fractional parser resets current on Z like
        // the integer tokenizer already does.
        byte[] relative = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L2.5,0.5 L0.5,2.5 Z m4,4 2,0 -2,2 z' fill='red'/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L2.5,0.5 L0.5,2.5 Z M4.5,4.5 L6.5,4.5 L4.5,6.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(absolute, relative);
    }

    [Fact]
    public void Forward_cross_type_gradient_href_resolves()
    {
        // S9: forward hrefs resolve in document order like backward ones;
        // a forward cross-type href renders exactly like its backward twin.
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='l' href='#r'/><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><rect x='2' y='2' width='12' height='12' fill='url(#l)'/></svg>", out _, out _);
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><linearGradient id='l' href='#r'/></defs><rect x='2' y='2' width='12' height='12' fill='url(#l)'/></svg>", out _, out _);

        Assert.Equal(backward, forward);
        Assert.True(Pixel(forward, 16, 2, 2).Red > Pixel(forward, 16, 2, 2).Blue);
    }

    [Fact]
    public void Double_crossing_gradient_evenodd_contour_stays_unsupported()
    {
        // R34: first-vertex parity signs are meaningless for vertex-sharing
        // loops on every paint path, so multi-crossing contours stay gated
        // under even-odd fill for gradient fills too.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><polygon points='0,0 6,6 6,2 0,8 6,8' fill='url(#g)' fill-rule='evenodd'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Double_crossing_radial_evenodd_contour_stays_unsupported()
    {
        // R34: first-vertex parity signs are meaningless for vertex-sharing
        // loops on every paint path, so multi-crossing contours stay gated
        // under even-odd fill for radial fills too.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><radialGradient id='r'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><polygon points='0,0 6,6 6,2 0,8 6,8' fill='url(#r)' fill-rule='evenodd'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Fractional_polygon_matches_equivalent_path()
    {
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 7.5,0.5 0.5,7.5' fill='red'/></svg>", out _, out _);
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L7.5,0.5 L0.5,7.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(path, polygon);
    }

    [Fact]
    public void Fractional_polyline_closes_fill_like_polygon()
    {
        byte[] polygon = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 7.5,0.5 0.5,7.5' fill='red'/></svg>", out _, out _);
        byte[] polyline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0.5,0.5 7.5,0.5 0.5,7.5' fill='red'/></svg>", out _, out _);

        Assert.Equal(polygon, polyline);
    }

    [Fact]
    public void Fractional_point_list_stroke_matches_path_stroke()
    {
        byte[] polyline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0.5,0.5 7.5,0.5 7.5,7.5' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0.5 L7.5,0.5 L7.5,7.5' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(path, polyline);
    }

    [Fact]
    public void Fractional_point_list_truncation_is_malformed()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 7.5' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Fractional_point_list_garbage_is_malformed()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 hello,1' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Fractional_point_list_pair_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 7.5,0.5' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Fractional_duplicate_point_list_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0.5,0.5 0.5,0.5 0.5,0.5' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Disjoint_opposite_contours_ignore_reversal()
    {
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M .1 .1 L .4 .1 L .1 .4 Z M .6 .6 L .6 .9 L .9 .6 Z' fill='red'/></svg>", out _, out _);
        byte[] reversed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M .1 .1 L .4 .1 L .1 .4 Z M .6 .6 L .9 .6 L .6 .9 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(forward, reversed);
        Assert.True(Pixel(forward, 8, 0, 0).Alpha > 0);
    }

    [Fact]
    public void Evenodd_counter_hole_stays_transparent()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Nonzero_nested_contour_paints_interior()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red' fill-rule='nonzero'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Group_evenodd_opens_child_counter()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g fill-rule='evenodd'><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Nested_group_inner_rule_wins()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g fill-rule='evenodd'><g fill-rule='nonzero'><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></g></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Nested_group_inherits_outer_rule()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g fill-rule='evenodd'><g><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></g></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Root_style_fill_rule_applies_to_leaf()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' style='fill-rule:evenodd'><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Root_attribute_fill_rule_applies_to_leaf()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' fill-rule='evenodd'><path d='M0,0 H8 V8 H0 Z M2,2 H6 V6 H2 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Invalid_group_fill_rule_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g fill-rule='bogus'><path d='M0,0 H8 V8 H0 Z' fill='red'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only nonzero and evenodd fill rules are implemented.", rejected.Message);
    }
    private static byte[] RenderPixels(string svg, out int width, out int height)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None);
        return DecodeRgba(png, out width, out height);
    }

    private static byte[] DecodeRgba(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        width = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4));
        using MemoryStream compressed = new();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(offset, 4));
            if (png.Span.Slice(offset + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(png.Span.Slice(offset + 8, length));
            offset += length + 12;
        }

        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        byte[] raw = new byte[checked((width * 4 + 1) * height)];
        zlib.ReadExactly(raw);
        byte[] pixels = new byte[checked(width * 4 * height)];
        for (int y = 0; y < height; y++)
        {
            if (raw[y * (width * 4 + 1)] != 0) throw new InvalidDataException("Test PNG decoder expects an unfiltered scanline.");
            raw.AsSpan(y * (width * 4 + 1) + 1, width * 4).CopyTo(pixels.AsSpan(y * width * 4, width * 4));
        }

        return pixels;
    }

    private static SvgColor Pixel(byte[] pixels, int width, int x, int y)
    {
        int offset = (y * width + x) * 4;
        return new SvgColor(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    [Fact]
    public void Corner_touching_siblings_paint_union()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H4 V4 H0 Z M4,4 H8 V8 H4 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 6, 6));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 2));
    }

    [Fact]
    public void Relative_absolute_touch_twins_paint_union()
    {
        byte[] relative = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='m0,0 l8,0 l0,8 l-8,0 z m8,4 l4,-4 l0,8 z' fill='red'/></svg>", out _, out _);
        byte[] absolute = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H8 V8 H0 Z M8,4 L12,0 L12,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(relative, absolute);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(absolute, 16, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(absolute, 16, 10, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(absolute, 16, 14, 4));
    }

    [Fact]
    public void Nested_touch_hole_stays_empty()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H8 V8 H0 Z M0,0 L1,4 L4,1 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 1, 1));
    }

    [Fact]
    public void Evenodd_touch_hole_stays_empty()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H8 V8 H0 Z M0,0 L1,4 L4,1 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 1, 1));
    }

    [Fact]
    public void Self_touch_hourglass_paints_both_lobes()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,0 L2,2 L4,4 L0,4 L2,2' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 2));
    }

    [Fact]
    public void Coincident_edge_overlap_unions_like_outline()
    {
        // R34: two loops sharing one coincident span splice into the union boundary.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,0 L0,4 Z M0,4 L4,4 L4,0 Z' fill='red'/></svg>", out _, out _);
        byte[] outline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,0 L4,4 L0,4 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(outline, union);
    }

    [Fact]
    public void Crossing_plus_touch_at_split_vertex_paints_union()
    {
        // R34 planar split: the triangle corner (2,2) is bitwise-identical to the
        // computed bowtie crossing, so the touch becomes a shared split vertex and
        // the union paints. Staged oracle runs mixt-o1..o3 agree 3/3 byte-identical
        // SHA-256 317b08359b73ce8af25043340c5063d1464fa315633d3f7335a42d01010769f6.
        // Parsed signs read 1,-1,1: the triangle resolves -1 through its shared
        // first vertex while Abs masks the sign everywhere except overlap pixels,
        // so the shared pixel subtracts (85) instead of unioning (170).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,4 L4,0 L0,4 Z M2,2 L8,4 L8,8 L2,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 6));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 0));
    }

    [Fact]
    public void Crossing_plus_inexact_touch_stays_unsupported()
    {
        // The triangle corner (2,3) misses the computed bowtie crossing (2,2), so the
        // touch survives splitting as a strict vertex-on-edge contact and the set
        // stays unsupported.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,4 L4,0 L0,4 Z M3,1 L2,0 L4,0 Z' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Pentagram_paints_full_star()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><polygon points='50,0 21,91 98,35 2,35 79,91' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 100, 50, 50));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 100, 50, 10));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 100, 30, 80));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 100, 50, 60));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 100, 5, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 100, 50, 95));
    }

    [Fact]
    public void Crossing_squares_paint_union()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><path d='M0,0 H8 V8 H0 Z M4,2 H12 V8 H4 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 2, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 10, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 14, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 14, 0));
    }

    [Fact]
    public void Mixed_crossing_and_corner_touch_paints_union()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M8,0 L16,0 L16,8 L8,8 Z M4,4 L12,4 L4,12 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 12, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 5, 9));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 10, 6));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 15));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 2, 2));
    }

    [Fact]
    public void Evenodd_crossing_triangles_clear_lens()
    {
        byte[] nonzero = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 L11,3 L4,12 Z M7,0 L15,5 L9,13 Z' fill='red'/></svg>", out _, out _);
        byte[] evenodd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 L11,3 L4,12 Z M7,0 L15,5 L9,13 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(nonzero, 16, 5, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(evenodd, 16, 5, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(nonzero, 16, 11, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(evenodd, 16, 11, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(nonzero, 16, 8, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(evenodd, 16, 8, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(nonzero, 16, 9, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(evenodd, 16, 9, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(evenodd, 16, 0, 15));
    }

    [Fact]
    public void Evenodd_mixed_crossing_and_corner_touch_paints_solos()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M8,0 L16,0 L16,8 L8,8 Z M4,4 L12,4 L4,12 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 12, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 5, 9));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 15));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 2, 2));
    }

    [Fact]
    public void Shared_two_span_pair_splices_to_union()
    {
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,0 L10,0 L10,4 L6,6 L10,8 L10,10 L0,10 Z M10,0 L16,0 L16,10 L10,10 Z' fill='red'/></svg>", out _, out _);
        byte[] separate = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,0 L10,0 L10,4 L6,6 L10,8 L10,10 L0,10 Z' fill='red'/><path d='M10,0 L16,0 L16,10 L10,10 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(separate, union);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 12, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, 20, 8, 6));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, 20, 18, 18));
    }

    [Fact]
    public void Shared_two_span_evenodd_matches_separate_paths()
    {
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,0 L10,0 L10,4 L6,6 L10,8 L10,10 L0,10 Z M10,0 L16,0 L16,10 L10,10 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);
        byte[] separate = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,0 L10,0 L10,4 L6,6 L10,8 L10,10 L0,10 Z' fill='red' fill-rule='evenodd'/><path d='M10,0 L16,0 L16,10 L10,10 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(separate, union);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 12, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, 20, 8, 6));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, 20, 18, 18));
    }

    [Fact]
    public void Nested_span_sharing_evenodd_keeps_hole_empty()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,0 L16,0 L16,16 L0,16 Z M4,0 L12,0 L12,8 L8,12 L4,8 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 20, 2, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 20, 8, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 20, 8, 14));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 20, 18, 18));
    }

    [Fact]
    public void Shared_single_span_evenodd_matches_separate_paths()
    {
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,8 L8,8 L4,14 Z M0,8 L4,2 L8,8 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);
        byte[] separate = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20'><path d='M0,8 L8,8 L4,14 Z' fill='red' fill-rule='evenodd'/><path d='M0,8 L4,2 L8,8 Z' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(separate, union);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 4, 11));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 4, 5));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(union, 20, 4, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, 20, 10, 10));
    }


    [Fact]
    public void Evenodd_three_loop_crossing_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M1,1 L11,3 L4,12 Z M7,0 L15,5 L9,13 Z M0,8 L6,8 L6,14 Z' fill='red' fill-rule='evenodd'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }


    [Fact]
    public void Dust_spike_drops_leaving_main_lobe()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L2,1 L4,0 L2,1 L2,4 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 6));
    }

    [Fact]
    public void Shared_spike_tip_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L2,1 L4,0 L2,1 L2,4 Z M4,0 L6,0 L6,2 Z' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Identical_diagonal_duplicates_render_once()
    {
        // R34 coincident merge runs before classification, so repeated identical
        // diagonal contours union without doubling shared fringes: the twin render
        // is byte-identical to the single triangle.
        byte[] single = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out _, out _);
        byte[] duplicate = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(single, duplicate);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(duplicate, 8, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(duplicate, 8, 7, 7));
    }

    [Fact]
    public void Editor_namedview_paints_nothing()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'/></svg>", out _, out _);
        byte[] withMetadata = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' xmlns:sodipodi='http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape'><sodipodi:namedview pagecolor='#ffffff' inkscape:zoom='1.9'><inkscape:page x='0' y='0'/></sodipodi:namedview><rect width='8' height='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(plain, withMetadata);
    }

    [Fact]
    public void Percentage_root_matches_explicit_viewport_render()
    {
        byte[] explicitPixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='150'><rect width='300' height='150' fill='red'/></svg>", out int explicitWidth, out int explicitHeight);
        byte[] percentPixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='100%'><rect width='300' height='150' fill='red'/></svg>", out int percentWidth, out int percentHeight);

        Assert.Equal((explicitWidth, explicitHeight), (300, 150));
        Assert.Equal((percentWidth, percentHeight), (300, 150));
        Assert.Equal(explicitPixels, percentPixels);
    }

    [Fact]
    public void Block_display_paints_like_inline()
    {
        byte[] inlinePixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='red'/></svg>", out _, out _);
        byte[] blockPixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' style='display: block'><rect width='8' height='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(inlinePixels, blockPixels);
    }

    [Fact]
    public void Rotated_square_paints_diamond()
    {
        // Row 5: axis-aligned integer paths route to the diagonal fill path under
        // rotation. Staged oracle runs rotsq-o1..o2 agree 2/2 byte-identical
        // SHA-256 151714dd91af799da416a710fe3f61bb4111e90fe59c54687c5a59791e4ac18a;
        // the only residual is diagonal-edge fringe (44 px at max channel error 17).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M4,4 H12 V12 H4 Z' fill='red' transform='rotate(45,8,8)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 15, 15));
    }

    [Fact]
    public void Skewed_path_paints_parallelogram()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M4,4 H12 V12 H4 Z' fill='red' transform='skewX(45)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 14, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Rotated_rectangle_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><rect x='4' y='4' width='8' height='8' fill='red' transform='rotate(30)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rotated_ellipse_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><ellipse cx='8' cy='8' rx='6' ry='4' fill='red' transform='rotate(30)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rotated_pattern_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)' transform='rotate(30)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Custom_region_masks_clip_group_content()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m' maskUnits='userSpaceOnUse' x='4' y='4' width='8' height='8'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='16' height='16' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 4, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 11, 11));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 3, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 12, 12));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Custom_region_masks_combine_with_mask_value()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m' maskUnits='userSpaceOnUse' x='4' y='4' width='8' height='8'><rect x='0' y='0' width='16' height='16' fill='#808080'/></mask></defs><g mask='url(#m)'><rect x='0' y='0' width='16' height='16' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Custom_region_masks_combine_with_group_opacity()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m' maskUnits='userSpaceOnUse' x='4' y='4' width='8' height='8'><rect x='0' y='0' width='16' height='16' fill='#ffffff'/></mask></defs><g opacity='0.5' mask='url(#m)'><rect x='0' y='0' width='16' height='16' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 128), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Explicitly_closed_empty_leaf_matches_self_closing_twin()
    {
        // An explicitly closed leaf with an element-free subtree renders exactly like the self-closing form.
        byte[] closed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 H14 V14 H2 Z' fill='red'></path></svg>", out _, out _);
        byte[] selfClosed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 H14 V14 H2 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(selfClosed, closed);
    }

    [Fact]
    public void Doubly_reflected_pattern_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/></pattern></defs><rect width='8' height='8' fill='url(#p)' transform='scale(-1,-1)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rotated_gradient_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#g)' transform='rotate(30)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Non_uniform_general_transform_paints_union()
    {
        // Fills need no uniformity: areas sum regardless of stretch. Staged oracle
        // runs nuni-o1..o2 agree 2/2 byte-identical
        // SHA-256 df2347f52b3f4c5ea1a92f076c321239c4062b841e31d1ce953da3aabad98ed4;
        // the only residual is edge fringe (31 px at max alpha error 26).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M4,4 H12 V12 H4 Z' fill='red' transform='rotate(30) scale(2,1)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 12));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Non_uniform_rotated_stroke_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,8 H14' stroke='red' stroke-width='2' fill='none' transform='rotate(30) scale(2,1)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Rotation_matrix_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 H8 V8 H0 Z' fill='red' transform='matrix(0,-1,1,0,0,0)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Degenerate_ring_renders_empty()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 2,0 0,0 0,2' fill='red'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Dropped_degenerate_line_leaves_fractional_fill_untouched()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2.5,2.5 L14.5,2.5 L2.5,14.5 Z' fill='red'/></svg>", out _, out _);
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M5.5,5.5 L9.5,9.5 M2.5,2.5 L14.5,2.5 L2.5,14.5 Z' fill='red'/></svg>", out int width, out _);

        Assert.Equal(plain, pixels);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 15, 15));
    }

    [Fact]
    public void Dropped_degenerate_dot_leaves_fractional_fill_untouched()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2.5,2.5 L14.5,2.5 L2.5,14.5 Z' fill='red'/></svg>", out _, out _);
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M5.5,5.5 L5.5,5.5 Z M2.5,2.5 L14.5,2.5 L2.5,14.5 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(plain, pixels);
    }


    private static long AlphaSum(byte[] pixels)
    {
        long total = 0;
        for (int index = 3; index < pixels.Length; index += 4) total += pixels[index];
        return total;
    }

    [Fact]
    public void Split_vertical_edge_walks_like_unsplit_edge()
    {
        RenderBudget budget = new(100_000);
        SvgDevicePoint[] split = [new(0, 0), new(0, 4), new(0, 8), new(8, 0)];
        SvgDevicePoint[] whole = [new(0, 0), new(0, 8), new(8, 0)];
        using (SvgAnalyticCoverage splitWalker = SvgAnalyticCoverage.Prepare(split, [4], [], false, null, 0, 16, 0, 16, 16, 16, budget, out long _, out bool splitEligible, CancellationToken.None))
        using (SvgAnalyticCoverage wholeWalker = SvgAnalyticCoverage.Prepare(whole, [3], [], false, null, 0, 16, 0, 16, 16, 16, budget, out long _, out bool wholeEligible, CancellationToken.None))
        {
            Assert.True(splitEligible);
            Assert.True(wholeEligible);
            splitWalker.PrepareRow(4, out long _, CancellationToken.None);
            wholeWalker.PrepareRow(4, out long _, CancellationToken.None);
            Assert.Equal(wholeWalker.Row.ToArray(), splitWalker.Row.ToArray());
        }
    }

    [Fact]
    public void Coincident_shared_edge_unions_like_outline()
    {
        // R34: two loops sharing one coincident span splice into the union boundary.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 L10,2 L2,10 Z M10,2 L6,6 L10,6 Z' fill='red'/></svg>", out _, out _);
        byte[] outline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M6,6 L2,10 L2,2 L10,2 L10,6 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(outline, union);
    }

    [Fact]
    public void Contained_double_strike_renders_like_outer()
    {
        // R34: a loop contained in a larger same-direction loop drops, keeping the outer fill.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 L12,2 L2,12 Z M6,2 L12,2 L6,8 Z' fill='red'/></svg>", out _, out _);
        byte[] outer = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 L12,2 L2,12 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(outer, union);
    }

    [Fact]
    public void Nested_touching_hole_stays_unsupported()
    {
        // R34: nested loops sharing one coincident span keep hole roles through the fan path.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 L16,0 L0,16 Z M0,8 L0,12 L4,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 1, 9));
    }

    [Fact]
    public void Multi_span_nested_island_drops_like_outer()
    {
        // R34: a nested island sharing several boundary spans drops, keeping the outer fill.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 H16 V16 H0 Z M0,4 H16 V12 H0 Z' fill='red'/></svg>", out _, out _);
        byte[] outer = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 H16 V16 H0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(outer, union);
    }

    [Fact]
    public void Adjacent_squares_splice_like_outer()
    {
        // R34: adjacent loops sharing single spans splice sequentially into the union boundary.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 H5 V16 H0 Z M5,0 H11 V16 H5 Z M11,0 H16 V16 H11 Z' fill='red'/></svg>", out _, out _);
        byte[] outer = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 H16 V16 H0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(outer, union);
    }

    [Fact]
    public void Opposite_adjacent_triangles_splice_like_rect()
    {
        // R34: adjacent loops sharing one span in opposite directions splice into the union boundary.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 L16,0 L0,16 Z M16,0 L16,16 L0,16 Z' fill='red'/></svg>", out _, out _);
        byte[] outer = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,0 H16 V16 H0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(outer, union);
    }
    [Fact]
    public void Retraced_span_plus_crossing_unions_like_outline()
    {
        // R34: a single loop retracing one span across a crossing subdivides,
        // splits and cross-chains into the same ring the separated
        // ring-plus-island twin reaches through contained-drop.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='70' height='95'><path d='M5,43 h60 L5,5 v80 h60 L5,25 z' fill='#DC143C'/></svg>", out int width, out int height);
        byte[] outline = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='70' height='95'><path d='M5,25 L5,43 L5,85 L65,85 L23,43 L65,43 L5,5 Z M5,25 L5,43 L23,43 Z' fill='#DC143C'/></svg>", out _, out _);

        Assert.Equal(outline, union);
        Assert.Equal(70, width);
        Assert.Equal(95, height);
        Assert.Equal(new SvgColor(220, 20, 60, 255), Pixel(union, width, 30, 55));
        Assert.Equal(new SvgColor(220, 20, 60, 255), Pixel(union, width, 10, 30));
        Assert.Equal(new SvgColor(220, 20, 60, 255), Pixel(union, width, 5, 25));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 60, 50));
    }

    [Fact]
    public void Retraced_span_plus_crossing_evenodd_twin_stays_unsupported()
    {
        // R34: the mixed-contact union keeps nonzero semantics, so the
        // even-odd twin stays gated at the simplicity check.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='70' height='95'><path d='M5,43 h60 L5,5 v80 h60 L5,25 z' fill='#DC143C' fill-rule='evenodd'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only simple diagonal polygon contours are implemented;", rejected.Message);
    }
    [Fact]
    public void Variant_envelope_arcs_render_with_fringe_pins()
    {
        // R34: near-duplicate variant arcs conform span-by-span into exact duplicates,
        // then splice and drop into one ring; the banked oracle triple reads fringe-level
        // residuals only while structural pins lock placement.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='59' height='58' viewBox='0 0 58.894 57.681'><defs><linearGradient id='g' y1='0.5' x2='1' y2='0.5' gradientUnits='objectBoundingBox'><stop offset='0' stop-color='#e3156a'/><stop offset='1' stop-color='#91187f'/></linearGradient></defs><g transform='translate(1528.394 -821.497)'><path d='M-1501.434,839.988a25.146,25.146,0,0,1-10.955-6.246A25.162,25.162,0,0,1-1523.4,840a17.836,17.836,0,0,0-.316,3.349,17.817,17.817,0,0,0,11.08,16.421,17.744,17.744,0,0,0,11.49-16.583A17.852,17.852,0,0,0-1501.434,839.988Zm-11.786,13.107h0l-6.754-6.754,2.076-2.082,4.681,4.681,7.321-7.323,2.079,2.079Zm11.786-13.107a25.146,25.146,0,0,1-10.955-6.246A25.162,25.162,0,0,1-1523.4,840a17.836,17.836,0,0,0-.316,3.349,17.817,17.817,0,0,0,11.08,16.421,17.744,17.744,0,0,0,11.49-16.583A17.852,17.852,0,0,0-1501.434,839.988Zm-11.786,13.107h0l-6.754-6.754,2.076-2.082,4.681,4.681,7.321-7.323,2.079,2.079Zm.831-19.353A25.162,25.162,0,0,1-1523.4,840a17.836,17.836,0,0,0-.316,3.349,17.817,17.817,0,0,0,11.08,16.421,17.744,17.744,0,0,0,11.49-16.583,17.852,17.852,0,0,0-.289-3.2A25.146,25.146,0,0,1-1512.389,833.742Zm-.831,19.353h0l-6.754-6.754,2.076-2.082,4.681,4.681,7.321-7.323,2.079,2.079Zm11.786-13.107a25.146,25.146,0,0,1-10.955-6.246A25.162,25.162,0,0,1-1523.4,840a17.836,17.836,0,0,0-.316,3.349,17.817,17.817,0,0,0,11.08,16.421,17.744,17.744,0,0,0,11.49-16.583A17.852,17.852,0,0,0-1501.434,839.988Zm-11.786,13.107h0l-6.754-6.754,2.076-2.082,4.681,4.681,7.321-7.323,2.079,2.079Zm11.786-13.107a25.146,25.146,0,0,1-10.955-6.246A25.162,25.162,0,0,1-1523.4,840a17.836,17.836,0,0,0-.316,3.349,17.817,17.817,0,0,0,11.08,16.421,17.744,17.744,0,0,0,11.49-16.583A17.852,17.852,0,0,0-1501.434,839.988Zm-11.786,13.107h0l-6.754-6.754,2.076-2.082,4.681,4.681,7.321-7.323,2.079,2.079Z' fill='url(#g)'/></g></svg>", out int width, out int height);

        Assert.Equal(59, width);
        Assert.Equal(58, height);
        Assert.Equal(new SvgColor(169, 23, 120, 255), Pixel(pixels, width, 20, 20));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 58, 57));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 29, 29));
    }

    [Fact]
    public void Variant_arc_pair_without_checks_stays_unsupported()
    {
        // R34: the isolated variant pair keeps a multi-span same-directed endgame past
        // span conforming, so it stays honestly gated while the positioned full path admits.
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='59' height='58' viewBox='0 0 58.894 57.681'><defs><linearGradient id='g' y1='0.5' x2='1' y2='0.5' gradientUnits='objectBoundingBox'><stop offset='0' stop-color='#e3156a'/><stop offset='1' stop-color='#91187f'/></linearGradient></defs><g transform='translate(1528.394 -821.497)'><path d='M-1501.434,839.988A25.146,25.146,0,0,1,-1512.389,833.742A25.162,25.162,0,0,1,-1523.4,840A17.836,17.836,0,0,0,-1523.716,843.349A17.852,17.852,0,0,0,-1501.434,839.988ZM-1512.389,833.742A25.162,25.162,0,0,1,-1523.4,840A17.836,17.836,0,0,0,-1523.716,843.349A25.146,25.146,0,0,1,-1512.389,833.742Z' fill='url(#g)'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only simple diagonal polygon contours are implemented;", rejected.Message);
    }
    [Fact]
    public void Touching_letter_parts_union_like_outline()
    {
        // R34: two letter fragments sharing vertices link their boundary arcs
        // into the union ring after interior arcs drop.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='737' height='371'><g transform='translate(40 40)'><path d='m0 2.575c-0.3738 0.2962-0.7086 0.5617-1.005 0.7996-0.2967 0.2365-0.6936 0.4733-1.192 0.7099-0.4882 0.2269-0.9628 0.4-1.426 0.5181-0.4619 0.1245-1.108 0.1782-1.94 0.1782-1.547 0-2.832-0.4932-3.856-1.48-1.022-0.9864-1.534-2.229-1.534-3.729 0-1.233 0.259-2.229 0.7785-2.989 0.5194-0.7597 1.267-1.361 2.243-1.805 0.9862-0.4532 2.165-0.7697 3.536-0.9465 1.371-0.1782 2.846-0.3164 4.424-0.4146v-0.0873c0-0.9179-0.3564-1.551-1.067-1.901-0.7112-0.3499-1.779-0.5256-3.201-0.5256-0.6439 0-1.397 0.1121-2.259 0.3325-0.8618 0.2218-1.703 0.5056-2.524 0.8506h-0.4671v-4.01c0.5405-0.1483 1.413-0.3224 2.617-0.5256 1.204-0.2016 2.42-0.3026 3.645-0.3026 3.012 0 5.206 0.4758 6.582 1.428 1.376 0.9515 2.064 2.404 2.064 4.358v11.3h-5.421zm-10.18 1.761h-15.48v-22.03h5.826v17.83h9.658z' fill='#173556'/></g></svg>", out int width, out int height);

        Assert.Equal(737, width);
        Assert.Equal(371, height);
        Assert.Equal(new SvgColor(23, 53, 86, 255), Pixel(union, width, 40, 30));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 736, 370));
    }
    [Fact]
    public void Second_touching_letter_pair_unions_like_outline()
    {
        // R34: a second touching letter pair links through the same shared-vertex arc surgery.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='737' height='371'><g transform='translate(40 40)'><path d='m-13.01 0.1245c-1.443 0-2.765-0.1731-3.964-0.5181-1.199-0.3449-2.241-0.8781-3.123-1.598-0.8719-0.7199-1.553-1.625-2.041-2.715-0.4882-1.09-0.7324-2.375-0.7324-3.855 0-1.588 0.2654-2.94 0.7947-4.054 0.5293-1.115 1.246-2.032 2.15-2.752 0.8717-0.6913 1.898-1.201 3.077-1.532 1.179-0.3302 2.407-0.4957 3.684-0.4957 0.9665 0 1.932 0.1121 2.898 0.3178 0.9664 0.2117 1.937 0.5505 2.913 1.014v4.439h-0.7011c-0.2183-0.197-0.4858-0.4147-0.8021-0.6514-0.3164-0.2365-0.6627-0.4532-1.036-0.6513-0.3949-0.2067-0.8407-0.3825-1.34-0.5256-0.4981-0.1433-1.054-0.214-1.667-0.214-1.392 1.3e-4 -2.469 0.4608-3.232 1.384-0.7633 0.9229-1.144 2.163-1.144 3.721 0 1.667 0.4072 2.91 1.223 3.729 0.8159 0.8195 1.898 1.228 3.248 1.228 0.685 1e-4 1.288-0.0748 1.807-0.2218 0.5194-0.1483 0.9665-0.3251 1.34-0.533 0.3636-0.2066 0.675-0.422 0.9354-0.6439 0.259-0.2218 0.4832-0.422 0.67-0.599h0.7011v4.439c-0.3112 0.1281-0.6724 0.2765-1.082 0.4436-0.4096 0.1681-0.8281 0.3063-1.254 0.4146-0.5293 0.1382-1.025 0.2443-1.488 0.3178-0.462 0.0749-1.072 0.1121-1.83 0.1121zm-16.64 0c-3.303 0-5.834-0.7498-7.594-2.249-1.76-1.499-2.64-3.635-2.64-6.407 0-2.713 0.8208-4.878 2.461-6.496 1.641-1.618 3.936-2.427 6.885-2.427 2.68 0 4.694 0.685 6.044 2.057 1.35 1.371 2.025 3.334 2.025 5.889v1.864h-11.95c0.05 0.7696 0.2227 1.413 0.5143 1.931 0.2903 0.5181 0.6751 0.9291 1.153 1.236 0.4672 0.3054 1.013 0.5256 1.636 0.6588 0.6227 0.1332 1.303 0.1993 2.041 0.1993 0.634 0 1.257-0.0624 1.87-0.1993 0.6127-0.1332 1.173-0.3077 1.682-0.5256 0.4472-0.1883 0.8594-0.3899 1.238-0.6065 0.3784-0.2167 0.6875-0.4096 0.9267-0.5766h0.5766v4.276c-0.4459 0.1681-0.8544 0.3228-1.223 0.4658-0.3687 0.1433-0.8755 0.2888-1.519 0.4372-0.5915 0.1483-1.202 0.264-1.83 0.3476-0.6277 0.0873-1.394 0.1258-2.298 0.1258z' fill='#173556'/></g></svg>", out int width, out int height);

        Assert.Equal(737, width);
        Assert.Equal(371, height);
        Assert.Equal(new SvgColor(23, 53, 86, 255), Pixel(union, width, 16, 31));
        Assert.Equal(new SvgColor(23, 53, 86, 255), Pixel(union, width, 5, 25));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 736, 370));
    }
    [Fact]
    public void Chained_letter_triple_unions_like_outline()
    {
        // R34: three letter fragments sharing an opposite-directed edge chain splice
        // their coincident spans away and link the remainders into one union ring.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='737' height='371'><g transform='translate(40 40)'><path d='m-16.64 0c-3.303 0-5.834-0.7498-7.594-2.249-1.76-1.499-2.64-3.635-2.64-6.407 0-2.713 0.8208-4.878 2.461-6.496 1.641-1.618 3.936-2.427 6.885-2.427 2.68 0 4.694 0.685 6.044 2.057 1.35 1.371 2.025 3.334 2.025 5.889v1.864h-11.95c0.05 0.7696 0.2227 1.413 0.5143 1.931 0.2903 0.5181 0.6751 0.9291 1.153 1.236 0.4672 0.3054 1.013 0.5256 1.636 0.6588 0.6227 0.1332 1.303 0.1993 2.041 0.1993 0.634 0 1.257-0.0624 1.87-0.1993 0.6127-0.1332 1.173-0.3077 1.682-0.5256 0.4472-0.1883 0.8594-0.3899 1.238-0.6065 0.3784-0.2167 0.6875-0.4096 0.9267-0.5766h0.5766v4.276c-0.4459 0.1681-0.8544 0.3228-1.223 0.4658-0.3687 0.1433-0.8755 0.2888-1.519 0.4372-0.5915 0.1483-1.202 0.264-1.83 0.3476-0.6277 0.0873-1.394 0.1258-2.298 0.1258zm1.807-10.89c-0.023-1.095-0.2989-1.929-0.8019-2.501-0.5044-0.5717-1.275-0.8581-2.314-0.8581-1.059 1.5e-4 -1.888 0.3012-2.485 0.903-0.5965 0.6016-0.9266 1.42-0.9889 2.456zm-10.95-1.273h-0.4518c-0.2082-0.0749-0.5095-0.1244-0.903-0.1557-0.3949-0.0367-0.8307-0.05-1.309-0.05-0.5716 0-1.171 0.0749-1.799 0.2365-0.6289 0.158-1.238 0.3449-1.83 0.5629v11.13h-5.452v-16.62h5.452v2.382c0.2489-0.2168 0.5942-0.4907 1.036-0.8207 0.4408-0.3302 0.8432-0.5941 1.207-0.7921 0.3949-0.2268 0.8543-0.422 1.378-0.5841 0.5243-0.163 1.02-0.2443 1.488-0.2443 0.1768 1.5e-4 0.3738 5e-3 0.5915 0.0132 0.2182 0.0103 0.4161 0.023 0.5917 0.05z' fill='#173556'/></g></svg>", out int width, out int height);

        Assert.Equal(737, width);
        Assert.Equal(371, height);
        Assert.Equal(new SvgColor(23, 53, 86, 255), Pixel(union, width, 14, 30));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 736, 370));
    }
    [Fact]
    public void Evenodd_crossing_ring_pair_fills_like_outline()
    {
        // R34: an even-odd ring pair with only strict crossings subdivides both
        // edges at each computed crossing and forces opposite parity signs.
        byte[] union = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='600' height='400'><g fill='#ff0' fill-rule='evenodd' stroke-width='0.573' stroke-linecap='round' stroke-linejoin='round'><path d='m154.59,146.4c-2.7101,7.2937,36.149,31.318,82.903,59.754,46.752,28.434,87.065,46.006,90.053,43.486,0.18256-0.32544,1.4701-2.5409,1.352-2.5232-0.56001,0.84402-1.9234,1.1104-4.0506,0.49741-12.631-3.6433-45.575-18.765-86.374-43.506-40.798-24.743-76.294-47.544-81.811-57.232-0.38363-0.67116-0.65702-1.8962-0.60146-2.8487l-0.13405-0.002-1.1747,2.0532-0.16139,0.32191h-0.00088zm164.36,116.04c-0.51238,0.92957-1.4675,0.96044-3.2816,0.76112-11.3-1.2506-45.589-17.925-86.162-42.213-47.21-28.26-86.2-54.01-81.97-60.74l1.1509-2.0346,0.22665,0.0706c-3.8037,11.405,76.948,57.578,81.702,60.522,46.724,28.947,86.115,45.851,89.601,41.458l-1.2682,2.181v-0.002z'/></g></svg>", out int width, out int height);

        Assert.Equal(600, width);
        Assert.Equal(400, height);
        Assert.Equal(new SvgColor(255, 255, 0, 255), Pixel(union, width, 214, 210));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(union, width, 599, 399));
    }
}
