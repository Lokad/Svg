using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> MaskResourceDisplayCases()
    {
        foreach (string content in new[] { "rect", "circle", "path", "union", "use", "empty" })
        foreach (string target in new[] { "rect", "path", "group", "use", "symbol", "pattern", "text" })
        foreach (bool ancestor in new[] { false, true }) yield return [content, target, ancestor];
    }

    [Theory]
    [MemberData(nameof(MaskResourceDisplayCases))]
    public async Task Mask_display_distinguishes_disabled_resources_from_definition_ancestors(string kind, string target, bool ancestor)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot? fonts = target == "text" ? await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None) : null;
        foreach (bool forward in new[] { false, true })
        foreach (bool alpha in new[] { false, true })
        foreach (string declaration in new[] { "style='display:none'", "class='hidden'" })
        foreach (SvgColor background in target == "text" ? new[] { SvgColor.Transparent, new SvgColor(255, 255, 255, 255) } : new[] { SvgColor.Transparent })
        {
            SvgRenderOptions options = new(null, 1, background, SvgRenderLimits.Default, fonts);
            byte[] expected = SvgConverter.ConvertToPng(Input(false), options, CancellationToken.None).ToArray();
            byte[] input = Input(true);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, new(null, 1, background, SvgRenderLimits.Default), CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Input(bool disabled)
            {
                string content = kind switch
                {
                    "rect" => "<rect width='4' height='8' fill='white'/>",
                    "circle" => "<circle cx='2' cy='4' r='2' fill='white'/>",
                    "path" => "<path d='M0 0H4V8H0Z' fill='white'/>",
                    "union" => "<path d='M0 0H4V4H0Z' fill='white'/><rect y='4' width='4' height='4' fill='white'/>",
                    "use" => "<use href='#maskSource'/>",
                    _ => ""
                };
                string mask = "<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='4' height='8' " + (alpha ? "mask-type='alpha' " : "")
                    + (disabled && !ancestor ? declaration : "") + ">" + content + "</mask></defs>";
                if (disabled && ancestor) mask = "<g " + declaration + ">" + mask + "</g>";
                string binding = disabled || ancestor ? " mask='url(#m)'" : "";
                string drawing = target switch
                {
                    "path" => "<path d='M0 0H8V8H0Z' fill='red'" + binding + "/>",
                    "group" => "<g" + binding + "><rect width='8' height='8' fill='red'/></g>",
                    "use" => "<defs><path id='s' d='M0 0H8V8H0Z' fill='red'/></defs><use href='#s'" + binding + "/>",
                    "symbol" => "<defs><symbol id='s'><rect width='8' height='8' fill='red'" + binding + "/></symbol></defs><use href='#s'/>",
                    "pattern" => "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='red'" + binding + "/></pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
                    "text" => "<text x='.25' y='7' font-size='200' font-family='Example' fill='red'" + binding + ">A</text>",
                    _ => "<rect width='8' height='8' fill='red'" + binding + "/>"
                };
                return Document("<style>.hidden{display:none}</style><defs><path id='maskSource' d='M0 0H4V8H0Z' fill='white'/></defs>" + (forward ? drawing + mask : mask + drawing));
            }
        }
        if (target != "text") Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_mask_regions_do_not_crop_filtered_output_or_ancestor_effects(bool forward)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        const string mask = "<defs><mask id='m' style='display:none' maskUnits='userSpaceOnUse' x='2' y='2' width='1' height='1'><rect width='8' height='8' fill='white'/></mask></defs>";
        const string effects = "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='8' height='8'><feFlood flood-color='red'/></filter><clipPath id='c'><rect width='7' height='8'/></clipPath></defs>";
        const string drawing = "<g opacity='.5' clip-path='url(#c)'><path d='M0 0H8V8H0Z' fill='blue' filter='url(#f)' mask='url(#m)'/></g>";
        byte[] input = Document(effects + (forward ? drawing + mask : mask + drawing));
        byte[] expected = Render(Document(effects + drawing.Replace(" mask='url(#m)'", "", StringComparison.Ordinal)));
        Assert.Equal(expected, Render(input));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_group_masks_preserve_sibling_glyph_paint_and_real_opacity(bool forward)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        foreach (SvgColor background in new[] { SvgColor.Transparent, new SvgColor(255, 255, 255, 255) })
        foreach (string opacity in new[] { "", " opacity='.5'" })
        {
            const string definition = "<defs><mask id='m' style='display:none'/></defs>";
            string content = "<g" + opacity + " mask='url(#m)'><text x='.25' y='7' font-family='Example' font-size='200' fill='red'>A</text><text x='3.25' y='7' font-family='Example' font-size='200' fill='blue'>A</text></g>";
            SvgRenderOptions options = new(null, 1, background, SvgRenderLimits.Default, fonts);
            byte[] expected = SvgConverter.ConvertToPng(Document(content.Replace(" mask='url(#m)'", "", StringComparison.Ordinal)), options, CancellationToken.None).ToArray();
            Assert.NotEqual(SvgConverter.ConvertToPng(Document(""), options, CancellationToken.None).ToArray(), expected);
            byte[] input = Document(forward ? content + definition : definition + content);
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, new(null, 1, background, SvgRenderLimits.Default), CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mask_resource_display_preserves_storage_limits_and_duplicate_checks(bool ancestor)
    {
        string definition = "<defs><mask id='m' " + (ancestor ? "" : "style='display:none'") + "><g><path d='M0 0H8V8H0Z' fill='white'/></g></mask></defs>";
        if (ancestor) definition = "<g display='none'>" + definition + "</g>";
        byte[] input = Document(definition + "<rect width='8' height='8' fill='red' mask='url(#m)'/>");
        RenderBudget baseline = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, baseline, null, CancellationToken.None)) { }
        Assert.Equal(0, baseline.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(baseline.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget cancelled = new RenderBudget(new HostMemoryBudget(1_000_000));
        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelled, cancellation.Token));
        Assert.Equal(0, cancelled.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(Document(definition + "<defs><mask id='m'/></defs>"))).ErrorCode);
        Assert.Equal(Render(Document("<rect width='8' height='8' fill='red'/>")), Render(input));
    }
}
