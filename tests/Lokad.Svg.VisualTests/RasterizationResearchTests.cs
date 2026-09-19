using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class RasterizationResearchTests
{
    [Fact]
    public void Pixel_center_triangle_baseline_is_not_Chrome_exact()
    {
        string referencePath = Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png");
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(referencePath), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizePixelCenterTriangle());

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(16, comparison.ChangedPixelCount);
        Assert.Equal((byte)255, comparison.MaximumChannelError);
        Assert.Equal((byte)127, comparison.MaximumAlphaError);
        Assert.Equal(16, comparison.AlphaChangedPixelCount);
        Assert.Equal(1, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(6, comparison.MaximumMismatchX);
        Assert.Equal(6, comparison.MaximumMismatchY);
    }

    private static RgbaImage RasterizePixelCenterTriangle()
    {
        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                double pixelX = x + 0.5;
                double pixelY = y + 0.5;
                bool isInside = IsInsideTriangle(pixelX, pixelY);
                if (!isInside) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 3] = 255;
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static bool IsInsideTriangle(double x, double y)
    {
        double first = Cross(1, 1, 7, 2, x, y);
        double second = Cross(7, 2, 2, 7, x, y);
        double third = Cross(2, 7, 1, 1, x, y);
        return (first >= 0 && second >= 0 && third >= 0) || (first <= 0 && second <= 0 && third <= 0);
    }

    private static double Cross(double firstX, double firstY, double secondX, double secondY, double x, double y)
        => (secondX - firstX) * (y - firstY) - (secondY - firstY) * (x - firstX);
}
