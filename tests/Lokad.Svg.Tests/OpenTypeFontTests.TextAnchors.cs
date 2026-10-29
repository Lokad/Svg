using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(0, 1, 210, 290, 380)]
    [InlineData(1, 1, 95, 175, 265)]
    [InlineData(2, 1, -20, 60, 150)]
    [InlineData(0, -1, 190, 230, 260)]
    [InlineData(1, -1, 125, 165, 195)]
    [InlineData(2, -1, 60, 100, 130)]
    public async Task Text_anchor_extents_include_internal_relative_advances(int anchor, int direction, double first, double second, double third)
    {
        // Three 60-unit advances. The initial dx moves the anchor origin;
        // subsequent dx values extend or contract the chunk's 180-unit advance.
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_024));
        using (SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 100, 200, 3, 0, [],
            [new(0, direction * 10, 0), new(1, direction * 20, 0), new(2, direction * 30, 0)], (SvgTextAnchor)anchor, budget, string.Empty, CancellationToken.None))
        {
            Assert.Equal(first, positions.Glyphs[0].X);
            Assert.Equal(second, positions.Glyphs[1].X);
            Assert.Equal(third, positions.Glyphs[2].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0, 390, 200, 260)]
    [InlineData(1, 265, 75, 135)]
    [InlineData(2, 140, -50, 10)]
    public async Task Text_anchors_cover_both_advance_edges_when_relative_positions_backtrack(int anchor, double first, double second, double third)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 100, 200, 3, 0, [],
            [new(1, -250, 0)], (SvgTextAnchor)anchor, new RenderBudget(new HostMemoryBudget(1_024)), string.Empty, CancellationToken.None);
        Assert.Equal(first, positions.Glyphs[0].X);
        Assert.Equal(second, positions.Glyphs[1].X);
        Assert.Equal(third, positions.Glyphs[2].X);
    }
}
