using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

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
    public void Sizes_scene_lists_from_their_element_type()
    {
        RenderBudget geometryBudget = new(long.MaxValue);
        using (BudgetedSceneList<SvgGeometry> geometries = new(geometryBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            for (int index = 0; index < 4; index++) geometries.Add(SvgGeometry.FromRectangle(new SvgRectangle(0, 0, 1, 1, SvgColor.Transparent, false, null)));
            Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4), geometryBudget.Snapshot().PeakBytes);
        }
        RenderBudget commandBudget = new(long.MaxValue);
        using (BudgetedSceneList<SvgDrawCommand> commands = new(commandBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            for (int index = 0; index < 4; index++) commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, index, 255));
            Assert.Equal(ParserCollectionMemory.GetArrayByteCount<SvgDrawCommand>(4), commandBudget.Snapshot().PeakBytes);
        }
    }
}
