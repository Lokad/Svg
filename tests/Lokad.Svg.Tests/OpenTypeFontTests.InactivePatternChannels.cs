using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> InactivePatternChannels()
    {
        foreach (string property in new[] { "fill-opacity", "stroke-opacity", "stroke-width" })
        foreach (string content in new[] { "text", "use", "mask-group", "mask-use" })
        foreach (string consumer in new[] { "direct", "identified", "use", "group", "symbol", "mask", "filter" })
            yield return [property, content, consumer];
    }

    [Theory]
    [MemberData(nameof(InactivePatternChannels))]
    public async Task A_primitive_inactive_paint_channel_needs_no_pattern_fonts(string property, string content, string consumer)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string paint = property == "fill-opacity" ? "fill='url(#p)'" : "fill='none' stroke='url(#p)'" + (property == "stroke-width" ? "" : " stroke-width='2'");
        byte[] expected = Render(Input("solid", "attribute", false, false));
        if (consumer != "filter") Assert.Equal(Render(EmptyPatternDocument("")), expected);
        foreach (string cascade in new[] { "attribute", "inline", "sheet" })
        foreach (bool forward in new[] { false, true })
        {
            byte[] input = Input(content, cascade, forward, false);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            Assert.Equal(expected, Render(input));
            using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            Assert.Empty(fonts.Fonts);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
        byte[] live = Input(content, "attribute", false, true);
        Assert.Single(SvgFontRequestDiscovery.Discover(live, CancellationToken.None));
        using SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(live, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        if (consumer != "filter") Assert.NotEqual(expected, control.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);

        byte[] Input(string tile, string cascade, bool forward, bool live)
        {
            string value = live ? "1" : cascade == "attribute" ? "0" : cascade == "sheet" ? "-0e+2" : property == "stroke-width" ? "0px" : "0%";
            string style = cascade == "attribute" ? " " + property + "='" + value + "'"
                : cascade == "inline" ? " style='" + property + ":" + value + "'" : " class='channel'";
            string shape = "<rect width='32' height='32' " + paint + style + "/>";
            string body = consumer switch
            {
                "identified" => shape.Replace("/>", " id='shape'/>", StringComparison.Ordinal),
                "use" => "<defs>" + shape.Replace("/>", " id='shape'/>", StringComparison.Ordinal) + "</defs><use href='#shape' fill-opacity='1' stroke-opacity='1' stroke-width='4'/>",
                "group" => "<defs><g id='shape'>" + shape + "</g></defs><use href='#shape'/>",
                "symbol" => "<defs><symbol id='shape' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#shape' width='32' height='32'/>",
                "mask" => "<defs><mask id='target' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + shape + "</mask></defs><rect width='32' height='32' fill='blue' mask='url(#target)'/>",
                "filter" => "<defs><filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs><g filter='url(#outer)'>" + shape + "</g>",
                _ => shape
            };
            string rules = cascade == "sheet" ? "<style>.channel { " + property + ":" + value + "!important }</style>" : "";
            string resources = EmptyPatternResources(tile);
            return EmptyPatternDocument(rules + (forward ? body + resources : resources + body));
        }
    }

    public static IEnumerable<object[]> LivePatternChannels()
    {
        foreach (string property in new[] { "fill-opacity", "stroke-opacity", "stroke-width" })
        foreach (string scenario in new[] { "other-channel", "sibling", "inheritance", "cascade", "small-nonzero" })
        foreach (string content in new[] { "text", "use", "mask-group", "mask-use" })
            yield return [property, scenario, content];
    }

    [Theory]
    [MemberData(nameof(LivePatternChannels))]
    public async Task Live_pattern_channels_keep_their_font_requirement(string property, string scenario, string content)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string paint = property == "fill-opacity" ? "fill='url(#p)'" : "fill='none' stroke='url(#p)'";
        string shape = "<rect width='32' height='32' " + paint;
        string body = scenario switch
        {
            "other-channel" => "<rect width='32' height='32' fill='url(#p)' stroke='url(#p)' " + property + "='0'/>",
            "sibling" => shape + " " + property + "='0'/>" + shape + "/>",
            "inheritance" => "<g " + property + "='0'>" + shape + " " + property + "='1'/></g>",
            "cascade" => "<style>.live { " + property + ":1!important }</style>" + shape + " " + property + "='0' class='live'/>",
            _ => shape + " " + property + "='0.001'/>"
        };
        byte[] input = EmptyPatternDocument(EmptyPatternResources(content) + body);
        Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        byte[] actual = prepared.ConvertToPng(CancellationToken.None).ToArray();
        if (scenario != "small-nonzero") Assert.NotEqual(Render(EmptyPatternDocument("")), actual);
        Assert.Equal(1, provider.RequestCount);
    }
}
