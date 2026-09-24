using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Tests;

public sealed class SvgUseInstanceTests
{
    [Fact]
    public void Direct_use_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Use_offset_matches_shifted_rendering()
    {
        byte[] shifted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='2,1 6,1 2,5' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 4,0 0,4' fill='red'/></defs><use href='#p' x='2' y='1'/></svg>", out _, out _);

        Assert.Equal(shifted, instanced);
    }

    [Fact]
    public void Use_scale_matches_scaled_rendering()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 4,0 0,4' fill='red'/></defs><use href='#p' transform='scale(2)'/></svg>", out _, out _);

        Assert.Equal(scaled, instanced);
    }

    [Fact]
    public void Forward_reference_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Orthogonal_bands_instance_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Stroked_definition_instances_both_geometries()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#p'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Use_opacity_matches_fill_opacity()
    {
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' fill-opacity='0.5'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(faded, instanced);
    }

    [Fact]
    public void Use_clip_matches_direct_clip()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><polygon points='0,0 8,0 0,8' fill='red' clip-path='url(#c)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><clipPath id='c'><rect x='4' y='0' width='4' height='8'/></clipPath></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' clip-path='url(#c)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Circle_definition_instances()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><circle cx='8' cy='8' r='6' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><circle id='c' cx='8' cy='8' r='6' fill='red'/></defs><use href='#c'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Forward_multi_geometry_use_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs><use href='#p'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><defs><polygon id='p' points='0,0 8,0 8,8 0,8' fill='red' stroke='blue' stroke-width='2'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Forward_bands_use_preserves_following_content_order()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></defs><use href='#p'/><rect x='4' y='0' width='4' height='8' fill='blue'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#p'/><rect x='4' y='0' width='4' height='8' fill='blue'/><defs><polygon id='p' points='0,0 8,0 8,4 4,4 4,8 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void ViewBox_less_symbol_polygon_instances_directly()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_polygon_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Scaled_symbol_polygon_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 0,4' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Offset_symbol_polygon_matches_shifted_rendering()
    {
        byte[] shifted = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='2,1 6,1 2,5' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 0,4' fill='red'/></symbol></defs><use href='#s' x='2' y='1' width='4' height='4'/></svg>", out _, out _);

        Assert.Equal(shifted, instanced);
    }

    [Fact]
    public void Symbol_polygon_viewBox_origin_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><polygon points='0,0 4,0 0,4' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><symbol id='s' viewBox='4 0 4 4'><polygon points='4,0 8,0 4,4' fill='red'/></symbol></defs><use href='#s' width='4' height='4'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_polygon_opacity_matches_fill_opacity()
    {
        byte[] faded = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red' fill-opacity='0.5'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8' opacity='0.5'/></svg>", out _, out _);

        Assert.Equal(faded, instanced);
    }

    [Fact]
    public void Forward_symbol_polygon_matches_backward_reference()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><use href='#s' width='8' height='8'/><defs><symbol id='s' viewBox='0 0 8 8'><polygon points='0,0 8,0 0,8' fill='red'/></symbol></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
    }

    [Fact]
    public void Symbol_path_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><path d='M0,0 L4,0 L0,4 Z' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Symbol_orthogonal_band_matches_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 8,8 0,8' fill='red'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><symbol id='s' viewBox='0 0 4 4'><polygon points='0,0 4,0 4,4 0,4' fill='red'/></symbol></defs><use href='#s' width='8' height='8'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Non_uniform_symbol_polygon_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='4'><defs><symbol id='s' viewBox='0 0 4 4' preserveAspectRatio='none'><polygon points='0,0 4,0 0,4' fill='red'/></symbol></defs><use href='#s' width='8' height='4'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Fill_override_on_polygon_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' fill='blue'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void White_masked_polygon_matches_unmasked_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='red'/></svg>", out _, out _);
        byte[] masked = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#ffffff'/></mask></defs><polygon points='0,0 8,0 0,8' fill='red' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, masked);
    }

    [Fact]
    public void Masked_polygon_use_matches_masked_direct_rendering()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><polygon points='0,0 8,0 0,8' fill='red' mask='url(#m)'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><use href='#p' mask='url(#m)'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Fill_override_on_unstyled_polygon_matches_direct()
    {
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,0 0,8' fill='blue'/></svg>", out _, out _);
        byte[] instanced = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><polygon id='p' points='0,0 8,0 0,8'/></defs><use href='#p' fill='blue'/></svg>", out _, out _);

        Assert.Equal(direct, instanced);
    }

    [Fact]
    public void Forward_polygon_use_inside_masked_group_matches_backward()
    {
        byte[] backward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs><g mask='url(#m)'><use href='#p'/></g></svg>", out _, out _);
        byte[] forward = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect x='0' y='0' width='8' height='8' fill='#808080'/></mask></defs><g mask='url(#m)'><use href='#p'/></g><defs><polygon id='p' points='0,0 8,0 0,8' fill='red'/></defs></svg>", out _, out _);

        Assert.Equal(backward, forward);
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
}