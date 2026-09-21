using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGlyphCoverageTests
{
    [Fact]
    public void Reports_fixed_subdivision_work_for_each_coverage_sample()
    {
        SvgGlyphQuadraticContour contour = new([
            new SvgGlyphQuadraticSegment(new(0, 0), new(1, 0), new(1, 0)),
            new SvgGlyphQuadraticSegment(new(1, 0), new(1, 1), new(1, 1))
        ]);

        Assert.Equal(16, SvgGlyphCoverage.GetSampleWorkUnits([contour]));
    }

    [Fact]
    public void Samples_a_closed_quadratic_square_with_nonzero_winding()
    {
        SvgGlyphQuadraticContour contour = new([
            Line(0, 0, 2, 0), Line(2, 0, 2, 2), Line(2, 2, 0, 2), Line(0, 2, 0, 0)]);

        Assert.Equal(255, SvgGlyphCoverage.GetCoverage([contour], 0, 0, 4, CancellationToken.None));
        Assert.Equal(0, SvgGlyphCoverage.GetCoverage([contour], 2, 0, 4, CancellationToken.None));
    }

    [Fact]
    public void Uses_opposite_winding_to_leave_a_glyph_counter_transparent()
    {
        SvgGlyphQuadraticContour outer = new([
            Line(0, 0, 4, 0), Line(4, 0, 4, 4), Line(4, 4, 0, 4), Line(0, 4, 0, 0)]);
        SvgGlyphQuadraticContour inner = new([
            Line(1, 1, 1, 3), Line(1, 3, 3, 3), Line(3, 3, 3, 1), Line(3, 1, 1, 1)]);

        Assert.Equal(255, SvgGlyphCoverage.GetCoverage([outer, inner], 0, 0, 4, CancellationToken.None));
        Assert.Equal(0, SvgGlyphCoverage.GetCoverage([outer, inner], 1, 1, 4, CancellationToken.None));
    }

    [Fact]
    public void Rejects_finite_extreme_quadratic_coverage_when_winding_arithmetic_overflows()
    {
        SvgGlyphQuadraticContour contour = new([Line(-double.MaxValue, -double.MaxValue, double.MaxValue, double.MaxValue)]);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphCoverage.GetCoverage([contour], 0, 0, 1, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_finite_extreme_coverage_samples_that_overflow()
    {
        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphCoverage.GetCoverage([], double.MaxValue, 0, double.MaxValue, 1, 1, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    private static SvgGlyphQuadraticSegment Line(double x1, double y1, double x2, double y2) => new(new(x1, y1), new(x2, y2), new(x2, y2));
}
