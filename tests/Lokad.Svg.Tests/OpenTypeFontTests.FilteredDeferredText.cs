using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static TheoryData<string, string, bool, double> DeferredFilterTextCases()
    {
        TheoryData<string, string, bool, double> cases = new();
        foreach (string consumer in new[] { "direct-gradient", "path-solid", "path-gradient", "continuation", "decoration", "stroke" })
        foreach (string context in new[] { "group", "own", "nested", "use", "symbol", "pattern", "mask" })
        foreach (bool forward in new[] { false, true })
        foreach (double scale in new[] { 1d, 1.5 }) cases.Add(consumer, context, forward, scale);
        return cases;
    }

    [Theory]
    [MemberData(nameof(DeferredFilterTextCases))]
    public async Task Deferred_text_retains_enclosing_filter_source_bounds(string consumer, string context, bool forward, double scale)
    {
        byte[] program = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(program)], CancellationToken.None);
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] empty = SvgConverter.ConvertToPng(StrokeDocument(string.Empty), options, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(DeferredFilterTextDocument(consumer, context, forward), options, CancellationToken.None).ToArray();
        Assert.False(actual.AsSpan().SequenceEqual(empty));
    }

    [Theory]
    [InlineData("path-gradient", "group")]
    [InlineData("continuation", "own")]
    [InlineData("path-gradient", "symbol")]
    public async Task Deferred_filter_recording_releases_owners_on_work_denial_and_cancellation(string consumer, string context)
    {
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] input = DeferredFilterTextDocument(consumer, context, true);
        RenderBudget accepted = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accepted, fonts, CancellationToken.None)) { }
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget memoryDenied = new RenderBudget(new HostMemoryBudget(256));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, memoryDenied, fonts, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, memoryDenied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, accepted, fonts, cancellation.Token)).CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accepted, fonts, CancellationToken.None)) { }
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
    }

    private static byte[] DeferredFilterTextDocument(string consumer, string context, bool forward)
    {
        const string resources = "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='96' height='96'><feOffset dx='2' dy='1'/></filter><filter id='z' filterUnits='userSpaceOnUse' x='0' y='0' width='96' height='96'><feOffset dx='0' dy='0'/></filter><path id='b' d='M8 64H88'/><path id='short' d='M8 64H32'/><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        string paint = consumer == "path-solid" || consumer == "decoration" ? "fill='red'" : "fill='url(#p)'";
        if (consumer == "decoration") paint += " text-decoration='underline'";
        if (consumer == "stroke") paint = "fill='none' stroke='url(#p)' stroke-width='2'";
        string content = consumer == "direct-gradient" ? "A" : consumer == "continuation" ? "<textPath href='#short'>A</textPath>A" : "<textPath href='#b'>A</textPath>";
        string label = "<text id='label' font-family='Example' font-size='48' " + paint + (consumer == "direct-gradient" ? " x='8' y='64'" : "") + ">" + content + "</text>";
        const string effect = "filter='url(#f)'";
        string definitions = resources;
        string body;
        switch (context)
        {
            case "own": body = label.Replace("<text ", "<text " + effect + " ", StringComparison.Ordinal); break;
            case "nested": body = "<g " + effect + "><g filter='url(#z)'>" + label + "</g></g>"; break;
            case "use": definitions += label; body = "<g " + effect + "><use href='#label'/></g>"; break;
            case "symbol": definitions += "<symbol id='s' viewBox='0 0 96 96'>" + label + "</symbol>"; body = "<g " + effect + "><use href='#s' width='96' height='96'/></g>"; break;
            case "pattern": definitions += "<pattern id='tile' patternUnits='userSpaceOnUse' width='96' height='96'><g " + effect + ">" + label + "</g></pattern>"; body = "<rect width='96' height='96' fill='url(#tile)'/>"; break;
            case "mask": definitions += "<mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='96' height='96'><g " + effect + ">" + label + "</g></mask>"; body = "<rect width='96' height='96' fill='red' mask='url(#m)'/>"; break;
            default: body = "<g " + effect + ">" + label + "</g>"; break;
        }
        string defs = "<defs>" + definitions + "</defs>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'>" + (forward ? body + defs : defs + body) + "</svg>");
    }
}
