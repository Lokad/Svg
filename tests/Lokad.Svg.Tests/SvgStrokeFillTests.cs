using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Tests;

public sealed class SvgStrokeFillTests
{
    [Fact]
    public void Open_polyline_stroke_makes_butt_band()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 4,0 8,0' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 2));
        Assert.Equal(2040, AlphaSum(pixels));
    }

    [Fact]
    public void Closed_square_stroke_makes_ring()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 4));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 1, 1));
        Assert.Equal(7140, AlphaSum(pixels));
    }

    [Fact]
    public void Open_path_stroke_miters()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L8,4' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 7, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 3));
        Assert.Equal(2805, AlphaSum(pixels));
    }

    [Fact]
    public void Closed_triangle_stroke_rings_diagonal()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 0, 4));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 1, 1));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 4, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
    }

    [Fact]
    public void Fill_and_stroke_combine_in_paint_order()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 8, 7, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 4));
    }

    [Fact]
    public void Open_polyline_and_closed_polygon_stroke_differently()
    {
        byte[] open = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polyline points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] closed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);

        Assert.NotEqual(open, closed);
    }

    [Fact]
    public void Fill_rule_does_not_change_stroke_outlines()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></svg>", out _, out _);
        byte[] evenOdd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(plain, evenOdd);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='3'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='0'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2.5'/></svg>")]
    public void Non_even_stroke_widths_stay_unsupported(string svg)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linecap='round'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-linejoin='round'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2,2'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='url(#g)' stroke-width='2'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2' stroke-miterlimit='0.5'/></svg>")]
    public void Non_admitted_stroke_styles_stay_unsupported(string svg)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Stroke_under_anisotropic_transform_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><g transform='scale(2,1)'><polygon points='0,0 8,0 8,8 0,8' fill='none' stroke='red' stroke-width='2'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Stroke_on_circle_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='2' fill='red' stroke='blue' stroke-width='2'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
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