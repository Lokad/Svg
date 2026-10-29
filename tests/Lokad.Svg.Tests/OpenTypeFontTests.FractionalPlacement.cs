using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(10, 8.53125, 119)]
    [InlineData(-10, -8.53125, 102)]
    public async Task Absolute_chunk_resets_precede_scaled_fragment_origin_truncation(double originX, double expectedDeviceOrigin, double expectedStrike)
    {
        // Same native Mono metrics/viewBox scale as the unchanged SVG Translate
        // document. Both lines share an authored x; glyph 12 must retain its
        // quarter-pixel strike after a second absolute x reset. Native paint
        // origins truncate toward zero on either side of the viewport.
        byte[] bytes = CreateFont(1000, 3, 2, 1);
        WriteUInt16(bytes, FindTableOffset(bytes, 0x686D7478) + 4, 600);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        const double scale = 512d / 600;
        SvgTextPositionAdjustment[] positions = [new(13, originX, null, 0, 40)];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(snapshot.Fonts[0], new string('A', 26), 18, originX, 80, 0, [], positions,
            SvgTextAnchor.Start, new(scale, SvgGlyphMetricTarget.Windows, false), budget, string.Empty, CancellationToken.None))
        {
            Assert.Equal(expectedDeviceOrigin, glyphs.Glyphs[0].X * scale, 10);
            Assert.Equal(glyphs.Glyphs[0].X, glyphs.Glyphs[13].X, 10);
            Assert.Equal(glyphs.Glyphs[12].X, glyphs.Glyphs[25].X, 10);
            foreach (int index in new[] { 12, 25 })
                Assert.Equal(expectedStrike, MathF.Floor((float)(glyphs.Glyphs[index].X * scale) * 4 + .5f) / 4);
            Assert.Equal(80, glyphs.Glyphs[0].Y);
            Assert.Equal(120, glyphs.Glyphs[13].Y);
        }
        Assert.Equal(new SvgTextPositionAdjustment(13, originX, null, 0, 40), positions[0]);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false, 329.1015625, 411.5)]
    [InlineData(true, 329.1875, 411.5)]
    public async Task Shaped_offsets_keep_independent_linear_metrics_before_scaled_strike_placement(bool textPath, double expectedX, double expectedStrike)
    {
        // Independent 1229/2048-unit metrics: glyph 22 is at 329.1015625.
        // At scale 1.25 its strike rounds to 411.5; premature 26.6 flooring
        // instead selects 411.25 and displaces a complete glyph's pixels.
        // Text paths retain their separately qualified character-advance grid.
        byte[] bytes = CreateFont(2048, 3, 2, 1);
        int hmtx = FindTableOffset(bytes, 0x686D7478);
        WriteUInt16(bytes, hmtx + 4, 1229);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(snapshot.Fonts[0], new string('A', 23), 24, 12.25, 32, 0, [], [], SvgTextAnchor.Start,
            new(1, SvgGlyphMetricTarget.Windows, textPath), budget, string.Empty, CancellationToken.None))
        {
            Assert.Equal(23, glyphs.Glyphs.Length);
            Assert.Equal(expectedX, glyphs.Glyphs[22].X);
            double strike = MathF.Floor((float)(glyphs.Glyphs[22].X * 1.25) * 4 + .5f) / 4;
            Assert.Equal(expectedStrike, strike);
            Assert.Equal(12.25, glyphs.Glyphs[0].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    public async Task Path_items_invert_the_admitted_device_scale_without_scalar_drift(string target)
    {
        byte[] bytes = CreateFont(1000, 3, 2, 1);
        WriteUInt16(bytes, FindTableOffset(bytes, 0x686D7478) + 4, 600);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        const double scale = 512d / 600;
        // A 600/1000 advance at this viewport scale occupies 9.21875 physical
        // pixels on the path grid. Inverting a rounded scalar scale accumulates drift.
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(snapshot.Fonts[0], new string('A', 27), 18, 0, 0, 0, 0, true,
            [], [], [], [], SvgTextAnchor.Start, new(scale, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, true),
            budget, string.Empty, CancellationToken.None))
        {
            Assert.Equal(27, glyphs.PaintFragments.Length);
            for (int index = 0; index < glyphs.Glyphs.Length; index++)
                Assert.Equal(index * 9.21875, glyphs.Glyphs[index].X * scale, 10);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

}
