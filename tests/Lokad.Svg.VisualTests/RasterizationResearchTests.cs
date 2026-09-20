using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class RasterizationResearchTests
{
    private const long FixedOne = 65_536;

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

    [Fact]
    public void Source_guided_fixed_edge_stepping_does_not_make_edge_strip_coverage_exact()
    {
        RgbaImage integerReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png")), 64);
        RgbaImage fractionalReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-fractional-8x8.png")), 64);
        RgbaImage halfGridReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);

        PixelComparison integer = PixelComparer.Compare(integerReference, RasterizeSourceGuidedFixedEdgeStripTriangle(IntegerTriangle));
        PixelComparison fractional = PixelComparer.Compare(fractionalReference, RasterizeSourceGuidedFixedEdgeStripTriangle(FractionalTriangle));
        PixelComparison halfGrid = PixelComparer.Compare(halfGridReference, RasterizeSourceGuidedFixedEdgeStripTriangle(HalfGridTriangle));

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
        Assert.Equal(15, halfGrid.ChangedPixelCount);
        Assert.Equal((byte)2, halfGrid.MaximumChannelError);
        Assert.Equal((byte)2, halfGrid.MaximumAlphaError);
        Assert.Equal(15, halfGrid.AlphaChangedPixelCount);
        Assert.Equal(1, halfGrid.MinimumMismatchX);
        Assert.Equal(1, halfGrid.MinimumMismatchY);
        Assert.Equal(7, halfGrid.MaximumMismatchX);
        Assert.Equal(6, halfGrid.MaximumMismatchY);
    }

    [Fact]
    public void Source_guided_incremental_alpha_accumulation_does_not_make_edge_strip_coverage_exact()
    {
        RgbaImage integerReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png")), 64);
        RgbaImage fractionalReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-fractional-8x8.png")), 64);
        RgbaImage halfGridReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);

        PixelComparison integer = PixelComparer.Compare(integerReference, RasterizeSourceGuidedFixedEdgeStripTriangle(IntegerTriangle, true));
        PixelComparison fractional = PixelComparer.Compare(fractionalReference, RasterizeSourceGuidedFixedEdgeStripTriangle(FractionalTriangle, true));
        PixelComparison halfGrid = PixelComparer.Compare(halfGridReference, RasterizeSourceGuidedFixedEdgeStripTriangle(HalfGridTriangle, true));

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
        Assert.Equal(15, halfGrid.ChangedPixelCount);
        Assert.Equal((byte)2, halfGrid.MaximumChannelError);
        Assert.Equal((byte)2, halfGrid.MaximumAlphaError);
        Assert.Equal(15, halfGrid.AlphaChangedPixelCount);
        Assert.Equal(1, halfGrid.MinimumMismatchX);
        Assert.Equal(1, halfGrid.MinimumMismatchY);
        Assert.Equal(7, halfGrid.MaximumMismatchX);
        Assert.Equal(6, halfGrid.MaximumMismatchY);
    }

    [Fact]
    public void Source_guided_trapezoid_alpha_math_does_not_make_coverage_exact()
    {
        RgbaImage integerReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-8x8.png")), 64);
        RgbaImage fractionalReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-fractional-8x8.png")), 64);
        RgbaImage halfGridReference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-triangle-half-grid-8x8.png")), 64);

        PixelComparison integer = PixelComparer.Compare(integerReference, RasterizeSourceGuidedTrapezoidAlphaTriangle(IntegerTriangle));
        PixelComparison fractional = PixelComparer.Compare(fractionalReference, RasterizeSourceGuidedTrapezoidAlphaTriangle(FractionalTriangle));
        PixelComparison halfGrid = PixelComparer.Compare(halfGridReference, RasterizeSourceGuidedTrapezoidAlphaTriangle(HalfGridTriangle));

        Assert.True(integer.DimensionsMatch);
        Assert.False(integer.IsExact);
        Assert.Equal(6, integer.ChangedPixelCount);
        Assert.Equal((byte)255, integer.MaximumChannelError);
        Assert.Equal((byte)211, integer.MaximumAlphaError);
        Assert.Equal(6, integer.AlphaChangedPixelCount);
        Assert.Equal(1, integer.MinimumMismatchX);
        Assert.Equal(1, integer.MinimumMismatchY);
        Assert.Equal(6, integer.MaximumMismatchX);
        Assert.Equal(1, integer.MaximumMismatchY);

        Assert.True(fractional.DimensionsMatch);
        Assert.False(fractional.IsExact);
        Assert.Equal(18, fractional.ChangedPixelCount);
        Assert.Equal((byte)255, fractional.MaximumChannelError);
        Assert.Equal((byte)249, fractional.MaximumAlphaError);
        Assert.Equal(18, fractional.AlphaChangedPixelCount);
        Assert.Equal(1, fractional.MinimumMismatchX);
        Assert.Equal(1, fractional.MinimumMismatchY);
        Assert.Equal(7, fractional.MaximumMismatchX);
        Assert.Equal(6, fractional.MaximumMismatchY);

        Assert.True(halfGrid.DimensionsMatch);
        Assert.False(halfGrid.IsExact);
        Assert.Equal(18, halfGrid.ChangedPixelCount);
        Assert.Equal((byte)255, halfGrid.MaximumChannelError);
        Assert.Equal((byte)249, halfGrid.MaximumAlphaError);
        Assert.Equal(18, halfGrid.AlphaChangedPixelCount);
        Assert.Equal(1, halfGrid.MinimumMismatchX);
        Assert.Equal(1, halfGrid.MinimumMismatchY);
        Assert.Equal(7, halfGrid.MaximumMismatchX);
        Assert.Equal(6, halfGrid.MaximumMismatchY);
    }

    [Fact]
    public void Analytic_semiopaque_diagonal_source_over_baseline_is_not_Chrome_exact()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-overlap-8x8.png")), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizeAnalyticDiagonalOverlap());

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(37, comparison.ChangedPixelCount);
        Assert.Equal((byte)33, comparison.MaximumChannelError);
        Assert.Equal((byte)27, comparison.MaximumAlphaError);
        Assert.Equal(36, comparison.AlphaChangedPixelCount);
        Assert.Equal(0, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(7, comparison.MaximumMismatchX);
        Assert.Equal(7, comparison.MaximumMismatchY);
    }

    [Fact]
    public void Source_guided_fixed_edge_semiopaque_source_over_baseline_is_not_Chrome_exact()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-overlap-8x8.png")), 64);
        PixelComparison comparison = PixelComparer.Compare(reference, RasterizeSourceGuidedDiagonalOverlap());

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(26, comparison.ChangedPixelCount);
        Assert.Equal((byte)3, comparison.MaximumChannelError);
        Assert.Equal((byte)1, comparison.MaximumAlphaError);
        Assert.Equal(19, comparison.AlphaChangedPixelCount);
        Assert.Equal(0, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(7, comparison.MaximumMismatchX);
        Assert.Equal(6, comparison.MaximumMismatchY);
    }

    [Fact]
    public void Quantized_source_over_diagonal_overlap_baseline_is_not_Chrome_exact()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-overlap-8x8.png")), 64);
        RgbaImage candidate = RasterizeQuantizedSourceOverDiagonalOverlap();
        PixelComparison comparison = PixelComparer.Compare(reference, candidate);
        PixelComparison skiaAlphaMultiply = PixelComparer.Compare(candidate, RasterizeQuantizedSourceOverDiagonalOverlap(true));

        Assert.True(comparison.DimensionsMatch);
        Assert.False(comparison.IsExact);
        Assert.Equal(22, comparison.ChangedPixelCount);
        Assert.Equal((byte)6, comparison.MaximumChannelError);
        Assert.Equal((byte)1, comparison.MaximumAlphaError);
        Assert.Equal(17, comparison.AlphaChangedPixelCount);
        Assert.Equal(0, comparison.MinimumMismatchX);
        Assert.Equal(1, comparison.MinimumMismatchY);
        Assert.Equal(7, comparison.MaximumMismatchX);
        Assert.Equal(6, comparison.MaximumMismatchY);
        Assert.True(skiaAlphaMultiply.IsExact);
    }

    [Fact]
    public void Phase_shifted_diagonal_overlap_keeps_fixed_edge_and_byte_compositing_non_exact()
    {
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "references", "diagonal-overlap-half-grid-8x8.png")), 64);
        PixelComparison fixedEdge = PixelComparer.Compare(reference, RasterizeSourceGuidedDiagonalOverlap(RedHalfGridOverlapTriangle, BlueHalfGridOverlapTriangle));
        PixelComparison quantized = PixelComparer.Compare(reference, RasterizeQuantizedSourceOverDiagonalOverlap(RedHalfGridOverlapTriangle, BlueHalfGridOverlapTriangle));

        Assert.True(fixedEdge.DimensionsMatch);
        Assert.False(fixedEdge.IsExact);
        Assert.Equal(29, fixedEdge.ChangedPixelCount);
        Assert.Equal((byte)13, fixedEdge.MaximumChannelError);
        Assert.Equal((byte)5, fixedEdge.MaximumAlphaError);
        Assert.Equal(24, fixedEdge.AlphaChangedPixelCount);
        Assert.Equal(0, fixedEdge.MinimumMismatchX);
        Assert.Equal(1, fixedEdge.MinimumMismatchY);
        Assert.Equal(6, fixedEdge.MaximumMismatchX);
        Assert.Equal(7, fixedEdge.MaximumMismatchY);

        Assert.True(quantized.DimensionsMatch);
        Assert.False(quantized.IsExact);
        Assert.Equal(23, quantized.ChangedPixelCount);
        Assert.Equal((byte)13, quantized.MaximumChannelError);
        Assert.Equal((byte)5, quantized.MaximumAlphaError);
        Assert.Equal(18, quantized.AlphaChangedPixelCount);
        Assert.Equal(0, quantized.MinimumMismatchX);
        Assert.Equal(1, quantized.MinimumMismatchY);
        Assert.Equal(6, quantized.MaximumMismatchX);
        Assert.Equal(7, quantized.MaximumMismatchY);
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

    private static RgbaImage RasterizeAnalyticDiagonalOverlap()
    {
        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                double redAlpha = Area(ClipToPixel(RedOverlapTriangle, x, y)) * 0.5;
                double blueAlpha = Area(ClipToPixel(BlueOverlapTriangle, x, y)) * 0.5;
                double alpha = blueAlpha + redAlpha * (1 - blueAlpha);
                if (alpha == 0) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = ToByte(redAlpha * (1 - blueAlpha) / alpha);
                pixels[offset + 2] = ToByte(blueAlpha / alpha);
                pixels[offset + 3] = ToByte(alpha);
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static RgbaImage RasterizeSourceGuidedDiagonalOverlap()
        => RasterizeSourceGuidedDiagonalOverlap(RedOverlapTriangle, BlueOverlapTriangle);

    private static RgbaImage RasterizeSourceGuidedDiagonalOverlap(ReadOnlySpan<Point> redTriangle, ReadOnlySpan<Point> blueTriangle)
    {
        RgbaImage redCoverage = RasterizeSourceGuidedFixedEdgeStripTriangle(redTriangle);
        RgbaImage blueCoverage = RasterizeSourceGuidedFixedEdgeStripTriangle(blueTriangle);
        byte[] pixels = new byte[8 * 8 * 4];
        for (int index = 0; index < 8 * 8; index++)
        {
            double redAlpha = redCoverage.Pixels[index * 4 + 3] / 255d * 0.5;
            double blueAlpha = blueCoverage.Pixels[index * 4 + 3] / 255d * 0.5;
            double alpha = blueAlpha + redAlpha * (1 - blueAlpha);
            if (alpha == 0) continue;
            int offset = index * 4;
            pixels[offset] = ToByte(redAlpha * (1 - blueAlpha) / alpha);
            pixels[offset + 2] = ToByte(blueAlpha / alpha);
            pixels[offset + 3] = ToByte(alpha);
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static RgbaImage RasterizeQuantizedSourceOverDiagonalOverlap()
        => RasterizeQuantizedSourceOverDiagonalOverlap(RedOverlapTriangle, BlueOverlapTriangle, false);

    private static RgbaImage RasterizeQuantizedSourceOverDiagonalOverlap(bool useSkiaAlphaMultiply)
        => RasterizeQuantizedSourceOverDiagonalOverlap(RedOverlapTriangle, BlueOverlapTriangle, useSkiaAlphaMultiply);

    private static RgbaImage RasterizeQuantizedSourceOverDiagonalOverlap(ReadOnlySpan<Point> redTriangle, ReadOnlySpan<Point> blueTriangle)
        => RasterizeQuantizedSourceOverDiagonalOverlap(redTriangle, blueTriangle, false);

    private static RgbaImage RasterizeQuantizedSourceOverDiagonalOverlap(ReadOnlySpan<Point> redTriangle, ReadOnlySpan<Point> blueTriangle, bool useSkiaAlphaMultiply)
    {
        RgbaImage redCoverage = RasterizeSourceGuidedFixedEdgeStripTriangle(redTriangle);
        RgbaImage blueCoverage = RasterizeSourceGuidedFixedEdgeStripTriangle(blueTriangle);
        byte[] pixels = new byte[8 * 8 * 4];
        for (int index = 0; index < 8 * 8; index++)
        {
            int offset = index * 4;
            byte redAlpha = ToCoverageOpacity(redCoverage.Pixels[offset + 3], useSkiaAlphaMultiply);
            byte blueAlpha = ToCoverageOpacity(blueCoverage.Pixels[offset + 3], useSkiaAlphaMultiply);
            BlendSourceOver(pixels.AsSpan(offset, 4), byte.MaxValue, 0, 0, redAlpha);
            BlendSourceOver(pixels.AsSpan(offset, 4), 0, 0, byte.MaxValue, blueAlpha);
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static byte ToCoverageOpacity(byte coverage, bool useSkiaAlphaMultiply)
        => useSkiaAlphaMultiply ? (byte)((coverage * 128 + 255) >> 8) : (byte)((coverage * 128 + 127) / 255);

    private static void BlendSourceOver(Span<byte> destination, byte red, byte green, byte blue, byte alpha)
    {
        if (alpha == 0) return;
        int sourceAlpha = alpha;
        int destinationAlpha = destination[3];
        int inverseSourceAlpha = byte.MaxValue - sourceAlpha;
        int outputAlpha = sourceAlpha + (destinationAlpha * inverseSourceAlpha + 127) / 255;
        if (outputAlpha == 0) return;
        destination[0] = ComposeChannel(destination[0], red, destinationAlpha, sourceAlpha, inverseSourceAlpha, outputAlpha);
        destination[1] = ComposeChannel(destination[1], green, destinationAlpha, sourceAlpha, inverseSourceAlpha, outputAlpha);
        destination[2] = ComposeChannel(destination[2], blue, destinationAlpha, sourceAlpha, inverseSourceAlpha, outputAlpha);
        destination[3] = (byte)outputAlpha;
    }

    private static byte ComposeChannel(byte destination, byte source, int destinationAlpha, int sourceAlpha, int inverseSourceAlpha, int outputAlpha)
    {
        int sourceContribution = (source * sourceAlpha + outputAlpha / 2) / outputAlpha;
        int retainedContribution = (destination * destinationAlpha * inverseSourceAlpha + 255 * outputAlpha - 1) / (255 * outputAlpha);
        return checked((byte)(sourceContribution + retainedContribution));
    }

    private static List<Point> ClipToPixel(ReadOnlySpan<Point> polygon, int x, int y)
    {
        List<Point> clipped = [.. polygon];
        clipped = Clip(clipped, x, true, true);
        clipped = Clip(clipped, x + 1, false, true);
        clipped = Clip(clipped, y, true, false);
        return Clip(clipped, y + 1, false, false);
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

    private static RgbaImage RasterizeSourceGuidedFixedEdgeStripTriangle(ReadOnlySpan<Point> triangle)
        => RasterizeSourceGuidedFixedEdgeStripTriangle(triangle, false);

    private static RgbaImage RasterizeSourceGuidedFixedEdgeStripTriangle(ReadOnlySpan<Point> triangle, bool accumulateEachStrip)
    {
        FixedPoint[] fixedTriangle = new FixedPoint[triangle.Length];
        for (int index = 0; index < triangle.Length; index++) fixedTriangle[index] = FixedPoint.From(triangle[index]);

        byte[] pixels = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            List<long> boundaries = [ToFixed(y), ToFixed(y + 1)];
            foreach (FixedPoint point in fixedTriangle)
            {
                if (point.Y > boundaries[0] && point.Y < boundaries[1]) boundaries.Add(point.Y);
            }
            boundaries.Sort();

            for (int x = 0; x < 8; x++)
            {
                double area = 0;
                int accumulatedAlpha = 0;
                for (int interval = 0; interval < boundaries.Count - 1; interval++)
                {
                    long top = boundaries[interval];
                    long bottom = boundaries[interval + 1];
                    if (top == bottom) continue;
                    (Point LeftTop, Point RightTop, Point LeftBottom, Point RightBottom)? strip = SourceGuidedSnappedStrip(fixedTriangle, top, bottom);
                    if (strip is null) continue;
                    (Point leftTop, Point rightTop, Point leftBottom, Point rightBottom) = strip.Value;
                    List<Point> polygon = [leftTop, rightTop, rightBottom, leftBottom];
                    polygon = Clip(polygon, x, true, true);
                    polygon = Clip(polygon, x + 1, false, true);
                    polygon = Clip(polygon, y, true, false);
                    polygon = Clip(polygon, y + 1, false, false);
                    if (accumulateEachStrip)
                    {
                        int stripAlpha = (int)Math.Floor(Area(polygon) * 256);
                        accumulatedAlpha = Math.Min(byte.MaxValue, accumulatedAlpha + stripAlpha);
                    }
                    else area += Area(polygon);
                }
                int fixedPointAlpha = accumulateEachStrip ? accumulatedAlpha : (int)Math.Floor(area * 256);
                if (fixedPointAlpha == 0) continue;
                int offset = (y * 8 + x) * 4;
                pixels[offset] = byte.MaxValue;
                pixels[offset + 3] = (byte)Math.Min(fixedPointAlpha, byte.MaxValue);
            }
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static RgbaImage RasterizeSourceGuidedTrapezoidAlphaTriangle(ReadOnlySpan<Point> triangle)
    {
        FixedPoint[] fixedTriangle = new FixedPoint[triangle.Length];
        for (int index = 0; index < triangle.Length; index++) fixedTriangle[index] = FixedPoint.From(triangle[index]);

        byte[] alpha = new byte[8 * 8];
        for (int y = 0; y < 8; y++)
        {
            List<long> boundaries = [ToFixed(y), ToFixed(y + 1)];
            foreach (FixedPoint point in fixedTriangle)
            {
                if (point.Y > boundaries[0] && point.Y < boundaries[1]) boundaries.Add(point.Y);
            }
            boundaries.Sort();

            for (int interval = 0; interval < boundaries.Count - 1; interval++)
            {
                long top = boundaries[interval];
                long bottom = boundaries[interval + 1];
                FixedTrapezoid? trapezoid = SourceGuidedFixedTrapezoid(fixedTriangle, top, bottom);
                if (trapezoid is null) continue;
                BlitSourceGuidedTrapezoid(alpha, (int)(top >> 16), trapezoid.Value, ToAlphaForHeight(bottom - top));
            }
        }

        byte[] pixels = new byte[8 * 8 * 4];
        for (int index = 0; index < alpha.Length; index++)
        {
            if (alpha[index] == 0) continue;
            int offset = index * 4;
            pixels[offset] = byte.MaxValue;
            pixels[offset + 3] = alpha[index];
        }
        return new RgbaImage(8, 8, pixels);
    }

    private static void BlitSourceGuidedTrapezoid(byte[] mask, int y, FixedTrapezoid trapezoid, byte fullAlpha)
    {
        long upperLeft = trapezoid.UpperLeft;
        long upperRight = trapezoid.UpperRight;
        long lowerLeft = trapezoid.LowerLeft;
        long lowerRight = trapezoid.LowerRight;
        if (upperLeft > upperRight) return;
        if (lowerLeft > lowerRight)
        {
            long intersection = (Math.Max(upperLeft, lowerLeft) + Math.Min(upperRight, lowerRight)) / 2;
            lowerLeft = intersection;
            lowerRight = intersection;
        }
        if (upperLeft == upperRight && lowerLeft == lowerRight) return;
        if (upperLeft > lowerLeft) (upperLeft, lowerLeft) = (lowerLeft, upperLeft);
        if (upperRight > lowerRight) (upperRight, lowerRight) = (lowerRight, upperRight);

        long joinLeft = CeilToFixed(lowerLeft);
        long joinRight = FloorToFixed(upperRight);
        if (joinLeft <= joinRight)
        {
            if (upperLeft < joinLeft)
            {
                int length = CeilToInt(joinLeft - upperLeft);
                if (length == 1)
                {
                    BlitSingle(mask, y, FloorToInt(upperLeft), TrapezoidToAlpha(joinLeft - upperLeft, joinLeft - lowerLeft), fullAlpha);
                }
                else if (length == 2)
                {
                    long first = FloorToFixed(upperLeft) + FixedOne - upperLeft;
                    long second = lowerLeft - upperLeft - first;
                    BlitTwo(mask, y, FloorToInt(upperLeft), fullAlpha - PartialTriangleToAlpha(first, trapezoid.LeftInverseSlope), PartialTriangleToAlpha(second, trapezoid.LeftInverseSlope));
                }
                else BlitAaaTrapezoid(mask, y, upperLeft, joinLeft, lowerLeft, joinLeft, trapezoid.LeftInverseSlope, int.MaxValue, fullAlpha);
            }
            if (joinLeft < joinRight) BlitFull(mask, y, FloorToInt(joinLeft), FloorToInt(joinRight - joinLeft), fullAlpha);
            if (lowerRight > joinRight)
            {
                int length = CeilToInt(lowerRight - joinRight);
                if (length == 1)
                {
                    BlitSingle(mask, y, FloorToInt(joinRight), TrapezoidToAlpha(upperRight - joinRight, lowerRight - joinRight), fullAlpha);
                }
                else if (length == 2)
                {
                    long first = joinRight + FixedOne - upperRight;
                    long second = lowerRight - upperRight - first;
                    BlitTwo(mask, y, FloorToInt(joinRight), PartialTriangleToAlpha(first, trapezoid.RightInverseSlope), fullAlpha - PartialTriangleToAlpha(second, trapezoid.RightInverseSlope));
                }
                else BlitAaaTrapezoid(mask, y, joinRight, upperRight, joinRight, lowerRight, int.MaxValue, trapezoid.RightInverseSlope, fullAlpha);
            }
        }
        else BlitAaaTrapezoid(mask, y, upperLeft, upperRight, lowerLeft, lowerRight, trapezoid.LeftInverseSlope, trapezoid.RightInverseSlope, fullAlpha);
    }

    private static void BlitAaaTrapezoid(byte[] mask, int y, long upperLeft, long upperRight, long lowerLeft, long lowerRight, long leftInverseSlope, long rightInverseSlope, byte fullAlpha)
    {
        int left = FloorToInt(upperLeft);
        int right = CeilToInt(lowerRight);
        int length = right - left;
        if (length == 1)
        {
            BlitSingle(mask, y, left, TrapezoidToAlpha(upperRight - upperLeft, lowerRight - lowerLeft), fullAlpha);
            return;
        }

        int[] alphas = Enumerable.Repeat((int)fullAlpha, length).ToArray();
        int upperLeftPixel = FloorToInt(upperLeft);
        int lowerLeftPixel = CeilToInt(lowerLeft);
        if (upperLeftPixel + 2 == lowerLeftPixel)
        {
            long first = FloorToFixed(upperLeft) + FixedOne - upperLeft;
            long second = lowerLeft - upperLeft - first;
            SubtractAlpha(alphas, 0, fullAlpha - PartialTriangleToAlpha(first, leftInverseSlope));
            SubtractAlpha(alphas, 1, PartialTriangleToAlpha(second, leftInverseSlope));
        }
        else SubtractAlpha(alphas, upperLeftPixel - left, ComputeAlphaBelow(upperLeft - FloorToFixed(upperLeftPixel), lowerLeft - FloorToFixed(upperLeftPixel), leftInverseSlope, fullAlpha));

        int upperRightPixel = FloorToInt(upperRight);
        int lowerRightPixel = CeilToInt(lowerRight);
        if (upperRightPixel + 2 == lowerRightPixel)
        {
            long first = FloorToFixed(upperRight) + FixedOne - upperRight;
            long second = lowerRight - upperRight - first;
            SubtractAlpha(alphas, length - 2, PartialTriangleToAlpha(first, rightInverseSlope));
            SubtractAlpha(alphas, length - 1, fullAlpha - PartialTriangleToAlpha(second, rightInverseSlope));
        }
        else SubtractAlpha(alphas, upperRightPixel - left, ComputeAlphaAbove(upperRight - FloorToFixed(upperRightPixel), lowerRight - FloorToFixed(upperRightPixel), rightInverseSlope, fullAlpha));

        for (int index = 0; index < alphas.Length; index++) AddAlpha(mask, y, left + index, alphas[index]);
    }

    private static int[] ComputeAlphaAbove(long left, long right, long inverseSlope, byte fullAlpha)
    {
        int count = CeilToInt(right);
        int[] alphas = new int[count];
        if (count == 0) return alphas;
        if (count == 1)
        {
            alphas[0] = GetPartialAlpha((int)(((2L * FixedOne) - left - right) >> 9), fullAlpha);
            return alphas;
        }
        long first = FixedOne - left;
        long last = right - (count - 1) * FixedOne;
        long firstHeight = FixedMultiply(first, inverseSlope);
        alphas[0] = (int)(FixedMultiply(first, firstHeight) >> 9);
        long alpha16 = firstHeight + (inverseSlope >> 1);
        for (int index = 1; index < count - 1; index++)
        {
            alphas[index] = (int)((alpha16 >> 8) & byte.MaxValue);
            alpha16 += inverseSlope;
        }
        alphas[^1] = fullAlpha - PartialTriangleToAlpha(last, inverseSlope);
        return alphas;
    }

    private static int[] ComputeAlphaBelow(long left, long right, long inverseSlope, byte fullAlpha)
    {
        int count = CeilToInt(right);
        int[] alphas = new int[count];
        if (count == 0) return alphas;
        if (count == 1)
        {
            alphas[0] = GetPartialAlpha(TrapezoidToAlpha(left, right), fullAlpha);
            return alphas;
        }
        long first = FixedOne - left;
        long last = right - (count - 1) * FixedOne;
        long lastHeight = FixedMultiply(last, inverseSlope);
        alphas[^1] = (int)(FixedMultiply(last, lastHeight) >> 9);
        long alpha16 = lastHeight + (inverseSlope >> 1);
        for (int index = count - 2; index > 0; index--)
        {
            alphas[index] = (int)((alpha16 >> 8) & byte.MaxValue);
            alpha16 += inverseSlope;
        }
        alphas[0] = fullAlpha - PartialTriangleToAlpha(first, inverseSlope);
        return alphas;
    }

    private static void SubtractAlpha(int[] destination, int offset, int value)
    {
        if (offset < 0 || offset >= destination.Length) return;
        destination[offset] = Math.Max(0, destination[offset] - value);
    }

    private static void SubtractAlpha(int[] destination, int offset, ReadOnlySpan<int> values)
    {
        for (int index = 0; index < values.Length; index++) SubtractAlpha(destination, offset + index, values[index]);
    }

    private static void BlitSingle(byte[] mask, int y, int x, int alpha, byte fullAlpha)
    {
        if (fullAlpha == byte.MaxValue) SetAlpha(mask, y, x, alpha);
        else AddAlpha(mask, y, x, GetPartialAlpha(alpha, fullAlpha));
    }

    private static void BlitTwo(byte[] mask, int y, int x, int first, int second)
    {
        AddAlpha(mask, y, x, first);
        AddAlpha(mask, y, x + 1, second);
    }

    private static void BlitFull(byte[] mask, int y, int x, int length, byte alpha)
    {
        for (int index = 0; index < length; index++) AddAlpha(mask, y, x + index, alpha);
    }

    private static void AddAlpha(byte[] mask, int y, int x, int alpha)
    {
        if ((uint)x >= 8 || (uint)y >= 8 || alpha <= 0) return;
        int offset = y * 8 + x;
        mask[offset] = (byte)Math.Min(byte.MaxValue, mask[offset] + alpha);
    }

    private static void SetAlpha(byte[] mask, int y, int x, int alpha)
    {
        if ((uint)x >= 8 || (uint)y >= 8 || alpha <= 0) return;
        mask[y * 8 + x] = (byte)Math.Min(byte.MaxValue, alpha);
    }

    private static byte ToAlphaForHeight(long height) => (byte)Math.Min(byte.MaxValue, (byte)((byte.MaxValue * height + FixedOne / 2) >> 16));

    private static int TrapezoidToAlpha(long first, long second) => (int)(((first + second) / 2) >> 8);

    private static int PartialTriangleToAlpha(long width, long inverseSlope)
        => (int)((((width >> 11) * (width >> 11) * (inverseSlope >> 11)) >> 8) & byte.MaxValue);

    private static int GetPartialAlpha(int alpha, byte fullAlpha) => alpha * fullAlpha >> 8;

    private static long FixedMultiply(long left, long right) => checked(left * right >> 16);

    private static int FloorToInt(long value) => (int)(value >> 16);

    private static int CeilToInt(long value) => (int)((value + FixedOne - 1) >> 16);

    private static long FloorToFixed(long value) => value & ~(FixedOne - 1);

    private static long CeilToFixed(long value) => checked((long)CeilToInt(value) * FixedOne);

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

    private static (Point LeftTop, Point RightTop, Point LeftBottom, Point RightBottom)? SourceGuidedSnappedStrip(ReadOnlySpan<FixedPoint> triangle, long top, long bottom)
    {
        FixedTrapezoid? trapezoid = SourceGuidedFixedTrapezoid(triangle, top, bottom);
        return trapezoid is null ? null : new(
            new Point(trapezoid.Value.UpperLeft / 65_536d, top / 65_536d),
            new Point(trapezoid.Value.UpperRight / 65_536d, top / 65_536d),
            new Point(trapezoid.Value.LowerLeft / 65_536d, bottom / 65_536d),
            new Point(trapezoid.Value.LowerRight / 65_536d, bottom / 65_536d));
    }

    private static FixedTrapezoid? SourceGuidedFixedTrapezoid(ReadOnlySpan<FixedPoint> triangle, long top, long bottom)
    {
        long middle = top + (bottom - top) / 2;
        List<(FixedPoint Top, FixedPoint Bottom)> edges = [];
        for (int index = 0; index < triangle.Length; index++)
        {
            FixedPoint first = triangle[index];
            FixedPoint second = triangle[(index + 1) % triangle.Length];
            if ((first.Y < middle && middle < second.Y) || (second.Y < middle && middle < first.Y)) edges.Add((first, second));
        }
        if (edges.Count == 0) return null;
        Assert.Equal(2, edges.Count);
        long firstTop = SourceGuidedSnapEdgeX(edges[0].Top, edges[0].Bottom, top);
        long firstBottom = SourceGuidedSnapEdgeX(edges[0].Top, edges[0].Bottom, bottom);
        long secondTop = SourceGuidedSnapEdgeX(edges[1].Top, edges[1].Bottom, top);
        long secondBottom = SourceGuidedSnapEdgeX(edges[1].Top, edges[1].Bottom, bottom);
        return firstTop <= secondTop
            ? new FixedTrapezoid(firstTop, secondTop, firstBottom, secondBottom, SourceGuidedInverseSlope(edges[0].Top, edges[0].Bottom), SourceGuidedInverseSlope(edges[1].Top, edges[1].Bottom))
            : new FixedTrapezoid(secondTop, firstTop, secondBottom, firstBottom, SourceGuidedInverseSlope(edges[1].Top, edges[1].Bottom), SourceGuidedInverseSlope(edges[0].Top, edges[0].Bottom));
    }

    private static long SourceGuidedSnapEdgeX(FixedPoint first, FixedPoint second, long y)
    {
        if (first.Y > second.Y) (first, second) = (second, first);
        long slope = checked((second.X - first.X) << 16) / (second.Y - first.Y);
        long x = checked(first.X + ((slope * (y - first.Y)) >> 16));
        return (x + 2_048) & ~4_095L;
    }

    private static long SourceGuidedInverseSlope(FixedPoint first, FixedPoint second)
    {
        if (first.Y > second.Y) (first, second) = (second, first);
        long slope = checked((second.X - first.X) << 16) / (second.Y - first.Y);
        return slope == 0 ? int.MaxValue : Math.Abs(checked((65_536L << 16) / slope));
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
    private static readonly Point[] RedOverlapTriangle = [new(1.25, 1.125), new(7.25, 2.125), new(2.25, 7.125)];
    private static readonly Point[] BlueOverlapTriangle = [new(0.75, 2.375), new(6.75, 1.375), new(5.75, 7.375)];
    private static readonly Point[] RedHalfGridOverlapTriangle = [new(1.28125, 1.15625), new(7.28125, 2.15625), new(2.28125, 7.15625)];
    private static readonly Point[] BlueHalfGridOverlapTriangle = [new(0.78125, 2.40625), new(6.78125, 1.40625), new(5.78125, 7.40625)];

    private enum EdgeRounding { Floor, Nearest, Ceiling }

    private readonly record struct Point(double X, double Y);

    private readonly record struct FixedTrapezoid(long UpperLeft, long UpperRight, long LowerLeft, long LowerRight, long LeftInverseSlope, long RightInverseSlope);

    private readonly record struct FixedPoint(long X, long Y)
    {
        internal static FixedPoint From(Point point)
            => new(ToFixed(point.X), SnapY(ToFixed(point.Y)));

        private static long SnapY(long value) => ((value + 8_192) >> 14) << 14;
    }

    private static long ToFixed(double value) => checked((long)Math.Round(value * 65_536, MidpointRounding.AwayFromZero));

    private static byte ToByte(double value) => checked((byte)Math.Round(value * byte.MaxValue, MidpointRounding.AwayFromZero));
}
