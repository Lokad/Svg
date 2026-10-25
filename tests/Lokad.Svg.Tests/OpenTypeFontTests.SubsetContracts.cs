using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> UnsupportedTextKeywords()
    {
        foreach (string property in new[] { "font-weight", "font-style", "font-size", "letter-spacing", "word-spacing", "white-space" })
        foreach (string keyword in new[] { "inherit", "initial", "unset" })
        foreach (string form in new[] { "attribute", "inline", "sheet" })
            if (property != "white-space" || form != "attribute") yield return [property, keyword, form];
    }

    [Theory]
    [MemberData(nameof(UnsupportedTextKeywords))]
    public async Task Explicit_text_global_keywords_keep_the_documented_subset(string property, string keyword, string form)
    {
        string positive = property switch { "font-weight" => "400", "font-size" => "100px", _ => "normal" };
        foreach (string target in new[] { "group", "text", "span" })
            await AssertSubsetRejection(SubsetInput(target, form, property, keyword), SubsetInput(target, form, property, positive), true);
    }

    public static IEnumerable<object[]> UnsupportedZeroFontSizes()
    {
        foreach (string value in new[] { "0", "+0", "-0", "0px", "0.0", "0e0" })
        foreach (string target in new[] { "group", "text", "span", "pattern", "mask" })
        foreach (string form in new[] { "attribute", "inline", "sheet" })
            if (form != "attribute" || target is not ("pattern" or "mask")) yield return [value, target, form];
    }

    [Theory]
    [MemberData(nameof(UnsupportedZeroFontSizes))]
    public async Task Text_font_sizes_must_be_positive_before_program_acquisition(string value, string target, string form)
        => await AssertSubsetRejection(SubsetInput(target, form, "font-size", value), SubsetInput(target, form, "font-size", "100px"), true);

    public static IEnumerable<object[]> UnsupportedEffectKeywords()
    {
        foreach (string property in new[] { "clip-path", "mask" })
        foreach (string target in new[] { "group", "text", "shape", "use" })
        {
            foreach (string keyword in new[] { "initial", "unset" }) yield return [property, target, "attribute", keyword];
            if (property == "mask") yield return [property, target, "attribute", "inherit"];
            foreach (string form in new[] { "inline", "sheet" })
            foreach (string value in new[] { "none", "initial", "unset", "inherit", "url(#effect)" })
                yield return [property, target, form, value];
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedEffectKeywords))]
    public async Task Effect_attributes_and_css_keep_distinct_admission_contracts(string property, string target, string form, string value)
        => await AssertSubsetRejection(SubsetInput(target, form, property, value), SubsetInput(target, "attribute", property, "none"), false);

    private static async Task AssertSubsetRejection(byte[] input, byte[] positive, bool fontSyntax)
    {
        byte[] unchanged = input.ToArray();
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        if (fontSyntax)
        {
            SvgException discovery = await Assert.ThrowsAsync<SvgException>(async () =>
            {
                using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.UnsupportedFeature, discovery.ErrorCode);
        }
        SvgException preparation = await Assert.ThrowsAsync<SvgException>(async () =>
        {
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, preparation.ErrorCode);
        if (fontSyntax) Assert.Equal(0, provider.RequestCount);

        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(positive, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
        RenderBudget budget = new(1_000_000);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, options.Limits, budget, fonts, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] expected = SvgConverter.ConvertToPng(positive, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument admitted = await SvgPreparedDocument.CreateAsync(positive, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, admitted.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    private static byte[] SubsetInput(string target, string form, string property, string value)
    {
        string attributes = form switch
        {
            "attribute" => property + "='" + value + "'",
            "inline" => "style='" + property + ":" + value + "'",
            _ => "class='case'",
        };
        string css = form == "sheet" ? "<style>.case{" + property + ":" + value + "}</style>" : "";
        const string text = "<text x='1' y='3'>A</text>";
        string content = target switch
        {
            "group" => "<g " + attributes + ">" + text + "</g>",
            "text" => "<text x='1' y='3' " + attributes + ">A</text>",
            "span" => "<text x='1' y='3'><tspan " + attributes + ">A</tspan></text>",
            "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8' " + attributes + ">" + text + "</pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
            "mask" => "<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='8' height='8' " + attributes + "><g fill='white'>" + text + "</g></mask></defs><rect width='8' height='8' mask='url(#m)'/>",
            "shape" => "<rect width='8' height='8' " + attributes + "/>",
            "use" => "<defs><rect id='r' width='8' height='8'/></defs><use href='#r' " + attributes + "/>",
            _ => throw new ArgumentException("Unknown subset target.", nameof(target)),
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' font-family='Example' font-size='100'>" + css + content + "</svg>");
    }
}
