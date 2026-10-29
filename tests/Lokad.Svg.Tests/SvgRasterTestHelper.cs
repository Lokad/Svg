using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

/// <summary>Separates geometry/paint pixel checks from the explicit PNG paths retained by those tests.</summary>
internal static class SvgRasterTestHelper
{
    internal static byte[] RenderPixels(string svg, out int width, out int height)
        => RenderPixels(TextEncoding.UTF8.GetBytes(svg), out width, out height);

    internal static byte[] RenderPixels(ReadOnlyMemory<byte> svg, out int width, out int height)
        => RenderPixels(svg, SvgRenderOptions.Default, out width, out height);

    internal static byte[] RenderPixels(ReadOnlyMemory<byte> svg, SvgRenderOptions options, out int width, out int height)
    {
        using SvgConverter.RasterizedConversion capture = SvgConverter.PrepareRasterization(
            svg, options, CancellationToken.None);
        width = capture.Surface.Width;
        height = capture.Surface.Height;
        // Tests own this copy after the scene/surface owner is released. Match
        // encoded PNG's zero-alpha RGB rule without changing other color lanes.
        byte[] pixels = capture.Surface.Pixels.ToArray();
        for (int offset = 0; offset < pixels.Length; offset += 4)
            if (pixels[offset + 3] == 0) pixels.AsSpan(offset, 3).Clear();
        return pixels;
    }

    internal static void AssertPixelsEqual(string control, string input, SvgRenderOptions options)
        => AssertPixelsEqual(TextEncoding.UTF8.GetBytes(control), TextEncoding.UTF8.GetBytes(input), options);

    internal static void AssertPixelsEqual(ReadOnlyMemory<byte> control, ReadOnlyMemory<byte> input, SvgRenderOptions options)
    {
        byte[] expected = RenderPixels(control, options, out int expectedWidth, out int expectedHeight);
        byte[] actual = RenderPixels(input, options, out int actualWidth, out int actualHeight);
        Assert.Equal((expectedWidth, expectedHeight), (actualWidth, actualHeight));
        Assert.Equal(expected, actual);
    }

    internal static void AssertPngEqual(string control, string input, SvgRenderOptions options)
        => AssertPngEqual(TextEncoding.UTF8.GetBytes(control), TextEncoding.UTF8.GetBytes(input), options);

    internal static void AssertPngEqual(ReadOnlyMemory<byte> control, ReadOnlyMemory<byte> input, SvgRenderOptions options)
        => Assert.Equal(SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());

    internal static byte[] DecodeRgba(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        RgbaImage image = PngRgbaDecoder.Decode(png, int.MaxValue);
        width = image.Width;
        height = image.Height;
        return image.Pixels;
    }
}
