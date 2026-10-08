using System.Buffers.Binary;
using System.IO.Compression;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgMaskContentTests
{
    [Fact]
    public void Ring_mask_trims_group_to_annulus()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m'><circle cx='8' cy='8' r='4' fill='none' stroke='white' stroke-width='4'/></mask></defs><rect width='16' height='16' fill='red'/><g mask='url(#m)'><rect width='16' height='16' fill='blue'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 12));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 2, 2));
    }

    [Fact]
    public void Translated_group_keeps_spatial_mask()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m'><circle cx='8' cy='8' r='4' fill='none' stroke='white' stroke-width='4'/></mask></defs><rect width='16' height='16' fill='red'/><g mask='url(#m)'><g transform='translate(1,0)'><rect width='16' height='16' fill='blue'/></g></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(0, 0, 255, 255), Pixel(pixels, 16, 8, 12));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 2, 2));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect width='8' height='8' fill='white'/><circle cx='4' cy='4' r='2' fill='none' stroke='white'/></mask></defs><rect width='8' height='8' fill='red'/></svg>", "Only single-element mask content is implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><circle cx='4' cy='4' r='2' fill='none' stroke='red'/></mask></defs><rect width='8' height='8' fill='red'/></svg>", "Only grayscale luminance mask content is implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><circle cx='4' cy='4' r='2' fill='#888888' stroke='white'/></mask></defs><rect width='8' height='8' fill='red'/></svg>", "Only single-paint mask content is implemented.")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><circle cx='4' cy='4' r='2' fill='none' stroke='white'/></mask></defs><defs><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r' mask='url(#m)'/></svg>", "Spatial mask content on use instances is not implemented.")]
    public void Spatial_mask_gates_reject_honestly(string svg, string message)
    {
        SvgException rejected = Assert.Throws<SvgException>(() => RenderPixels(svg, out _, out _));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains(message, rejected.Message);
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
