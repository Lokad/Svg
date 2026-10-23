using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> EmptyGroupMaskCases()
    {
        foreach (bool forward in new[] { false, true })
        foreach (bool group in new[] { false, true })
        foreach (bool parentFilter in new[] { false, true })
        foreach (string type in new[] { "alpha", "luminance" })
        foreach (string container in new[] { "g", "a", "switch" })
        foreach (bool pattern in new[] { false, true })
            if (!pattern || container == "g")
                yield return new object[] { forward, group, parentFilter, type, container, pattern };
    }

    [Theory]
    [MemberData(nameof(EmptyGroupMaskCases))]
    public async Task Self_closing_group_masks_resolve_without_fonts_in_either_source_order(bool forward, bool group, bool parentFilter, string type, string container, bool pattern)
    {
        const string text = "<text font-family='Unavailable' font-size='200' x='2' y='14' fill='white'>A</text>";
        string definitions = group ? "" : text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal);
        definitions += "<mask id='m' mask-type='" + type + "' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + (group ? "<g>" + text + "</g>" : "<use href='#t'/>") + "</mask>";
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        string empty = "<" + container + " mask='url(#m)'/>";
        string body = parentFilter ? "<g filter='url(#f)'>" + empty + "</g>" : empty + "<rect width='8' height='8' fill='blue'/>";
        if (pattern)
        {
            definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + body + "</pattern>";
            body = "<rect width='32' height='32' fill='url(#p)'/>";
        }
        definitions = "<defs>" + definitions + filter + "</defs>";
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? body + definitions : definitions + body) + "</svg>"), unchanged = input.ToArray();
        byte[] control = System.Text.Encoding.UTF8.GetBytes(prefix + (parentFilter ? "<rect width='32' height='32' fill='blue'/>" : "<rect width='8' height='8' fill='blue'/>") + "</svg>");
        byte[] expected = SvgConverter.ConvertToPng(control, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("g", "url(#missing)")]
    [InlineData("a", "url(#missing)")]
    [InlineData("switch", "url(#missing)")]
    [InlineData("g", "url(#wrong-kind)")]
    [InlineData("g", "elsewhere.svg#m")]
    public async Task Empty_group_masks_validate_references_before_opening_font_programs(string container, string mask)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><" + container + " mask='" + mask + "'/><defs><g id='wrong-kind'/><mask id='m'><g><text font-family='Unavailable'>A</text></g></mask></defs></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Empty_group_own_filters_preserve_live_mask_fonts(bool forward, bool balanced)
    {
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        const string definitions = "<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g><text font-family='Example' font-size='200' x='2' y='14' fill='white'>A</text></g></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs>";
        string body = "<g mask='url(#m)' filter='url(#f)'" + (balanced ? "></g>" : "/>");
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? body + definitions : definitions + body) + "</svg>");
        byte[] control = System.Text.Encoding.UTF8.GetBytes(prefix + definitions + "<rect width='32' height='32' fill='blue' mask='url(#m)'/></svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, new CountingFontProvider(new CountingFontSource(CreateTriangleFont())), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Fact]
    public void Empty_group_mask_validation_releases_storage_on_denial_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + string.Concat(Enumerable.Repeat("<g mask='url(#m)'/>", 256)) + "<defs><mask id='m'><g><text font-family='Unavailable'>A</text></g></mask></defs></svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new(4_000_000);
        ParserWorkCounter work = new(1_000_000);
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, work, CancellationToken.None))
                Assert.Empty(scene.Geometries);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new(budget.Snapshot().PeakBytes - 74);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, tight, null, new(1_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(work.Used - 1), CancellationToken.None)).ErrorCode);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, new(1_000_000), canceled.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
