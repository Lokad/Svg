using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Scoped_anchor_positions_release_owned_staging_after_refusal_and_retry()
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgInheritedTextStyle start = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] spans = [new(0, 1, start), new(1, 4, start.WithAnchor(SvgTextAnchor.End))];
        SvgTextPositionAdjustment[] positions = [new(1, 120, null, 0, 0)];
        SvgTextStyle style = new(new("Example", 400, false), 20, 10, 40, 0, 0, 0, SvgTextAnchor.Start, "anchor");
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextRun run = Create(budget))
            Assert.Equal(new[] { 10d, 84, 96, 108 }, run.Glyphs.ToArray().Select(glyph => glyph.X));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        HostMemoryBudget host = new(budget.Snapshot().PeakBytes - 1);
        RenderBudget limited = new(host);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Create(limited)).ErrorCode);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);
        host.Limit = budget.Snapshot().PeakBytes;
        using (SvgTextRun retry = Create(limited)) Assert.Equal(4, retry.Glyphs.Length);
        Assert.Equal(0, limited.Snapshot().CurrentBytes);

        SvgTextRun Create(RenderBudget allowance)
            => SvgTextRunBuilder.Create(style, "AAAA", [], positions, [], spans, null, null, fonts, allowance, CancellationToken.None);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Css_anchor_validation_precedes_acquisition_and_prepared_replay_keeps_chunk_scope()
    {
        (var catalog, var provider, var source) = CreateExamplePack(CreateTriangleFont());
        byte[] invalid = "<svg xmlns='http://www.w3.org/2000/svg'><g style='text-anchor:center'><text font-family='Example'>A</text></g></svg>"u8.ToArray();
        Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(invalid, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        })).ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='48'><g style='text-anchor:end'><text x='60' y='35' font-family='Example' font-size='20'>AA<tspan x='120' style='text-anchor:middle'>AAA</tspan></text></g></svg>"u8.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] expected = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absolute_chunks_use_the_current_span_anchor_from_child_or_ancestor_positions(bool ancestorPositions)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgInheritedTextStyle start = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgInheritedTextStyle end = start.WithAnchor(SvgTextAnchor.End);
        SvgTextFontSpan[] spans = [new(0, 1, start), new(1, 2, end), new(2, 3, start)];
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "anchor");
        if (ancestorPositions) style = style with { PositionLists = new("0 100 200", null, null, null, null) };
        SvgTextPositionAdjustment[] positions = ancestorPositions ? [] : [new(1, 100, null, 0, 0), new(2, 200, null, 0, 0)];
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "AAA", [], positions, [], spans, null, null, fonts, budget, CancellationToken.None))
            Assert.Equal(new[] { 0d, 88, 200 }, run.Glyphs.ToArray().Select(glyph => glyph.X));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anchor_only_scopes_keep_the_first_character_anchor_for_the_complete_chunk(bool startsInsideSpan)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgInheritedTextStyle start = new("Example", 400, false, 20, SvgTextWhitespaceMode.Default, 0, SvgTextAnchor.Start);
        SvgTextFontSpan[] spans = startsInsideSpan
            ? [new(0, 1, start.WithAnchor(SvgTextAnchor.End)), new(1, 3, start)]
            : [new(0, 1, start), new(1, 2, start.WithAnchor(SvgTextAnchor.End)), new(2, 3, start)];
        SvgTextStyle style = new(new("Example", 400, false), 20, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "anchor");
        using SvgTextRun run = SvgTextRunBuilder.Create(style, "AAA", [], [], [], spans, null, null, fonts,
            new RenderBudget(new HostMemoryBudget(1_000_000)), CancellationToken.None);
        Assert.Equal(startsInsideSpan ? new[] { -36d, -24, -12 } : [0d, 12, 24], run.Glyphs.ToArray().Select(glyph => glyph.X));
    }
}
