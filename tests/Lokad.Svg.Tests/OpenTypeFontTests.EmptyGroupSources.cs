using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> GroupMaskSourceCases()
    {
        foreach (string mode in new[] { "balanced", "title", "desc", "metadata", "empty-child", "nested-empty", "empty-reference", "zero-rect", "zero-circle", "empty-path", "missing-image", "zero-viewport", "hidden-rect", "child-filter", "zero-child-filter", "parent-filter", "visible-child", "stroke", "own-filter" })
        foreach (bool forward in new[] { false, true })
        foreach (bool group in new[] { false, true })
        foreach (bool alpha in new[] { false, true })
        foreach (bool pattern in new[] { false, true })
            yield return new object[] { mode, forward, group, alpha, pattern };
    }

    [Theory]
    [MemberData(nameof(GroupMaskSourceCases))]
    public async Task Group_masks_require_source_paint_or_the_groups_own_filter(string mode, bool forward, bool group, bool alpha, bool pattern)
    {
        bool live = mode is "visible-child" or "stroke" or "own-filter";
        string family = live ? "Example" : "Unavailable";
        const string rectangle = "<rect width='32' height='32' fill='blue'/>";
        const string stroke = "<path d='M0 12H32' stroke='blue' stroke-width='8'/>";
        string child = mode switch
        {
            "title" => "<title>inert</title>",
            "desc" => "<desc>inert</desc>",
            "metadata" => "<metadata>inert</metadata>",
            "empty-child" => "<g/>",
            "nested-empty" => "<g><g/></g>",
            "empty-reference" => "<use href='#empty'/>",
            "zero-rect" => "<rect width='0' height='32'/>",
            "zero-circle" => "<circle r='0'/>",
            "empty-path" => "<path d=''/>",
            "missing-image" => "<image width='32' height='32'/>",
            "zero-viewport" => "<use href='#s' width='0'/>",
            "hidden-rect" => "<rect width='32' height='32' visibility='hidden'/>",
            "child-filter" => "<g filter='url(#f)'/>",
            "zero-child-filter" => "<rect width='0' height='32' filter='url(#f)'/>",
            "visible-child" => rectangle.Replace("/>", " visibility='visible'/>", StringComparison.Ordinal),
            "stroke" => stroke,
            _ => ""
        };
        string text = "<text font-family='" + family + "' font-size='200' x='2' y='14' fill='white'>A</text>";
        string definitions = "<g id='empty'/><symbol id='s' viewBox='0 0 32 32'>" + rectangle + "</symbol>";
        if (!group) definitions += text.Replace("<text ", "<text id='t' ", StringComparison.Ordinal);
        definitions += "<mask id='m' mask-type='" + (alpha ? "alpha" : "luminance") + "' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + (group ? "<g>" + text + "</g>" : "<use href='#t'/>") + "</mask>";
        definitions += "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        string attributes = mode == "own-filter" ? " filter='url(#f)'" : mode == "visible-child" ? " visibility='hidden'" : "";
        string body = "<g mask='url(#m)'" + attributes + ">" + child + "</g>";
        if (mode == "parent-filter") body = "<g filter='url(#f)'>" + body + "</g>";
        if (pattern)
        {
            definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + body + "</pattern>";
            body = "<rect width='32' height='32' fill='url(#p)'/>";
        }
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        definitions = "<defs>" + definitions + "</defs>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + (forward ? body + definitions : definitions + body) + "</svg>"), unchanged = input.ToArray();
        string control = mode == "parent-filter" ? rectangle : live ? (mode == "stroke" ? stroke : rectangle).Replace("/>", " mask='url(#m)'/>", StringComparison.Ordinal) : "";
        byte[] expectedInput = System.Text.Encoding.UTF8.GetBytes(prefix + definitions + control + "</svg>");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgRenderOptions options = SvgRenderOptions.Default;
        using SvgFontSnapshot? fonts = live ? await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, new CountingFontProvider(new CountingFontSource(CreateTriangleFont())), CancellationToken.None) : null;
        if (fonts is not null) options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(expectedInput, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        IReadOnlyList<SvgFontFaceRequest> requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);
        if (live) Assert.Equal("Example", Assert.Single(requests).Family);
        else Assert.Empty(requests);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(live ? 1 : 0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("<g mask='url(#missing)'></g>")]
    [InlineData("<g mask='url(#m)'><rect width='0' height='bad'/></g>")]
    [InlineData("<g mask='elsewhere.svg#m'><title>inert</title></g>")]
    [InlineData("<g mask='url(#m)'><g filter='url(#missing)'/></g>")]
    public async Task Inactive_group_masks_preserve_source_and_reference_errors_before_font_io(string target)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + target + "<defs><mask id='m'><g><text font-family='Unavailable'>A</text></g></mask></defs></svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public void Group_mask_source_discovery_and_replay_release_borrowed_storage_on_denial_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + string.Concat(Enumerable.Repeat("<g mask='url(#m)'><g><use href='#empty'/></g></g>", 256)) + "<defs><g id='empty'/><mask id='m'><g><text font-family='Unavailable'>" + new string('A', 8192) + "</text></g></mask></defs></svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 74));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, tight, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.All(surface.Pixels, value => Assert.Equal(0, value));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, canceled.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)) { }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
