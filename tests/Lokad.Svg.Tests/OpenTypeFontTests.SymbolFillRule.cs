using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> SymbolFillRules()
    {
        foreach (string rule in new[] { "evenodd", "nonzero" })
        foreach (string location in new[] { "attribute", "inline", "rule" })
        foreach (int children in new[] { 1, 2 })
        foreach (bool alias in new[] { false, true }) yield return [rule, location, children, alias];
    }

    [Theory]
    [MemberData(nameof(SymbolFillRules))]
    public async Task Symbol_fill_rule_survives_flattening_and_overrides_instance_inheritance(string rule, string location, int children, bool alias)
    {
        string declaration = location == "attribute" ? "fill-rule='" + rule + "'"
            : location == "inline" ? "style='fill-rule:" + rule + "'" : "class='p'";
        string sheet = location == "rule" ? "<style>.p{fill-rule:" + rule + "}</style>" : "";
        string paths = "<path id='child' d='M0 0H8V8H0ZM2 2H6V6H2Z'/>"
            + (children == 2 ? "<path d='M8 0H16V8H8ZM10 2H14V6H10Z'/>" : "");
        string source = "<defs><symbol id='s' " + declaration + ">" + paths + "</symbol>"
            + (alias ? "<symbol id='alias'><use href='#s'/></symbol>" : "") + "</defs>";
        byte[] input = Svg(sheet + source + "<use href='#" + (alias ? "alias" : "s") + "'/>", rule == "evenodd" ? "nonzero" : "evenodd");
        byte[] expected = Render(Svg("<g fill-rule='" + rule + "'>" + paths + "</g>", "nonzero"));
        Assert.Equal(expected, Render(input));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);
        // Referring to a child directly excludes its source symbol's presentation.
        Assert.Equal(Render(Svg(paths.Split("/>")[0] + "/>", "nonzero")),
            Render(Svg(sheet + source + "<use href='#child'/>", "nonzero")));

        static byte[] Svg(string body, string inheritedRule)
            => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='8' fill='red' fill-rule='" + inheritedRule + "'>" + body + "</svg>");
        static byte[] Render(byte[] svg) => SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }
}
