using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> NonpaintingMaskTargets()
    {
        foreach (string mode in new[] { "zero-width", "zero-height", "zero-circle", "zero-ellipse", "empty-path", "hidden", "zero-filtered-rect", "zero-filtered-circle", "empty-filtered-path", "missing-image", "zero-image" })
        foreach (bool group in new[] { false, true })
        foreach (string type in new[] { "alpha", "luminance" })
        foreach (string consumer in new[] { "direct", "use", "symbol", "pattern" })
            yield return [mode, group, type, consumer];
    }

    [Theory]
    [MemberData(nameof(NonpaintingMaskTargets))]
    public async Task Nonpainting_primitive_targets_do_not_lower_or_acquire_mask_fonts(string mode, bool group, string type, string consumer)
    {
        const string text = "<text x='2' y='14' font-family='Unavailable' font-size='200' fill='white'>A</text>";
        string resources = group ? "" : text.Replace("<text ", "<text id='glyph' ", StringComparison.Ordinal);
        resources += "<mask id='m' mask-type='" + type + "' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + (group ? "<g>" + text + "</g>" : "<use href='#glyph'/>") + "</mask>";
        resources += "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        string image = Convert.ToBase64String(Lokad.Svg.Encoding.PngEncoder.Encode(1, 1, new byte[] { 255, 0, 0, 255 }, CancellationToken.None));
        string target = mode switch
        {
            "zero-width" => "<rect width='0' height='32' fill='red' mask='url(#m)'/>",
            "zero-height" => "<rect width='32' height='0' fill='red' mask='url(#m)'/>",
            "zero-circle" => "<circle r='0' fill='red' mask='url(#m)'/>",
            "zero-ellipse" => "<ellipse rx='16' ry='0' fill='red' mask='url(#m)'/>",
            "empty-path" => "<path d='' fill='red' mask='url(#m)'/>",
            "zero-filtered-rect" => "<rect width='0' height='32' filter='url(#f)' mask='url(#m)'/>",
            "zero-filtered-circle" => "<circle r='0' filter='url(#f)' mask='url(#m)'/>",
            "empty-filtered-path" => "<path d='' filter='url(#f)' mask='url(#m)'/>",
            "missing-image" => "<image width='32' height='32' mask='url(#m)'/>",
            "zero-image" => "<image width='0' height='32' href='data:image/png;base64," + image + "' mask='url(#m)'/>",
            _ => "<rect width='32' height='32' fill='red' visibility='hidden' mask='url(#m)'/>",
        };
        string body = target;
        if (consumer == "use") { resources += target.Insert(target.IndexOf(' '), " id='target'"); body = "<use href='#target'/>"; }
        if (consumer == "symbol") { resources += "<symbol id='s' viewBox='0 0 32 32'>" + target + "</symbol>"; body = "<use href='#s'/>"; }
        if (consumer == "pattern") { resources += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + target + "</pattern>"; body = "<rect width='32' height='32' fill='url(#p)'/>"; }
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + body + "<defs>" + resources + "</defs></svg>"), unchanged = input.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(prefix + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("pattern", false)]
    [InlineData("direct", true)]
    [InlineData("pattern", true)]
    public async Task Disabled_masked_groups_never_lower_the_mask_even_with_visible_children_or_filters(string consumer, bool filtered)
    {
        const string mask = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g><text font-family='Unavailable' font-size='200' x='2' y='14' fill='white'>A</text></g></mask>";
        const string filter = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        string group = "<g display='none' mask='url(#m)'" + (filtered ? " filter='url(#f)'" : "") + "><rect width='32' height='32' fill='red' display='inline'/></g>";
        string definitions = mask + filter, body = group;
        if (consumer == "pattern") { definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + group + "</pattern>"; body = "<rect width='32' height='32' fill='url(#p)'/>"; }
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + body + "<defs>" + definitions + "</defs></svg>");
        byte[] expected = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(prefix + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("direct", false)]
    [InlineData("alias", false)]
    [InlineData("symbol", false)]
    [InlineData("pattern", false)]
    [InlineData("direct", true)]
    [InlineData("alias", true)]
    [InlineData("symbol", true)]
    [InlineData("pattern", true)]
    public async Task Disabled_use_instances_never_materialize_their_own_mask(string consumer, bool filtered)
    {
        const string resources = "<rect id='r' width='32' height='32' fill='red'/><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g><text font-family='Unavailable' font-size='200' x='2' y='14' fill='white'>A</text></g></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        string instance = "<use href='#r' display='none' mask='url(#m)'" + (filtered ? " filter='url(#f)'" : "") + "/>";
        string definitions = resources, body = instance;
        if (consumer == "alias") { definitions += "<g id='a'>" + instance + "</g>"; body = "<use href='#a'/>"; }
        if (consumer == "symbol") { definitions += "<symbol id='s'>" + instance + "</symbol>"; body = "<use href='#s'/>"; }
        if (consumer == "pattern") { definitions += "<pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + instance + "</pattern>"; body = "<rect width='32' height='32' fill='url(#p)'/>"; }
        const string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(prefix + body + "<defs>" + definitions + "</defs></svg>");
        byte[] expected = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(prefix + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("vertical")]
    [InlineData("path")]
    [InlineData("polyline")]
    [InlineData("group-stroke")]
    [InlineData("group-visible-child")]
    [InlineData("use-visible-child")]
    [InlineData("group-filter")]
    [InlineData("hidden-filtered-rect")]
    [InlineData("empty-filtered-use")]
    public async Task Painted_strokes_visibility_overrides_and_filter_output_keep_mask_font_dependencies(string kind)
    {
        const string rectangle = "<rect width='32' height='32' fill='blue' visibility='visible'/>";
        const string line = "<line x1='0' y1='12' x2='32' y2='12' stroke='blue' stroke-width='8'";
        string body = kind switch
        {
            "vertical" => "<line x1='4' y1='0' x2='4' y2='32' stroke='blue' stroke-width='8' mask='url(#m)'/>",
            "path" => "<path d='M0 12H32' stroke='blue' stroke-width='8' mask='url(#m)'/>",
            "polyline" => "<polyline points='0,12 32,12' stroke='blue' stroke-width='8' mask='url(#m)'/>",
            "group-stroke" => "<g mask='url(#m)'>" + line + "/></g>",
            "group-visible-child" => "<g visibility='hidden' mask='url(#m)'>" + rectangle + "</g>",
            "use-visible-child" => "<defs><g id='s'>" + rectangle + "</g></defs><use href='#s' visibility='hidden' mask='url(#m)'/>",
            "group-filter" => "<g filter='url(#f)' mask='url(#m)'><rect width='32' height='32' visibility='hidden'/></g>",
            "hidden-filtered-rect" => "<rect width='32' height='32' visibility='hidden' filter='url(#f)' mask='url(#m)'/>",
            "empty-filtered-use" => "<defs><g id='s'/></defs><use href='#s' filter='url(#f)' mask='url(#m)'/>",
            _ => line + " mask='url(#m)'/>",
        };
        string resources = "<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><g><text font-family='Example' font-size='200' x='2' y='14' fill='white'>A</text></g></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resources + "</defs>" + body + "</svg>");
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, new CountingFontProvider(new CountingFontSource(CreateTriangleFont())), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(4_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, budget, CancellationToken.None))
            Assert.Contains(surface.Pixels, value => value != 0);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<rect width='0' height='bad' mask='url(#m)'/>")]
    [InlineData("<circle r='0' cx='bad' mask='url(#m)'/>")]
    [InlineData("<rect width='0' height='32' mask='elsewhere.svg#m'/>")]
    public async Task Nonpainting_targets_still_validate_geometry_and_mask_syntax_before_font_io(string target)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><mask id='m'><g><text font-family='Unavailable'>A</text></g></mask></defs>" + target + "</svg>");
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public void Nonpainting_target_discovery_and_lowering_release_borrowed_storage_on_denial_and_retry()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><rect width='0' height='32' mask='url(#m)'/><defs><mask id='m'><g><text font-family='Unavailable'>" + new string('A', 8192) + "</text></g></mask></defs></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, CancellationToken.None));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes * 3 / 4));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, SvgRenderLimits.Default, budget, cancelled.Token)).CancellationToken);
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.All(surface.Pixels, value => Assert.Equal(0, value));
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
