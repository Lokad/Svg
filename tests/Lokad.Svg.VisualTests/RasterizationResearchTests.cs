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

    [Fact]
    public void Sixteen_by_sixteen_supersampled_triangle_baseline_is_not_Chrome_exact()
    {
        string referencePath = Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png");
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(referencePath), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizeSupersampledTriangle(16));

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(14, comparison.ChangedPixelCount);
        Assert.Equal((byte)7, comparison.MaximumChannelError);
        Assert.Equal((byte)7, comparison.MaximumAlphaError);
        Assert.Equal(14, comparison.AlphaChangedPixelCount);
        Assert.Equal(1, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(6, comparison.MaximumMismatchX);
        Assert.Equal(6, comparison.MaximumMismatchY);
    }

    [Fact]
    public void Analytic_triangle_area_baseline_is_not_Chrome_exact()
    {
        string referencePath = Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png");
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(referencePath), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizeAnalyticTriangle());

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(7, comparison.ChangedPixelCount);
        Assert.Equal((byte)3, comparison.MaximumChannelError);
        Assert.Equal((byte)3, comparison.MaximumAlphaError);
        Assert.Equal(7, comparison.AlphaChangedPixelCount);
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

    private static RgbaImage RasterizeSupersampledTriangle(int samplesPerAxis)
    {
        byte[] pixels = new byte[8 * 8 * 4];
        int samplesPerPixel = checked(samplesPerAxis * samplesPerAxis);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int coveredSamples = 0;
                for (int sampleY = 0; sampleY < samplesPerAxis; sampleY++)
                {
                    for (int sampleX = 0; sampleX < samplesPerAxis; sampleX++)
                    {
                        double pixelX = x + (sampleX + 0.5) / samplesPerAxis;
                        double pixelY = y + (sampleY + 0.5) / samplesPerAxis;
                        if (IsInsideTriangle(pixelX, pixelY)) coveredSamples++;
                    }
                }
                if (coveredSamples == 0) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 3] = (byte)((coveredSamples * 255 + samplesPerPixel / 2) / samplesPerPixel);
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static RgbaImage RasterizeAnalyticTriangle()
    {
        byte[] pixels = new byte[8 * 8 * 4];
        ReadOnlySpan<Point> triangle = [new(1, 1), new(7, 2), new(2, 7)];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                List<Point> polygon = [.. triangle];
                polygon = Clip(polygon, x, true, true);
                polygon = Clip(polygon, x + 1, false, true);
                polygon = Clip(polygon, y, true, false);
                polygon = Clip(polygon, y + 1, false, false);
                byte alpha = (byte)Math.Round(Area(polygon) * 255, MidpointRounding.AwayFromZero);
                if (alpha == 0) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 3] = alpha;
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static List<Point> Clip(List<Point> polygon, double boundary, bool keepGreater, bool isVertical)
    {
        if (polygon.Count == 0) return [];
        List<Point> clipped = new(polygon.Count + 1);
        Point previous = polygon[^1];
        bool previousInside = IsInside(previous, boundary, keepGreater, isVertical);
        foreach (Point current in polygon)
        {
            bool currentInside = IsInside(current, boundary, keepGreater, isVertical);
            if (currentInside != previousInside) clipped.Add(Intersection(previous, current, boundary, isVertical));
            if (currentInside) clipped.Add(current);
            previous = current;
            previousInside = currentInside;
        }
        return clipped;
    }

    private static bool IsInside(Point point, double boundary, bool keepGreater, bool isVertical)
    {
        double value = isVertical ? point.X : point.Y;
        return keepGreater ? value >= boundary : value <= boundary;
    }

    private static Point Intersection(Point start, Point end, double boundary, bool isVertical)
    {
        double startValue = isVertical ? start.X : start.Y;
        double endValue = isVertical ? end.X : end.Y;
        double fraction = (boundary - startValue) / (endValue - startValue);
        return new Point(start.X + (end.X - start.X) * fraction, start.Y + (end.Y - start.Y) * fraction);
    }

    private static double Area(List<Point> polygon)
    {
        double twiceArea = 0;
        for (int index = 0; index < polygon.Count; index++)
        {
            Point current = polygon[index];
            Point next = polygon[(index + 1) % polygon.Count];
            twiceArea += current.X * next.Y - current.Y * next.X;
        }
        return Math.Abs(twiceArea) / 2;
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

    private readonly record struct Point(double X, double Y);
}
