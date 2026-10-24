using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> EmptyPatternTargets()
    {
        foreach (string target in new[] { "width", "height", "circle", "rx", "ry", "ellipse-fallback", "path", "white-path", "omitted" })
        foreach (string role in new[] { "fill", "stroke" })
        foreach (string content in new[] { "text", "use", "mask-group", "mask-use" })
        foreach (string consumer in new[] { "direct", "identified", "use", "group", "symbol", "mask", "filter" })
            yield return [target, role, content, consumer];
    }

    [Theory]
    [MemberData(nameof(EmptyPatternTargets))]
    public async Task Empty_primitive_pattern_paints_need_no_tile_fonts(string target, string role, string content, string consumer)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string emptyShape = Shape(false);
        string visibleShape = Shape(true);
        string emptyBody = Body(emptyShape);
        byte[] expected = Render(EmptyPatternDocument(EmptyPatternResources("solid") + emptyBody));
        if (consumer != "filter") Assert.Equal(Render(EmptyPatternDocument("")), expected);
        foreach (bool forward in new[] { false, true })
        {
            string resource = EmptyPatternResources(content);
            byte[] input = EmptyPatternDocument(forward ? emptyBody + resource : resource + emptyBody);
            Assert.Empty(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
            Assert.Equal(expected, Render(input));
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            Assert.Empty(snapshot.Fonts);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
        Assert.Single(SvgFontRequestDiscovery.Discover(EmptyPatternDocument(EmptyPatternResources(content) + Body(visibleShape)), CancellationToken.None));

        string Shape(bool visible)
        {
            string extent = visible ? "32" : "0";
            string radius = visible ? "16" : "0";
            string start = target switch
            {
                "width" => "<rect width='" + extent + "' height='32'",
                "height" => "<rect width='32' height='" + extent + "'",
                "circle" => "<circle cx='16' cy='16' r='" + radius + "'",
                "rx" => "<ellipse cx='16' cy='16' rx='" + radius + "' ry='16'",
                "ry" => "<ellipse cx='16' cy='16' rx='16' ry='" + radius + "'",
                "ellipse-fallback" => "<ellipse cx='16' cy='16' rx='" + radius + "'",
                "omitted" => "<rect height='32'" + (visible ? " width='32'" : ""),
                _ => "<path d='" + (visible ? "M0 0H32V32H0Z" : target == "white-path" ? " &#9;&#10;&#13; " : "") + "'"
            };
            return start + (role == "fill" ? " fill='url(#p)'" : " fill='none' stroke='url(#p)' stroke-width='2'") + "/>";
        }

        string Body(string shape) => consumer switch
        {
            "identified" => shape.Replace("/>", " id='zero'/>", StringComparison.Ordinal),
            "use" => "<defs>" + shape.Replace("/>", " id='zero'/>", StringComparison.Ordinal) + "</defs><use href='#zero' width='32' height='32'/>",
            "group" => "<defs><g id='zero'>" + shape + "</g></defs><use href='#zero'/>",
            "symbol" => "<defs><symbol id='zero' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#zero' width='32' height='32'/>",
            "mask" => "<defs><mask id='target' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + shape + "</mask></defs><rect width='32' height='32' fill='blue' mask='url(#target)'/>",
            "filter" => "<defs>" + shape.Replace("/>", " id='zero'/>", StringComparison.Ordinal) + "<filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs><g filter='url(#outer)'><use href='#zero'/></g>",
            _ => shape
        };
    }

    [Theory]
    [InlineData("text", false)]
    [InlineData("text", true)]
    [InlineData("use", false)]
    [InlineData("use", true)]
    [InlineData("mask-group", false)]
    [InlineData("mask-group", true)]
    [InlineData("mask-use", false)]
    [InlineData("mask-use", true)]
    public async Task A_live_pattern_consumer_still_acquires_and_paints_the_tile_font(string content, bool stroke)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string paint = stroke ? "fill='none' stroke='url(#p)' stroke-width='2'" : "fill='url(#p)'";
        byte[] input = EmptyPatternDocument(EmptyPatternResources(content) + "<rect width='0' height='32' " + paint + "/><g fill='url(#p)'><rect width='0' height='32'/><rect width='32' height='32' " + paint + "/></g>");
        Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None));
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.NotEqual(Render(EmptyPatternDocument("")), prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(1, provider.RequestCount);
    }

    private static byte[] EmptyPatternDocument(string body)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");

    private static string EmptyPatternResources(string content)
    {
        const string text = "<text x='-2' y='17' font-family='Example' font-size='200' fill='blue'>A</text>";
        const string white = "<text x='-2' y='17' font-family='Example' font-size='200' fill='white'>A</text>";
        string sources = content == "use" ? text.Replace("<text ", "<text id='tileText' ", StringComparison.Ordinal)
            : content == "mask-use" ? white.Replace("<text ", "<text id='tileWhite' ", StringComparison.Ordinal) : "";
        string tile = content == "solid" ? "<rect width='32' height='32' fill='blue'/>"
            : content == "use" ? "<use href='#tileText'/>" : content == "text" ? text
            : "<rect width='32' height='32' fill='blue' mask='url(#tileMask)'/>";
        if (content is "mask-group" or "mask-use") sources += "<mask id='tileMask' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + (content == "mask-use" ? "<use href='#tileWhite'/>" : "<g>" + white + "</g>") + "</mask>";
        return "<defs>" + sources + "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'>" + tile + "</pattern></defs>";
    }
}
