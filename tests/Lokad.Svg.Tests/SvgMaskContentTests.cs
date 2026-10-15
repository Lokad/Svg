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

    [Fact]
    public void White_mask_children_preserve_opaque_target_pixels()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><rect width='8' height='8' fill='white'/><circle cx='4' cy='4' r='2' fill='none' stroke='white'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>", out int width, out int height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, x, y));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><circle cx='4' cy='4' r='2' fill='none' stroke='red'/></mask></defs><rect width='8' height='8' fill='red'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><circle cx='4' cy='4' r='2' fill='#888888' stroke='white'/></mask></defs><rect width='8' height='8' fill='red'/></svg>")]
    public void Original_mask_paint_boundaries_admit_and_preserve_unmasked_siblings(string svg)
    {
        byte[] pixels = RenderPixels(svg, out int width, out int height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, x, y));
    }

    [Theory]
    [InlineData("fill='none' stroke='red' stroke-width='2'", 54, 0)]
    [InlineData("fill='#808080' stroke='white' stroke-width='2'", 255, 128)]
    public void Colored_stroke_and_independent_fill_use_composited_luminance(string paint, byte ringAlpha, byte centerAlpha)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><mask id='m'><circle cx='8' cy='8' r='4' " + paint
            + "/></mask></defs><rect width='16' height='16' fill='red' mask='url(#m)'/></svg>";
        byte[] pixels = RenderPixels(svg, out _, out _);
        Assert.Equal(ringAlpha, Pixel(pixels, 16, 8, 11).Alpha);
        Assert.Equal(centerAlpha, Pixel(pixels, 16, 8, 8).Alpha);
        Assert.Equal(0, Pixel(pixels, 16, 0, 0).Alpha);
    }

    [Fact]
    public void Partial_spatial_use_mask_matches_direct_leaf_coverage()
    {
        const string input = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><defs><mask id='m'><circle cx='4' cy='4' r='2' fill='none' stroke='white'/></mask></defs><defs><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r' mask='url(#m)'/></svg>";
        string direct = input.Replace("<use href='#r'", "<rect width='2' height='2' fill='red'", StringComparison.Ordinal);
        Assert.Equal(RenderPixels(direct, out _, out _), RenderPixels(input, out _, out _));
    }

    [Theory]
    [InlineData("maskUnits='userSpaceOnUse' x='2' y='2' width='4' height='4'", "<rect width='24' height='16' fill='white'/><rect width='0' height='0'/>", "<rect x='2' y='2' width='8' height='4' fill='red' transform='matrix(0 1 -1 0 20 0)' mask='url(#m)'/>")]
    [InlineData("maskContentUnits='objectBoundingBox'", "<rect width='50%' height='100%' fill='white'/>", "<g transform='matrix(0 1 -1 0 20 0)' mask='url(#m)'><rect x='2' y='2' width='8' height='4' fill='red'/></g>")]
    [InlineData("maskContentUnits='objectBoundingBox'", "<rect width='50%' height='100%' fill='white'/>", "<use href='#r' x='2' y='2' transform='matrix(0 1 -1 0 20 0)' mask='url(#m)'/>")]
    public void Affine_masks_use_local_bounds_and_regions_without_clipping_siblings(string units, string content, string target)
    {
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><defs><mask id='m' " + units + ">" + content
            + "</mask><rect id='r' width='8' height='4' fill='red'/></defs>" + target + "<rect x='22' y='14' width='2' height='2' fill='blue'/></svg>";
        byte[] pixels = RenderPixels(svg, out int width, out int height);
        Assert.Equal(24, width);
        Assert.Equal(16, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            SvgColor expected = x >= 22 && y >= 14 ? new(0, 0, 255, 255)
                : x >= 14 && x < 18 && y >= 2 && y < 6 ? new(255, 0, 0, 255) : SvgColor.Transparent;
            Assert.Equal(expected, Pixel(pixels, width, x, y));
        }
    }

    [Fact]
    public void User_space_region_percentages_use_viewport_extents()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='12'><defs><mask id='m' maskUnits='userSpaceOnUse' x='25%' y='0' width='50%' height='100%'><rect width='20' height='12' fill='white'/></mask></defs><rect width='20' height='12' fill='red' mask='url(#m)'/></svg>";
        byte[] pixels = RenderPixels(svg, out _, out _);
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 20; x++) Assert.Equal(x >= 5 && x < 15 ? new SvgColor(255, 0, 0, 255) : SvgColor.Transparent, Pixel(pixels, 20, x, y));
    }

    [Fact]
    public void Implicit_root_viewport_supplies_user_space_region_defaults()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg'><defs><mask id='m' maskUnits='userSpaceOnUse'><rect width='8' height='8' fill='white'/></mask></defs><rect width='8' height='8' fill='red' mask='url(#m)'/></svg>";
        byte[] pixels = RenderPixels(svg, out int width, out int height);
        Assert.Equal(300, width);
        Assert.Equal(150, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 7, 7));
        Assert.Equal(SvgColor.Transparent, Pixel(pixels, width, 8, 7));
    }

    [Fact]
    public void Partial_uniform_group_mask_replays_in_scaled_viewport_and_restores_siblings()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'><defs><mask id='m'><rect width='2' height='4' fill='white'/></mask></defs><svg x='4' y='2' width='16' height='12' viewBox='0 0 8 6'><g mask='url(#m)'><rect width='8' height='6' fill='red'/></g></svg><rect x='22' y='14' width='2' height='2' fill='blue'/></svg>";
        byte[] pixels = RenderPixels(svg, out int width, out int height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            SvgColor expected = x >= 22 && y >= 14 ? new(0, 0, 255, 255)
                : x >= 4 && x < 8 && y >= 2 && y < 10 ? new(255, 0, 0, 255) : SvgColor.Transparent;
            Assert.Equal(expected, Pixel(pixels, width, x, y));
        }
    }

    [Fact]
    public void Bounding_box_region_uses_full_object_before_viewport_clipping()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='16'><defs><mask id='m' x='0' y='0' width='.5' height='1'><rect width='20' height='16' fill='white'/></mask></defs><svg x='4' y='2' width='8' height='8' viewBox='0 0 4 4'><rect width='8' height='8' fill='red' mask='url(#m)'/></svg></svg>";
        byte[] pixels = RenderPixels(svg, out _, out _);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 20, 10, 6));
        Assert.Equal(SvgColor.Transparent, Pixel(pixels, 20, 12, 6));
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
