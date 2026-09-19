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
        RgbaImage candidate = RasterizeAnalyticTriangle();
        PixelComparison comparison = PixelComparer.Compare(reference, candidate);

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

    [Fact]
    public void Fixed_point_coverage_triangle_baseline_is_not_Chrome_exact()
    {
        string referencePath = Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png");
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(referencePath), 64);
        RgbaImage candidate = RasterizeFixedPointCoverageTriangle(IntegerTriangle, true);
        PixelComparison comparison = PixelComparer.Compare(reference, candidate);

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(6, comparison.ChangedPixelCount);
        Assert.Equal((byte)3, comparison.MaximumChannelError);
        Assert.Equal((byte)3, comparison.MaximumAlphaError);
        Assert.Equal(6, comparison.AlphaChangedPixelCount);
        Assert.Equal(1, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(6, comparison.MaximumMismatchX);
        Assert.Equal(6, comparison.MaximumMismatchY);
    }

    [Fact]
    public void Fixed_point_coverage_without_endpoint_snapping_is_not_Chrome_exact()
    {
        string referencePath = Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png");
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(referencePath), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizeFixedPointCoverageTriangle(IntegerTriangle, false));

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(6, comparison.ChangedPixelCount);
        Assert.Equal((byte)3, comparison.MaximumChannelError);
        Assert.Equal((byte)3, comparison.MaximumAlphaError);
        Assert.Equal(6, comparison.AlphaChangedPixelCount);
        Assert.Equal(1, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(6, comparison.MaximumMismatchX);
        Assert.Equal(6, comparison.MaximumMismatchY);
    }

    [Fact]
    public void Fixed_point_coverage_fractional_triangle_baseline_is_not_Chrome_exact()
    {
        string referencePath = Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-fractional-8x8.png");
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(referencePath), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizeFixedPointCoverageTriangle(FractionalTriangle, false));

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(22, comparison.ChangedPixelCount);
        Assert.Equal((byte)255, comparison.MaximumChannelError);
        Assert.Equal((byte)32, comparison.MaximumAlphaError);
        Assert.Equal(22, comparison.AlphaChangedPixelCount);
        Assert.Equal(1, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(7, comparison.MaximumMismatchX);
        Assert.Equal(7, comparison.MaximumMismatchY);
    }

    [Fact]
    public void Sixteenth_snapped_edge_strip_triangle_baseline_is_not_Chrome_exact()
    {
        RgbaImage integerReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png")), 64);
        RgbaImage fractionalReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-fractional-8x8.png")), 64);
        PixelComparison integerComparison = PixelComparer.Compare(integerReference, RasterizeSixteenthSnappedEdgeStripTriangle(IntegerTriangle));
        PixelComparison fractionalComparison = PixelComparer.Compare(fractionalReference, RasterizeSixteenthSnappedEdgeStripTriangle(FractionalTriangle));

        Assert.True(integerComparison.DimensionsMatch);
        Assert.False(integerComparison.IsExact);
        Assert.Equal(2, integerComparison.ChangedPixelCount);
        Assert.Equal((byte)1, integerComparison.MaximumChannelError);
        Assert.Equal((byte)1, integerComparison.MaximumAlphaError);
        Assert.Equal(2, integerComparison.AlphaChangedPixelCount);
        Assert.Equal(1, integerComparison.MinimumMismatchX);
        Assert.Equal(1, integerComparison.MinimumMismatchY);
        Assert.Equal(6, integerComparison.MaximumMismatchX);
        Assert.Equal(1, integerComparison.MaximumMismatchY);

        Assert.True(fractionalComparison.DimensionsMatch);
        Assert.False(fractionalComparison.IsExact);
        Assert.Equal(23, fractionalComparison.ChangedPixelCount);
        Assert.Equal((byte)255, fractionalComparison.MaximumChannelError);
        Assert.Equal((byte)32, fractionalComparison.MaximumAlphaError);
        Assert.Equal(23, fractionalComparison.AlphaChangedPixelCount);
        Assert.Equal(1, fractionalComparison.MinimumMismatchX);
        Assert.Equal(1, fractionalComparison.MinimumMismatchY);
        Assert.Equal(7, fractionalComparison.MaximumMismatchX);
        Assert.Equal(7, fractionalComparison.MaximumMismatchY);
    }

    [Fact]
    public void Half_grid_phase_triangle_baselines_are_not_Chrome_exact()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);
        PixelComparison fixedAlpha = PixelComparer.Compare(reference, RasterizeFixedPointCoverageTriangle(HalfGridTriangle, false));
        PixelComparison snappedEdge = PixelComparer.Compare(reference, RasterizeSixteenthSnappedEdgeStripTriangle(HalfGridTriangle));

        Assert.True(fixedAlpha.DimensionsMatch);
        Assert.False(fixedAlpha.IsExact);
        Assert.Equal(22, fixedAlpha.ChangedPixelCount);
        Assert.Equal((byte)255, fixedAlpha.MaximumChannelError);
        Assert.Equal((byte)23, fixedAlpha.MaximumAlphaError);
        Assert.Equal(22, fixedAlpha.AlphaChangedPixelCount);
        Assert.Equal(1, fixedAlpha.MinimumMismatchX);
        Assert.Equal(1, fixedAlpha.MinimumMismatchY);
        Assert.Equal(7, fixedAlpha.MaximumMismatchX);
        Assert.Equal(7, fixedAlpha.MaximumMismatchY);

        Assert.True(snappedEdge.DimensionsMatch);
        Assert.False(snappedEdge.IsExact);
        Assert.Equal(20, snappedEdge.ChangedPixelCount);
        Assert.Equal((byte)255, snappedEdge.MaximumChannelError);
        Assert.Equal((byte)24, snappedEdge.MaximumAlphaError);
        Assert.Equal(20, snappedEdge.AlphaChangedPixelCount);
        Assert.Equal(1, snappedEdge.MinimumMismatchX);
        Assert.Equal(1, snappedEdge.MinimumMismatchY);
        Assert.Equal(7, snappedEdge.MaximumMismatchX);
        Assert.Equal(7, snappedEdge.MaximumMismatchY);
    }

    [Fact]
    public void Sixteenth_vertex_snapping_half_grid_baselines_are_not_Chrome_exact()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);
        PixelComparison floor = PixelComparer.Compare(reference, RasterizeFixedPointCoverageTriangle(SnapVertices(HalfGridTriangle, false), false));
        PixelComparison ceiling = PixelComparer.Compare(reference, RasterizeFixedPointCoverageTriangle(SnapVertices(HalfGridTriangle, true), false));

        Assert.True(floor.DimensionsMatch);
        Assert.False(floor.IsExact);
        Assert.Equal(20, floor.ChangedPixelCount);
        Assert.Equal((byte)255, floor.MaximumChannelError);
        Assert.Equal((byte)29, floor.MaximumAlphaError);
        Assert.Equal(20, floor.AlphaChangedPixelCount);
        Assert.Equal(1, floor.MinimumMismatchX);
        Assert.Equal(1, floor.MinimumMismatchY);
        Assert.Equal(7, floor.MaximumMismatchX);
        Assert.Equal(7, floor.MaximumMismatchY);

        Assert.True(ceiling.DimensionsMatch);
        Assert.False(ceiling.IsExact);
        Assert.Equal(21, ceiling.ChangedPixelCount);
        Assert.Equal((byte)20, ceiling.MaximumChannelError);
        Assert.Equal((byte)20, ceiling.MaximumAlphaError);
        Assert.Equal(21, ceiling.AlphaChangedPixelCount);
        Assert.Equal(1, ceiling.MinimumMismatchX);
        Assert.Equal(1, ceiling.MinimumMismatchY);
        Assert.Equal(7, ceiling.MaximumMismatchX);
        Assert.Equal(7, ceiling.MaximumMismatchY);
    }

    [Fact]
    public void Sixteenth_edge_crossing_rounding_directions_do_not_explain_the_half_grid_triangle()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);
        PixelComparison floor = PixelComparer.Compare(reference, RasterizeSixteenthSnappedEdgeStripTriangle(HalfGridTriangle, EdgeRounding.Floor));
        PixelComparison ceiling = PixelComparer.Compare(reference, RasterizeSixteenthSnappedEdgeStripTriangle(HalfGridTriangle, EdgeRounding.Ceiling));

        Assert.True(floor.DimensionsMatch);
        Assert.False(floor.IsExact);
        Assert.Equal(19, floor.ChangedPixelCount);
        Assert.Equal((byte)255, floor.MaximumChannelError);
        Assert.Equal((byte)22, floor.MaximumAlphaError);
        Assert.Equal(19, floor.AlphaChangedPixelCount);
        Assert.Equal(1, floor.MinimumMismatchX);
        Assert.Equal(1, floor.MinimumMismatchY);
        Assert.Equal(7, floor.MaximumMismatchX);
        Assert.Equal(7, floor.MaximumMismatchY);

        Assert.True(ceiling.DimensionsMatch);
        Assert.False(ceiling.IsExact);
        Assert.Equal(22, ceiling.ChangedPixelCount);
        Assert.Equal((byte)255, ceiling.MaximumChannelError);
        Assert.Equal((byte)34, ceiling.MaximumAlphaError);
        Assert.Equal(22, ceiling.AlphaChangedPixelCount);
        Assert.Equal(1, ceiling.MinimumMismatchX);
        Assert.Equal(1, ceiling.MinimumMismatchY);
        Assert.Equal(7, ceiling.MaximumMismatchX);
        Assert.Equal(7, ceiling.MaximumMismatchY);
    }

    [Fact]
    public void Skia_style_quarter_y_input_quantization_does_not_make_edge_strip_coverage_exact()
    {
        RgbaImage integerReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png")), 64);
        RgbaImage fractionalReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-fractional-8x8.png")), 64);
        RgbaImage halfGridReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);

        PixelComparison integer = PixelComparer.Compare(integerReference, RasterizeSixteenthSnappedEdgeStripTriangle(SnapYToQuarter(IntegerTriangle)));
        PixelComparison fractional = PixelComparer.Compare(fractionalReference, RasterizeSixteenthSnappedEdgeStripTriangle(SnapYToQuarter(FractionalTriangle)));
        PixelComparison halfGrid = PixelComparer.Compare(halfGridReference, RasterizeSixteenthSnappedEdgeStripTriangle(SnapYToQuarter(HalfGridTriangle)));

        Assert.True(integer.DimensionsMatch);
        Assert.False(integer.IsExact);
        Assert.Equal(2, integer.ChangedPixelCount);
        Assert.Equal((byte)1, integer.MaximumChannelError);
        Assert.Equal((byte)1, integer.MaximumAlphaError);
        Assert.Equal(2, integer.AlphaChangedPixelCount);
        Assert.Equal(1, integer.MinimumMismatchX);
        Assert.Equal(1, integer.MinimumMismatchY);
        Assert.Equal(6, integer.MaximumMismatchX);
        Assert.Equal(1, integer.MaximumMismatchY);

        Assert.True(fractional.DimensionsMatch);
        Assert.False(fractional.IsExact);
        Assert.Equal(12, fractional.ChangedPixelCount);
        Assert.Equal((byte)2, fractional.MaximumChannelError);
        Assert.Equal((byte)2, fractional.MaximumAlphaError);
        Assert.Equal(12, fractional.AlphaChangedPixelCount);
        Assert.Equal(1, fractional.MinimumMismatchX);
        Assert.Equal(1, fractional.MinimumMismatchY);
        Assert.Equal(7, fractional.MaximumMismatchX);
        Assert.Equal(6, fractional.MaximumMismatchY);

        Assert.True(halfGrid.DimensionsMatch);
        Assert.False(halfGrid.IsExact);
        Assert.Equal(16, halfGrid.ChangedPixelCount);
        Assert.Equal((byte)8, halfGrid.MaximumChannelError);
        Assert.Equal((byte)8, halfGrid.MaximumAlphaError);
        Assert.Equal(16, halfGrid.AlphaChangedPixelCount);
        Assert.Equal(1, halfGrid.MinimumMismatchX);
        Assert.Equal(1, halfGrid.MinimumMismatchY);
        Assert.Equal(7, halfGrid.MaximumMismatchX);
        Assert.Equal(7, halfGrid.MaximumMismatchY);
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

    private static RgbaImage RasterizeFixedPointCoverageTriangle(ReadOnlySpan<Point> triangle, bool snapEndpoints)
    {
        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                List<Point> polygon = [.. triangle];
                polygon = Clip(polygon, x, true, true);
                polygon = Clip(polygon, x + 1, false, true);
                polygon = Clip(polygon, y, true, false);
                polygon = Clip(polygon, y + 1, false, false);
                int fixedPointAlpha = (int)Math.Floor(Area(polygon) * 256);
                byte alpha = !snapEndpoints ? (byte)Math.Min(fixedPointAlpha, byte.MaxValue)
                    : fixedPointAlpha > 247 ? byte.MaxValue
                    : fixedPointAlpha < 8 ? byte.MinValue
                    : (byte)fixedPointAlpha;
                if (alpha == 0) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 3] = alpha;
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static Point[] SnapVertices(ReadOnlySpan<Point> triangle, bool roundUp)
    {
        Point[] snapped = new Point[triangle.Length];
        for (int index = 0; index < triangle.Length; index++)
        {
            Point point = triangle[index];
            snapped[index] = new Point(Snap(point.X, roundUp), Snap(point.Y, roundUp));
        }
        return snapped;

        static double Snap(double value, bool roundUp) => (roundUp ? Math.Ceiling(value * 16) : Math.Floor(value * 16)) / 16;
    }

    private static Point[] SnapYToQuarter(ReadOnlySpan<Point> triangle)
    {
        Point[] snapped = new Point[triangle.Length];
        for (int index = 0; index < triangle.Length; index++)
        {
            Point point = triangle[index];
            snapped[index] = new Point(point.X, Math.Floor(point.Y * 4 + 0.5) / 4);
        }
        return snapped;
    }

    private static RgbaImage RasterizeSixteenthSnappedEdgeStripTriangle(ReadOnlySpan<Point> triangle)
        => RasterizeSixteenthSnappedEdgeStripTriangle(triangle, EdgeRounding.Nearest);

    private static RgbaImage RasterizeSixteenthSnappedEdgeStripTriangle(ReadOnlySpan<Point> triangle, EdgeRounding rounding)
    {
        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            List<double> boundaries = [y, y + 1];
            foreach (Point point in triangle)
            {
                if (point.Y > y && point.Y < y + 1) boundaries.Add(point.Y);
            }
            boundaries.Sort();

            for (int x = 0; x < 8; x++)
            {
                double area = 0;
                for (int interval = 0; interval < boundaries.Count - 1; interval++)
                {
                    double top = boundaries[interval];
                    double bottom = boundaries[interval + 1];
                    if (top == bottom) continue;
                    (Point LeftTop, Point RightTop, Point LeftBottom, Point RightBottom)? strip = SnappedStrip(triangle, top, bottom, rounding);
                    if (strip is null) continue;
                    (Point leftTop, Point rightTop, Point leftBottom, Point rightBottom) = strip.Value;
                    List<Point> polygon = [leftTop, rightTop, rightBottom, leftBottom];
                    polygon = Clip(polygon, x, true, true);
                    polygon = Clip(polygon, x + 1, false, true);
                    polygon = Clip(polygon, y, true, false);
                    polygon = Clip(polygon, y + 1, false, false);
                    area += Area(polygon);
                }
                int fixedPointAlpha = (int)Math.Floor(area * 256);
                if (fixedPointAlpha == 0) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = byte.MaxValue;
                pixels[offset + 3] = (byte)Math.Min(fixedPointAlpha, byte.MaxValue);
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static (Point LeftTop, Point RightTop, Point LeftBottom, Point RightBottom)? SnappedStrip(ReadOnlySpan<Point> triangle, double top, double bottom, EdgeRounding rounding)
    {
        double middle = (top + bottom) / 2;
        List<(Point Top, Point Bottom)> edges = [];
        for (int index = 0; index < triangle.Length; index++)
        {
            Point first = triangle[index];
            Point second = triangle[(index + 1) % triangle.Length];
            if ((first.Y < middle && middle < second.Y) || (second.Y < middle && middle < first.Y)) edges.Add((first, second));
        }
        if (edges.Count == 0) return null;
        Assert.Equal(2, edges.Count);
        Point firstTop = SnapEdgeX(edges[0].Top, edges[0].Bottom, top, rounding);
        Point firstBottom = SnapEdgeX(edges[0].Top, edges[0].Bottom, bottom, rounding);
        Point secondTop = SnapEdgeX(edges[1].Top, edges[1].Bottom, top, rounding);
        Point secondBottom = SnapEdgeX(edges[1].Top, edges[1].Bottom, bottom, rounding);
        return firstTop.X <= secondTop.X
            ? (firstTop, secondTop, firstBottom, secondBottom)
            : (secondTop, firstTop, secondBottom, firstBottom);
    }

    private static Point SnapEdgeX(Point first, Point second, double y, EdgeRounding rounding)
    {
        double x = first.X + (y - first.Y) * (second.X - first.X) / (second.Y - first.Y);
        double snappedX = rounding switch
        {
            EdgeRounding.Floor => Math.Floor(x * 16),
            EdgeRounding.Nearest => Math.Floor(x * 16 + 0.5),
            EdgeRounding.Ceiling => Math.Ceiling(x * 16),
            _ => throw new InvalidOperationException("Unknown edge rounding direction.")
        };
        return new Point(snappedX / 16, y);
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

    private static readonly Point[] IntegerTriangle = [new(1, 1), new(7, 2), new(2, 7)];
    private static readonly Point[] FractionalTriangle = [new(1.25, 1.125), new(7.25, 2.125), new(2.25, 7.125)];
    private static readonly Point[] HalfGridTriangle = [new(1.28125, 1.15625), new(7.28125, 2.15625), new(2.28125, 7.15625)];

    private enum EdgeRounding { Floor, Nearest, Ceiling }

    private readonly record struct Point(double X, double Y);
}
