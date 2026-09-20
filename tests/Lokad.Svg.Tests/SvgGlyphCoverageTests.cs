using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGlyphCoverageTests
{
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

    private static SvgGlyphQuadraticSegment Line(double x1, double y1, double x2, double y2) => new(new(x1, y1), new(x2, y2), new(x2, y2));
}
