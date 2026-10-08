using System.Buffers.Binary;
using System.IO.Compression;

namespace Lokad.Svg.Tests;

public sealed class SvgCurveFillTests
{
    [Fact]
    public void Quadratic_arch_fills_below_curve()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,-8 8,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 1, 7));
        Assert.Equal(Pixel(pixels, 8, 1, 7), Pixel(pixels, 8, 6, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 0));
    }

    [Fact]
    public void Implicit_close_matches_explicit_close()
    {
        byte[] closed = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,-8 8,8 Z' fill='red'/></svg>", out _, out _);
        byte[] open = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,-8 8,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(closed, open);
    }

    [Fact]
    public void Cubic_smooth_command_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 S4,0 8,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Twin_arc_full_circle_renders()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M0,8 A8,8 0 1 1 16,8 A8,8 0 1 1 0,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 16, 8, 12));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 16, 0, 0));
    }

    [Fact]
    public void Arc_under_anisotropic_transform_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8'><g transform='scale(2,1)'><path d='M0,4 A4,4 0 1 1 8,4 A4,4 0 1 1 0,4 Z' fill='red'/></g></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
    }

    [Fact]
    public void Integral_decimal_cubic_matches_integer_spelling()
    {
        byte[] integer = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 C3,0 6,0 8,8 Z' fill='red'/></svg>", out _, out _);
        byte[] decimalSpelling = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 C3.0,0.0 6.0,0.0 8.0,8.0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(integer, decimalSpelling);
    }

    [Fact]
    public void Integral_exponent_quadratic_matches_integer_spelling()
    {
        byte[] integer = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4,0 8,8 Z' fill='red'/></svg>", out _, out _);
        byte[] exponent = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 Q4e0,0e0 8e0,8e0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(integer, exponent);
    }

    [Fact]
    public void Fractional_quadratic_side_control_fills_sliver()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q4.5,0 8,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 0));
    }

    [Fact]
    public void Exponent_quadratic_outside_control_fills_triangle()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q10,0 1e-1,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 2, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 7, 7));
    }

    [Fact]
    public void Fractional_cubic_arch_fills_below_curve()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,8 C3.5,0 6,0 8,8 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 4, 7));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 0));
    }

    [Fact]
    public void Curve_under_nonuniform_output_mapping_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8' viewBox='0 0 8 8' preserveAspectRatio='none'><path d='M0,8 Q4,0 8,8 Z' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("non-uniform viewBox or output scaling", rejected.Message);
    }

    [Fact]
    public void Curve_under_skew_transform_renders()
    {
        // Row 5 skew admission: arc-free curve fills map through the general device transform, so a skewed quad renders instead of denying on uniformity.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g transform='skewX(15)'><path d='M0,8 Q4,0 8,8 Z' fill='red'/></g></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(16, height);
        Assert.NotEqual(0, AlphaSum(pixels));
    }

    [Fact]
    public void Straight_triangle_renders_under_nonuniform_output_mapping()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8' viewBox='0 0 8 8' preserveAspectRatio='none'><path d='M0,0 L8,0 L0,8 Z' fill='red'/></svg>", out int width, out int height);

        Assert.Equal(16, width);
        Assert.Equal(8, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 0, 0));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 8, 0));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, width, 15, 7));
    }

    [Fact]
    public void Degenerate_curve_fill_paints_nothing()
    {
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 C0,0 0,0 0,0 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(0, AlphaSum(pixels));
    }

    [Fact]
    public void Fractional_open_dash_matches_direct_dash()
    {
        byte[] fractional = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,4 L8,4' fill='none' stroke='red' stroke-width='2' stroke-dasharray='2 2'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,4 L8,4' fill='none' stroke='red' stroke-width='2' style='stroke-dasharray: 2 2'/></svg>", out _, out _);

        Assert.Equal(direct, fractional);
    }

    [Fact]
    public void Mixed_curve_degenerate_drops_line_untouched()
    {
        byte[] plain = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q4,0 8,8 Z' fill='red'/></svg>", out _, out _);
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q4,0 8,8 Z M0.5,0.5 L1.5,0.5' fill='red'/></svg>", out _, out _);

        Assert.Equal(plain, pixels);
        Assert.True(AlphaSum(pixels) > 0);
    }

    [Fact]
    public void Curve_command_count_exceeds_small_limit()
    {
        SvgRenderLimits limits = new(268435456, 16777216, 16384, 16777216, 134217728, 100000, 3, 100000000);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits, null);
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 Q1,0 2,2 Q3,3 4,4 Z' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);
    }

    [Fact]
    public void Flattened_expansion_beyond_capacity_stays_bounded()
    {
        SvgRenderLimits limits = new(268435456, 16777216, 16384, 16777216, 134217728, 100000, 3000, 100000000);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits, null);
        string d = string.Join(" ", Enumerable.Repeat("M0,0 C4000,0 4000,4000 0,4000", 1000));
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='" + d + "' fill='red'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, rejected.ErrorCode);
    }

    [Fact]
    public void Scaled_circle_fill_matches_direct_circle()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><circle cx='4' cy='4' r='3' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><circle cx='64' cy='64' r='48' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Scaled_arc_path_matches_direct_arc_path()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><path d='M1,4 A3,3 0 1 1 7,4 A3,3 0 1 1 1,4 Z' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><path d='M16,64 A48,48 0 1 1 112,64 A48,48 0 1 1 16,64 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Scaled_quadratic_fill_matches_direct_quadratic()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><path d='M0,8 Q4,0 8,8 Z' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><path d='M0,128 Q64,0 128,128 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Scaled_cubic_fill_matches_direct_cubic()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><path d='M0,8 C3,0 6,0 8,8 Z' fill='red'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><path d='M0,128 C48,0 96,0 128,128 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void OutputScale_viewBox_curve_matches_direct_rendering()
    {
        SvgRenderOptions magnified = new(null, 2, SvgColor.Transparent, SvgRenderLimits.Default, null);
        byte[] scaled = DecodeRgba(SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><path d='M0,8 Q4,0 8,8 Z' fill='red'/></svg>"), magnified, CancellationToken.None), out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><path d='M0,256 Q128,0 256,256 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }

    [Fact]
    public void Nested_viewport_curve_matches_direct_rendering()
    {
        byte[] nested = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><svg x='32' y='32' width='64' height='64' viewBox='0 0 8 8'><path d='M0,8 Q4,0 8,8 Z' fill='red'/></svg></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><path d='M32,96 Q64,32 96,96 Z' fill='red'/></svg>", out _, out _);

        Assert.Equal(direct, nested);
    }
    [Fact]
    public void Scaled_circle_stroke_matches_direct_stroke()
    {
        byte[] scaled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><circle cx='4' cy='4' r='3' fill='none' stroke='red' stroke-width='1'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><circle cx='64' cy='64' r='48' fill='none' stroke='red' stroke-width='16'/></svg>", out _, out _);

        Assert.Equal(direct, scaled);
    }
    [Fact]
    public void Gradient_circle_fill_under_nonuniform_output_mapping_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 32 64' preserveAspectRatio='none'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><circle cx='16' cy='32' r='12' fill='url(#g)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("non-uniform viewBox or output scaling", rejected.Message);
    }

    [Fact]
    public void Radial_circle_fill_under_nonuniform_output_mapping_stays_unsupported()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' viewBox='0 0 32 64' preserveAspectRatio='none'><defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></radialGradient></defs><circle cx='16' cy='32' r='12' fill='url(#g)'/></svg>");

        SvgException rejected = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, rejected.ErrorCode);
        Assert.Contains("non-uniform viewBox or output scaling", rejected.Message);
    }

    [Fact]
    public void Curved_bowtie_keeps_authored_crossing_coverage()
    {
        // Fresh pinned Chrome 153.0.8010.52 captures record alpha 130 at (3,3)
        // for these authored cubics. Splitting their flattened crossing into
        // separate loops loses that native curve event. The straight polygon
        // keeps the browser's alpha 127 through its original directed edges.
        byte[] curved = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 C2,2 6,6 8,8 L8,0 C6,2 2,6 0,8 Z' fill='red'/></svg>", out _, out _);
        byte[] straight = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><polygon points='0,0 8,8 8,0 0,8' fill='red'/></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(curved, 8, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(curved, 8, 6, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(curved, 8, 4, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 130), Pixel(curved, 8, 3, 3));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(straight, 8, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(straight, 8, 6, 2));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(straight, 8, 4, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 127), Pixel(straight, 8, 3, 3));
    }

    [Fact]
    public void Fractional_self_intersecting_path_paints_union()
    {
        // Authored self-crossings retain their directed winding through clipping.
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0,0 L8.5,8 L8,0 L0,8 Z' fill='red'/></svg>", out int width, out int height);

        Assert.Equal(8, width);
        Assert.Equal(8, height);
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 1, 2));
        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, width, 6, 2));
        // Fresh pinned-browser captures record 61 at this clipped crossing.
        Assert.Equal(new SvgColor(255, 0, 0, 61), Pixel(pixels, width, 4, 4));
    }

    [Fact]
    public void Fractional_path_inline_width_matches_direct_attribute()
    {
        // R36: inline stroke widths flow through the same cascade as attributes,
        // advancing the check-mark bundle past the inline-width gate (inv-074).
        byte[] styled = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0 L8,0 L8,8 Z' fill='red' stroke='blue' style='stroke-width:2'/></svg>", out _, out _);
        byte[] direct = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><path d='M0.5,0 L8,0 L8,8 Z' fill='red' stroke='blue' stroke-width='2'/></svg>", out _, out _);

        Assert.Equal(direct, styled);
    }

    [Fact]
    public void Rotated_fractional_triangle_paints_union()
    {
        // R37/row 5: rotation maps vertices through the general affine carrier while
        // the diagonal fill path is unchanged. Staged oracle runs rotri-o1..o2 agree
        // 2/2 byte-identical SHA-256 d406fa809e3083c7fea8a3cd7144512373edf447a1b56852732e4b138dc9aba3;
        // the only residual is edge fringe (20 px at max channel error 6).
        byte[] pixels = RenderPixels("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g transform='rotate(30)'><path d='M0.5,0 L8,0 L8,8 Z' fill='red'/></g></svg>", out _, out _);

        Assert.Equal(new SvgColor(255, 0, 0, 255), Pixel(pixels, 8, 3, 3));
        Assert.Equal(new SvgColor(0, 0, 0, 0), Pixel(pixels, 8, 0, 7));
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
