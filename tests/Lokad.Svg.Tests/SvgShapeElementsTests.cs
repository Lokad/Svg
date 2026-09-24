using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Tests;

public sealed class SvgShapeElementsTests
{
    [Fact]
    public void Circle_renders_analytic_disc()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 20));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 16, 32));
        Assert.Equal(Pixel(pixels, 64, 16, 32), Pixel(pixels, 64, 48, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 0, 0));
    }

    [Fact]
    public void Ellipse_renders_analytic_disc()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><ellipse cx='32' cy='32' rx='24' ry='14' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 24));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 16, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 0, 0));
    }

    [Fact]
    public void Rounded_rectangle_cuts_corners()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='8' y='8' width='48' height='48' rx='8' ry='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 32));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 56, 56));
    }

    [Fact]
    public void Corner_radius_defaults_to_sibling()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='8' y='8' width='48' height='16' rx='8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 32, 10));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 8, 8));
    }

    [Fact]
    public void Zero_radius_draws_nothing()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='0' fill='red'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='-1' fill='red'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><ellipse cx='4' cy='4' rx='-1' ry='2' fill='red'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='1' y='1' width='6' height='6' rx='-1' fill='red'/></svg>")]
    public void Negative_shape_radii_are_malformed(string svg)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, rejected.ErrorCode);
    }

    [Fact]
    public void Quarter_center_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4.5' cy='3.5' r='2' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 3));
    }

    [Fact]
    public void Fractional_beyond_quarter_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4.1' cy='4' r='2' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Gradient_fill_on_circle_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><circle cx='4' cy='4' r='2' fill='url(#g)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Stroke_on_circle_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><circle cx='4' cy='4' r='2' fill='red' stroke='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Transformed_circle_moves_center()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><g transform='translate(8,8)'><circle cx='8' cy='8' r='8' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 32, 16, 16));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 32, 8, 8));
    }

    [Fact]
    public void Clip_path_intersects_circle_bounds()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><clipPath id='c'><rect x='32' y='0' width='32' height='64'/></clipPath></defs><circle cx='32' cy='32' r='20' fill='red' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 64, 48, 32));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 64, 16, 32));
    }

    [Fact]
    public void Even_odd_on_circle_matches_default()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red'/></svg>", out _, out _);
        byte[] evenOdd = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><circle cx='32' cy='32' r='20' fill='red' fill-rule='evenodd'/></svg>", out _, out _);

        Assert.Equal(plain, evenOdd);
    }

    [Fact]
    public void Rounded_rectangle_in_definitions_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><rect id='r' x='1' y='1' width='4' height='4' rx='2'/></defs><use href='#r'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Circle_content_in_clip_path_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><circle cx='4' cy='4' r='2'/></clipPath></defs><rect width='8' height='8' fill='red' clip-path='url(#c)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }


    [Fact]
    public void Gray_masked_group_matches_half_opacity_group()
    {
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", out _, out _);
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g opacity='0.5'><polygon points='0,0 8,0 0,8' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(faded, masked);
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