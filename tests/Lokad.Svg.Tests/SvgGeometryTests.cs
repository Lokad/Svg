using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Tests;

public sealed class SvgGeometryTests
{
    [Fact]
    public void Rejects_default_and_cross_variant_payload_access()
    {
        SvgGeometry invalid = default;
        SvgGeometry rectangle = SvgGeometry.FromRectangle(new SvgRectangle(0, 0, 1, 1, SvgColor.Transparent, false, null));

        Assert.Equal(SvgGeometryKind.Invalid, invalid.Kind);
        Assert.Throws<InvalidOperationException>(() => _ = invalid.Rectangle);
        Assert.Throws<InvalidOperationException>(() => _ = invalid.AxisAlignedLine);
        Assert.Throws<InvalidOperationException>(() => _ = invalid.Glyph);
        Assert.Throws<InvalidOperationException>(() => _ = rectangle.AxisAlignedLine);
        Assert.Throws<InvalidOperationException>(() => _ = rectangle.Glyph);
    }

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

    [Fact]
    public void Charges_scene_arrays_using_their_runtime_value_sizes()
    {
        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4), SvgSceneMemory.GetArrayByteCount(SvgSceneArrayKind.Geometry, 4));
        Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4), SvgSceneMemory.GetArrayByteCount(SvgSceneArrayKind.DrawCommand, 4));
    }
}
