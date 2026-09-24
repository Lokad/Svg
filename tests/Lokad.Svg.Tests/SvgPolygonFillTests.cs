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
        SvgPolygonFill fill = SvgPolygonFill.Create(points, ends, null, new SvgColor(255, 0, 0, 255), true, false, null, budget);

        SvgGeometry geometry = SvgGeometry.FromPolygonFill(fill);

        Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
        Assert.Same(points, geometry.Polygon.Points);
        Assert.Same(ends, geometry.Polygon.ContourEnds);
        Assert.Null(geometry.Polygon.Signs);
        Assert.Equal(new SvgColor(255, 0, 0, 255), geometry.Polygon.Color);
        Assert.True(geometry.Polygon.HasFill);
        Assert.False(geometry.Polygon.IsEvenOdd);
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
    public void Nested_same_wound_nonzero_triangle_fills_outer()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z M2,1 L4,1 L2,3 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(8164, AlphaSum(pixels));
    }

    [Fact]
    public void Self_intersecting_diagonal_polygon_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Coincident_spike_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 4,4 0,8' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
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
    public void Touching_hole_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,8 L0,8 Z M0,4 L4,2 L4,6 Z' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
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

    private static long AlphaSum(byte[] pixels)
    {
        long total = 0;
        for (int index = 3; index < pixels.Length; index += 4) total += pixels[index];
        return total;
    }
}