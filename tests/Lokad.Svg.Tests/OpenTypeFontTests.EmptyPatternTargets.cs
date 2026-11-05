using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using System.Xml;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> EmptyPatternTargets(bool research)
    {
        foreach (string target in new[] { "width", "height", "circle", "rx", "ry", "ellipse-fallback", "path", "white-path", "omitted" })
        foreach (string role in new[] { "fill", "stroke" })
        foreach (string content in new[] { "text", "use", "mask-group", "mask-use" })
        foreach (string consumer in new[] { "direct", "identified", "use", "group", "symbol", "mask", "filter" })
        {
            // Every consumer/content/channel route uses the same zero-extent predicate.
            // Keep each primitive/channel lowering route directly as well.
            bool routine = target == "width" || consumer == "direct" && content == "text";
            if (routine != research) yield return [target, role, content, consumer];
        }
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [MemberData(nameof(EmptyPatternTargets), false)]
    public Task Empty_primitive_pattern_paints_need_no_tile_fonts(string target, string role, string content, string consumer)
        => AssertEmptyPrimitivePattern(target, role, content, consumer);

    [Theory]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    [MemberData(nameof(EmptyPatternTargets), true)]
    public Task Empty_primitive_pattern_paints_extended_preparation(string target, string role, string content, string consumer)
        => AssertEmptyPrimitivePattern(target, role, content, consumer);

    private async Task AssertEmptyPrimitivePattern(string target, string role, string content, string consumer)
    {
        (var catalog, var provider, _) = CreateUnusedFontPack();
        string emptyShape = EmptyPatternShape(target, role, false);
        string visibleShape = EmptyPatternShape(target, role, true);
        string emptyBody = EmptyPatternBody(emptyShape, consumer);
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
        Assert.Single(SvgFontRequestDiscovery.Discover(EmptyPatternDocument(EmptyPatternResources(content) + EmptyPatternBody(visibleShape, consumer)), CancellationToken.None));
    }

    private static string EmptyPatternShape(string target, string role, bool visible)
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

    private static string EmptyPatternBody(string shape, string consumer) => consumer switch
    {
        "identified" => shape.Replace("/>", " id='zero'/>", StringComparison.Ordinal),
        "use" => "<defs>" + shape.Replace("/>", " id='zero'/>", StringComparison.Ordinal) + "</defs><use href='#zero' width='32' height='32'/>",
        "group" => "<defs><g id='zero'>" + shape + "</g></defs><use href='#zero'/>",
        "symbol" => "<defs><symbol id='zero' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#zero' width='32' height='32'/>",
        "mask" => "<defs><mask id='target' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + shape + "</mask></defs><rect width='32' height='32' fill='blue' mask='url(#target)'/>",
        "filter" => "<defs>" + shape.Replace("/>", " id='zero'/>", StringComparison.Ordinal) + "<filter id='outer' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feFlood flood-color='blue'/></filter></defs><g filter='url(#outer)'><use href='#zero'/></g>",
        _ => shape
    };

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("circle")]
    [InlineData("rx")]
    [InlineData("ry")]
    [InlineData("ellipse-fallback")]
    [InlineData("path")]
    [InlineData("white-path")]
    [InlineData("omitted")]
    public void Intrinsic_empty_pattern_targets_use_the_shared_primitive_predicate(string target)
    {
        Assert.False(HasTarget(EmptyPatternShape(target, "fill", false)));
        Assert.True(HasTarget(EmptyPatternShape(target, "fill", true)));

        static bool HasTarget(string shape)
        {
            using StringReader text = new(shape);
            using XmlReader reader = XmlReader.Create(text);
            reader.MoveToContent();
            return SvgMaskSyntax.HasPaintableTarget(reader, true, null);
        }
    }

    public static IEnumerable<object[]> EmptyPatternGraphCases()
    {
        foreach (object[] row in EmptyPatternTargets(false))
            if ((string)row[0] == "width") yield return row;
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(EmptyPatternGraphCases))]
    public void Empty_pattern_graph_discovery_preserves_all_consumer_and_content_routes(string target, string role, string content, string consumer)
    {
        string resources = EmptyPatternResources(content);
        string empty = EmptyPatternBody(EmptyPatternShape(target, role, false), consumer);
        string visible = EmptyPatternBody(EmptyPatternShape(target, role, true), consumer);
        foreach (bool forward in new[] { false, true })
        {
            Assert.Empty(SvgFontRequestDiscovery.Discover(EmptyPatternDocument(forward ? empty + resources : resources + empty), CancellationToken.None));
            Assert.Single(SvgFontRequestDiscovery.Discover(EmptyPatternDocument(forward ? visible + resources : resources + visible), CancellationToken.None));
        }
    }

    [Theory]
    [Trait("Layer", "Contract")]
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
