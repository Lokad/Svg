using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("use")]
    [InlineData("symbol")]
    [InlineData("pattern")]
    public async Task Descriptive_characters_do_not_spend_the_rendered_text_character_allowance(string context)
    {
        byte[] input = Document(new string('A', Lokad.Svg.Parsing.SvgTextLimits.MaximumCharacters + 1)), control = Document("");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        byte[] Document(string ignored)
        {
            string text = "<text id='t' font-family='Example' font-size='200' x='2' y='14'>A<metadata>" + ignored + "</metadata>A</text>";
            string body = context switch
            {
                "text" => text,
                "use" => "<defs>" + text + "</defs><use href='#t'/>",
                "symbol" => "<defs><symbol id='s'>" + text + "</symbol></defs><use href='#s'/>",
                _ => "<defs><pattern id='tile' patternUnits='userSpaceOnUse' width='32' height='32'>" + text + "</pattern></defs><rect width='32' height='32' fill='url(#tile)'/>",
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
        }
    }

    public static IEnumerable<object[]> TextDescriptions()
    {
        foreach (string context in new[] { "text", "span", "path", "use", "symbol", "pattern" })
        foreach (string element in new[] { "title", "desc", "metadata" })
        foreach (string body in new[] { "", "ignored", "<tspan font-family='Unavailable' x='bad'>ignored</tspan>" })
            yield return [context, "<" + element + ">" + body + "</" + element + ">"];
        foreach (string context in new[] { "text", "span", "path", "use", "symbol", "pattern" })
        foreach (string element in new[] { "title", "desc", "metadata" })
            yield return [context, "<" + element + "/>"];
    }

    [Theory]
    [MemberData(nameof(TextDescriptions))]
    public async Task Descriptions_inside_text_do_not_emit_characters_or_change_resource_replay(string context, string description)
    {
        byte[] input = Document(description), control = Document("");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleAAndSpaceFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        byte[] Document(string child)
        {
            const string attributes = " font-family='Example' font-size='200' x='2' y='14'";
            string glyphs = context switch
            {
                "span" => "<text" + attributes + ">A<tspan>" + child + "A</tspan></text>",
                "path" => "<text font-family='Example' font-size='200'><textPath href='#p'>A" + child + "A</textPath></text>",
                _ => "<text" + attributes + ">A" + child + "A</text>",
            };
            string definitions = "<path id='p' d='M2 14H30'/>";
            string content;
            switch (context)
            {
                case "use": definitions += glyphs.Replace("<text ", "<text id='t' ", StringComparison.Ordinal); content = "<use href='#t'/>"; break;
                case "symbol": definitions += "<symbol id='s'>" + glyphs + "</symbol>"; content = "<use href='#s'/>"; break;
                case "pattern": definitions += "<pattern id='tile' patternUnits='userSpaceOnUse' width='32' height='32'>" + glyphs + "</pattern>"; content = "<rect width='32' height='32' fill='url(#tile)'/>"; break;
                default: content = glyphs; break;
            }
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + content + "<rect x='28' y='28' width='4' height='4' fill='blue'/></svg>");
        }
    }

    [Theory]
    [InlineData("A {0} A", "")]
    [InlineData("A {0} A", "xml:space='preserve'")]
    [InlineData("<tspan x='2 14' rotate='5 10'>A{0}A</tspan>", "")]
    [InlineData("<tspan letter-spacing='2' fill='red'>A {0}<tspan fill='blue'>A</tspan></tspan> A", "")]
    [InlineData("A{0}<tspan class='after' dx='2'>A</tspan>", "")]
    public async Task Ignored_descriptions_preserve_whitespace_position_lists_and_css_ancestors(string content, string attributes)
    {
        const string description = "<metadata class='before' font-size='bad'><title> A </title><g><tspan> A </tspan></g></metadata>";
        byte[] input = Document(description), control = Document("");
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleAAndSpaceFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        byte[] Document(string child) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><style>text > tspan.after {fill:red} metadata tspan {fill:blue}</style><text font-family='Example' font-size='200' x='2' y='14' " + attributes + ">" + content.Replace("{0}", child, StringComparison.Ordinal) + "</text></svg>");
    }

    [Theory]
    [InlineData("title")]
    [InlineData("desc")]
    [InlineData("metadata")]
    public async Task Mask_text_descriptions_keep_raw_replay_with_an_explicitly_prepared_face(string element)
    {
        // Mask-only SVG-driven acquisition is the separate R74 regression.
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(Document(""), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Document("<" + element + ">ignored</" + element + ">"), options, CancellationToken.None).ToArray());

        static byte[] Document(string child) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><text id='t' font-family='Example' font-size='200' x='2' y='14' fill='white'>A" + child + "A</text><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><use href='#t'/></mask></defs><rect width='32' height='32' fill='red' mask='url(#m)'/></svg>");
    }
}
