using Lokad.Svg.Encoding;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> PatternResourceDisplayCases()
    {
        foreach (string content in new[] { "rect", "circle", "path", "group", "gradient", "image", "use" })
        foreach (string scenario in new[] { "own", "ancestor", "base", "alias", "replacement", "replacement-visible" })
            yield return [content, scenario];
    }

    [Theory]
    [MemberData(nameof(PatternResourceDisplayCases))]
    public async Task Pattern_display_distinguishes_own_ancestor_and_template_content(string kind, string scenario)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        string content = kind switch
        {
            "circle" => "<circle cx='2' cy='4' r='2' fill='red'/>",
            "path" => "<path d='M0 0H4V8H0Z' fill='red'/>",
            "group" => "<g opacity='.5'><rect width='4' height='8' fill='red'/></g>",
            "gradient" => "<rect width='4' height='8' fill='url(#gradient)'/>",
            "image" => "<image width='4' height='8' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [255, 0, 0, 255], CancellationToken.None)) + "'/>",
            "use" => "<use href='#shape'/>",
            _ => "<rect width='4' height='8' fill='red'/>"
        };
        const string resources = "<defs><linearGradient id='gradient'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><path id='shape' d='M0 0H4V8H0Z' fill='red'/></defs>";
        const string target = "<rect width='8' height='8' fill='url(#p)'/>";
        string reference = scenario is "ancestor" or "replacement-visible" ? "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'>" + content + "</pattern></defs>" + target : "";
        byte[] expected = Render(Document(resources + reference));
        foreach (bool forward in new[] { false, true })
        foreach (string display in new[] { "display='none'", "style='display:none'", "class='hidden'" })
        {
            string basePattern = "<pattern id='base' width='8' height='8' patternUnits='userSpaceOnUse' " + (scenario is "base" or "replacement" ? display : "") + ">" + (scenario == "replacement-visible" ? "<rect width='8' height='8' fill='blue'/>" : content) + "</pattern>";
            string definition = scenario is "own" or "ancestor"
                ? "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse' " + (scenario == "own" ? display : "") + ">" + content + "</pattern>"
                : (forward ? "" : basePattern) + "<pattern id='p' href='#base' " + (scenario == "alias" ? display : "display='inline'") + ">" + (scenario is "replacement" or "replacement-visible" ? content : "") + "</pattern>" + (forward ? basePattern : "");
            definition = "<defs>" + definition + "</defs>";
            if (scenario == "ancestor") definition = "<g " + display + ">" + definition + "</g>";
            byte[] input = Document("<style>.hidden{display:none}</style>" + resources + (forward ? target + definition : definition + target));
            Assert.Equal(expected, Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData("own")]
    [InlineData("base")]
    [InlineData("alias")]
    public async Task Disabled_patterns_do_not_require_unavailable_tile_fonts(string scenario)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        foreach (bool forward in new[] { false, true })
        foreach (string display in new[] { "display='none'", "style='display:none'", "class='hidden'" })
        {
            const string text = "<text x='0' y='7' font-size='12' font-family='Unavailable'>A</text>";
            string source = "<pattern id='base' patternUnits='userSpaceOnUse' " + (scenario is "base" or "replacement" ? display : "") + ">" + text + "</pattern>";
            string alias = "<pattern id='p' href='#base' width='8' height='8' " + (scenario == "alias" ? display : "") + ">" + (scenario == "replacement" ? text : "") + "</pattern>";
            string definitions = scenario == "own" ? "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse' " + display + ">" + text + "</pattern>" : forward ? alias + source : source + alias;
            byte[] input = Document("<style>.hidden{display:none}</style><defs>" + definitions + "</defs><rect width='8' height='8' fill='url(#p)'/>");
            Assert.Equal(Render(Document("")), Render(input));
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(Render(Document("")), prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
        Assert.Equal(0, provider.RequestCount);
    }
}
