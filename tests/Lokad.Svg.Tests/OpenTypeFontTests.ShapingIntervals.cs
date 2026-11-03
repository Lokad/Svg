using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData((int)SvgTextAnchor.Middle)]
    [InlineData((int)SvgTextAnchor.End)]
    public async Task Anchor_only_intervals_preserve_continuous_ligature_shaping(int anchor)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgInheritedTextStyle start = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] spans = [new(0, 1, start), new(1, 2, start.WithAnchor((SvgTextAnchor)anchor)), new(2, 3, start)];
        SvgTextStyle style = new(new("Example", 400, false), 20, 10, 40, 0, 0, 0, SvgTextAnchor.Start, "ligature");
        using SvgTextRun run = SvgTextRunBuilder.Create(style, "AAA", [], [], [], spans, null, null, fonts, new RenderBudget(), CancellationToken.None);
        SvgPositionedGlyph glyph = Assert.Single(run.Glyphs.ToArray());
        Assert.Equal((ushort)2, glyph.GlyphIndex);
        Assert.Equal(10, glyph.X);
        Assert.Equal(SvgTextAnchor.Start, run.Style.Anchor);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public async Task Effective_font_size_changes_still_end_shaping_intervals()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgInheritedTextStyle start = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgInheritedTextStyle larger = new("Example", 400, false, 21, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] spans = [new(0, 1, start), new(1, 2, larger), new(2, 3, start)];
        SvgTextStyle style = new(new("Example", 400, false), 20, 10, 40, 0, 0, 0, SvgTextAnchor.Start, "size");
        using SvgTextRun run = SvgTextRunBuilder.Create(style, "AAA", [], [], [], spans, null, null, fonts, new RenderBudget(), CancellationToken.None);
        Assert.Equal(3, run.Glyphs.Length);
        Assert.All(run.Glyphs.ToArray(), glyph => Assert.Equal((ushort)1, glyph.GlyphIndex));
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Coalesced_font_intervals_release_ligature_staging_after_refusal_and_retry()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgInheritedTextStyle start = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] spans = [new(0, 1, start), new(1, 3, start.WithAnchor(SvgTextAnchor.End))];
        SvgTextStyle style = new(new("Example", 400, false), 20, 10, 40, 0, 0, 0, SvgTextAnchor.Start, "retry");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextRun run = Create(budget, fonts, style, spans)) Assert.Single(run.Glyphs.ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(budget.Snapshot().PeakBytes - 1);
        RenderBudget limited = new(host);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Create(limited, fonts, style, spans)).ErrorCode);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);
        host.Limit = budget.Snapshot().PeakBytes;
        using (SvgTextRun retry = Create(limited, fonts, style, spans)) Assert.Single(retry.Glyphs.ToArray());
        Assert.Equal(0, limited.Snapshot().CurrentBytes);

        static SvgTextRun Create(RenderBudget allowance, SvgFontSnapshot fonts, SvgTextStyle style, SvgTextFontSpan[] spans)
            => SvgTextRunBuilder.Create(style, "AAA", [], [], [], spans, null, null, fonts, allowance, CancellationToken.None);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("empty")]
    [InlineData("surrogate")]
    [InlineData("overlap")]
    [InlineData("gap")]
    public async Task Font_interval_coalescing_preserves_original_boundary_validation(string boundary)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgInheritedTextStyle font = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        string text = boundary == "surrogate" ? "\U0001F600" : "AAA";
        SvgTextFontSpan[] spans = boundary switch
        {
            "empty" => [new(0, 1, font), new(1, 1, font), new(1, 3, font)],
            "surrogate" => [new(0, 1, font), new(1, 2, font)],
            "overlap" => [new(0, 2, font), new(1, 3, font)],
            _ => [new(0, 1, font), new(2, 3, font)]
        };
        SvgTextStyle style = new(new("Example", 400, false), 20, 10, 40, 0, 0, 0, SvgTextAnchor.Start, "boundary");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        Assert.Throws<ArgumentException>(() => SvgTextRunBuilder.Create(style, text, [], [], [], spans, null, null, fonts, budget, CancellationToken.None));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
