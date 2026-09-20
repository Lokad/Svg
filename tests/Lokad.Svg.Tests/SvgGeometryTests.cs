using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgGeometryTests
{
    [Fact]
    public void Retains_typed_glyph_contours_independently_from_rectangle_data()
    {
        SvgGlyphQuadraticContour[] contours = [new([new SvgGlyphQuadraticSegment(new(0, 0), new(1, 0), new(1, 0))])];
        var glyph = new SvgGlyphGeometry(contours, new SvgColor(1, 2, 3, 255));

        SvgGeometry geometry = SvgGeometry.FromGlyphContours(glyph);

        Assert.Equal(SvgGeometryKind.GlyphContours, geometry.Kind);
        Assert.Same(contours, geometry.Glyph.Contours);
        Assert.Equal(new SvgColor(1, 2, 3, 255), geometry.Glyph.Color);
    }
}
