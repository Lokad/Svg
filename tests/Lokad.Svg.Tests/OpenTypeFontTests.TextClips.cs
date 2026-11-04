using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.Testing;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("live", 1, true)]
    [InlineData("fill-none", 1, true)]
    [InlineData("unused", 0, true)]
    [InlineData("hidden-child", 0, false)]
    [InlineData("zero-opacity", 0, false)]
    [InlineData("hidden-resource", 0, true)]
    [InlineData("dormant-group", 0, false)]
    [InlineData("ordinary-use", 1, true)]
    [InlineData("clip-group-use", 0, false)]
    [InlineData("hidden-consumer", 0, false)]
    [InlineData("zero-opacity-consumer", 0, false)]
    [InlineData("empty-consumer", 0, false)]
    [InlineData("nested-geometry", 1, true)]
    [InlineData("clip-text-use", 1, true)]
    [InlineData("resource-font", 1, true)]
    [InlineData("ignored-paint", 1, true)]
    [InlineData("invisible-text-bounds", 1, true)]
    [InlineData("live-group", 1, true)]
    [InlineData("empty-group", 0, false)]
    [InlineData("hidden-group", 0, false)]
    [InlineData("zero-opacity-group", 0, false)]
    [InlineData("hidden-group-child", 0, false)]
    public async Task Text_clip_font_selection_follows_live_source_roles(string role, int expectedRequests, bool visible)
    {
        string family = expectedRequests == 0 ? "Unavailable" : "Example";
        string attributes = role switch
        {
            "fill-none" => " fill='none' stroke='url(#ignored)' stroke-width='6'",
            "hidden-child" => " display='none'",
            "zero-opacity" or "invisible-text-bounds" => " opacity='0'",
            "ignored-paint" => " fill='url(#ignored)' stroke='url(#ignored)' filter='url(#ignored)'",
            _ => ""
        };
        string text = "<text id='t' x='2' y='14' font-family='" + family + "' font-size='200'" + attributes + ">A</text>";
        string child = role == "dormant-group" ? "<g>" + text + "</g>" : text;
        string extra = "", resourceAttributes = role == "hidden-resource" ? " display='none'" : "";
        if (role == "clip-group-use")
        {
            extra = "<g id='group'>" + text + "</g>";
            child = "<use href='#group'/>";
        }
        if (role == "clip-text-use")
        {
            extra = text;
            child = "<use href='#t' x='.25' opacity='.5'/>";
        }
        if (role == "resource-font")
        {
            resourceAttributes = " font-family='Example' font-size='200' fill='url(#ignored)'";
            child = "<text x='2' y='14'>A</text>";
        }
        if (role == "invisible-text-bounds") child = "<rect width='8' height='8' opacity='.5'/>" + text;
        if (role == "nested-geometry")
        {
            extra = "<clipPath id='inner'>" + text + "</clipPath>";
            child = "<rect width='32' height='32' clip-path='url(#inner)'/>";
        }
        string target = role == "ordinary-use" ? "<use href='#t'/>"
            : "<rect width='" + (role == "empty-consumer" ? "0" : "32") + "' height='32' fill='red'"
                + (role is "unused" ? "" : " clip-path='url(#c)'")
                + (role == "hidden-consumer" ? " display='none'" : role == "zero-opacity-consumer" ? " opacity='0'" : "") + "/>";
        target = role switch
        {
            "live-group" => "<g clip-path='url(#c)' opacity='.6'><rect width='32' height='32'/></g>",
            "empty-group" => "<g clip-path='url(#c)'/>",
            "hidden-group" => "<g clip-path='url(#c)' display='none'><rect width='32' height='32'/></g>",
            "zero-opacity-group" => "<g clip-path='url(#c)' opacity='0'><rect width='32' height='32'/></g>",
            "hidden-group-child" => "<g clip-path='url(#c)'><rect width='32' height='32' visibility='hidden'/></g>",
            _ => target
        };
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + extra + "<clipPath id='c'" + resourceAttributes + ">" + child + "</clipPath></defs>" + target + "</svg>");
        Assert.Equal(expectedRequests, SvgFontRequestDiscovery.Discover(input, CancellationToken.None).Count());
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(expectedRequests, provider.RequestCount);
        RenderBudget budget = new(new HostMemoryBudget(4_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), budget, CancellationToken.None))
            Assert.Equal(visible, surface.Pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Text_clip_owners_release_after_refusal_cancellation_and_prepared_retry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><rect width='5' height='5' opacity='.5'/><text x='2' y='14' font-family='Example' font-size='200'>A</text></clipPath></defs><g transform='matrix(.92 .18 -.12 1.03 1 -1)'><rect width='32' height='32' fill='red' clip-path='url(#c)'/></g></svg>"u8.ToArray();
        byte[] unchanged = input.ToArray();
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using (SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None))
        {
            RenderBudget measured = new(new HostMemoryBudget(4_000_000));
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, CancellationToken.None)) { }
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
            RenderBudget denied = new(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        using TestMemoryBudget host = new(4_000_000);
        SvgRenderOptions options = SvgRenderOptions.Default.WithMemoryBudget(host);
        using CancellationTokenSource cancellation = new();
        using (FontIntegrityProbe.Arm(cancellation.Cancel))
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, cancellation.Token))).CancellationToken);
        Assert.Equal(0, host.Reserved);
        using (SvgPreparedDocument denied = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None))
        {
            host.Limit = host.Reserved;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => denied.ConvertToOwnedPng(CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(0, host.Reserved);
        host.Limit = 4_000_000;
        using (SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None))
        using (SvgPngResult result = await retry.ConvertToOwnedPngAsync(CancellationToken.None))
        {
            Assert.False(result.Memory.IsEmpty);
            Assert.Equal(result.Memory.Length, host.Reserved);
            Assert.Null(retry.RetainedSceneGeometries);
        }
        Assert.Equal(0, host.Reserved);
        Assert.False(host.Disposed);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_clip_cycles_reject_before_font_program_acquisition(bool indirect)
    {
        string clip = indirect ? "b" : "a";
        string definitions = "<clipPath id='a'><text font-family='Example' clip-path='url(#" + clip + ")'>A</text></clipPath>"
            + (indirect ? "<clipPath id='b'><text font-family='Example' clip-path='url(#a)'>A</text></clipPath>" : "");
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + definitions + "</defs><rect width='32' height='32' clip-path='url(#a)'/></svg>");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () =>
            await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
    }
}
