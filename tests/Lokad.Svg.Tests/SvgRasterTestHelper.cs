using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

/// <summary>Separates geometry/paint pixel checks from the explicit PNG paths retained by those tests.</summary>
internal static class SvgRasterTestHelper
{
    internal static byte[] RenderPixels(string svg, out int width, out int height)
    {
        using SvgConverter.RasterizedConversion capture = SvgConverter.PrepareRasterization(
            TextEncoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None);
        width = capture.Surface.Width;
        height = capture.Surface.Height;
        // Tests own this copy after the scene/surface owner is released. Match
        // encoded PNG's zero-alpha RGB rule without changing other color lanes.
        byte[] pixels = capture.Surface.Pixels.ToArray();
        for (int offset = 0; offset < pixels.Length; offset += 4)
            if (pixels[offset + 3] == 0) pixels.AsSpan(offset, 3).Clear();
        return pixels;
    }

    internal static byte[] DecodeRgba(ReadOnlyMemory<byte> png, out int width, out int height)
    {
        RgbaImage image = PngRgbaDecoder.Decode(png, int.MaxValue);
        width = image.Width;
        height = image.Height;
        return image.Pixels;
    }
}
