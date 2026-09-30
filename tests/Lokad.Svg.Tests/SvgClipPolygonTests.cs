using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgClipPolygonTests
{
    [Fact]
    public void Triangle_clip_trims_stroked_path()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><clipPath id='t'><path d='M0,0 L16,8 L0,16 Z'/></clipPath></defs><path d='M0,8 L16,8' fill='none' stroke='red' stroke-width='4' clip-path='url(#t)'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 2, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 10, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 14, 6));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 2, 0));
    }

    [Fact]
    public void Translated_triangle_clip_trims_stroked_path()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><clipPath id='t'><path d='M0,0 L16,8 L0,16 Z' transform='translate(-4,0)'/></clipPath></defs><path d='M0,8 L16,8' fill='none' stroke='red' stroke-width='4' clip-path='url(#t)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 2, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 15, 6));
    }

    [Fact]
    public void Two_triangle_clip_unites_disjoint_halves()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H8 V4 H0 Z M0,4 H8 V8 H0 Z'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 6));
    }

    [Fact]
    public void Overlapping_clips_paint_opaque_nonzero_union()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H8 V4 H0 Z M0,2 H8 V6 H0 Z'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 5));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 6, 6));
    }

    [Fact]
    public void Evenodd_disjoint_clips_keep_holes()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><path d='M0,0 L10,0 L0,10 Z M0,32 L0,22 L10,32 Z'/></clipPath></defs><path d='M0,0 L32,0 L0,32 Z M12,12 L18,12 L12,18 Z' fill='red' fill-rule='evenodd' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 2, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 2, 28));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 32, 14, 14));
    }

    [Fact]
    public void Rect_path_clip_matches_rect_element_clip()
    {
        byte[] element = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='2' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] path = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M2,0 H6 V8 H2 Z'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(element, path);
    }

    [Fact]
    public void Triangle_clip_keeps_viewbox_mapping()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32' viewBox='0 0 16 16'><defs><clipPath id='t'><path d='M0,0 L16,8 L0,16 Z'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='red' clip-path='url(#t)'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='t'><path d='M0,0 L32,16 L0,32 Z'/></clipPath></defs><polygon points='0,0 32,0 0,32' fill='red' clip-path='url(#t)'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Unreferenced_multi_contour_clip_parses()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H4 V4 H0 Z M5,5 H8 V8 H5 Z'/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
    }
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 Q4,4 8,0 Z'/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", "Only straight clip paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H8 V8 H4 V4 H0 Z'/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", "Only convex clip paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d=''/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", "Degenerate clip paths are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 L8,0 L16,0 Z'/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", "Degenerate clip paths are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c' clipPathUnits='objectBoundingBox'><path d='M0,0 H1 V1 H0 Z'/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", "Only userSpaceOnUse clip-path paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='1' y='1' width='2' height='2'/><path d='M0,0 H8 V8 H0 Z'/></clipPath></defs><rect width='8' height='8' fill='red'/></svg>", "Only single-rectangle clip paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='t'><path d='M0,0 L8,4 L0,8 Z'/></clipPath></defs><g clip-path='url(#t)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", "Polygon clip-paths on groups are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='t'><path d='M0,0 L8,4 L0,8 Z'/></clipPath></defs><g transform='translate(1,0)'><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#t)'/></g></svg>", "Polygon clip-paths with transformed content are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='t'><path d='M0,0 L8,4 L0,8 Z'/></clipPath></defs><g id='g'><rect width='2' height='2' fill='red'/></g><use href='#g'/><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#t)'/></svg>", "Polygon clip-paths with use instances are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='t'><path d='M0,0 L8,4 L0,8 Z'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#t)'/></svg>", "Polygon clip-paths on this geometry are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H8 V4 H0 Z M0,0 H8 V8 H4 V4 H0 Z'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", "Only convex clip paths are implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H8 V4 H0 Z M1,1 Z'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", "Degenerate clip paths are not implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><path d='M0,0 H8 V4 H0 Z M0,2 H8 V6 H0 Z'/></clipPath></defs><path d='M0,0 L8,0 L0,8 Z M2,2 L4,2 L2,4 Z' fill='red' fill-rule='evenodd' clip-path='url(#c)'/></svg>", "Overlapping multi-contour clip paths are not implemented.")]
    public void Polygon_clip_gates_reject_honestly(string svg, string message)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => RenderPixels(svg, out _, out _));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains(message, rejected.Message);
    }

    [Fact]
    public void Shape_use_leaves_polygon_clip_intact()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='t'><path d='M0,0 L8,4 L0,8 Z'/></clipPath><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r'/><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#t)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 5, 1));
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
}
