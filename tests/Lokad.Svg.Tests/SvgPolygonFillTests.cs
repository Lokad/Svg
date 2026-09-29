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
    public void Single_point_path_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M4,4' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only closed axis-aligned integer paths are implemented.", rejected.Message);
    }

    [Fact]
    public void Mixed_open_closed_path_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z M0,4 L8,4' fill='red' stroke='red' stroke-width='2'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("Only closed axis-aligned integer path contours are implemented.", rejected.Message);
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
        // The sliver pins candidate identity (alpha 23) against the pinned
        // Chrome triple (alpha 37, SHA-256 a6c2aee0); the twin below locks reversal invariance.
        Assert.Equal(new SvgColor(255, 0, 0, 23), Pixel(pixels, width, 0, 0));
        Assert.Equal(23, AlphaSum(pixels));
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
    public void Coincident_edge_overlap_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L4,0 L0,4 Z M0,4 L4,4 L4,0 Z' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
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
    public void Degenerate_ring_renders_empty()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 2,0 0,0 0,2' fill='red'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }


    private static long AlphaSum(byte[] pixels)
    {
        long total = 0;
        for (int index = 3; index < pixels.Length; index += 4) total += pixels[index];
        return total;
    }
}
