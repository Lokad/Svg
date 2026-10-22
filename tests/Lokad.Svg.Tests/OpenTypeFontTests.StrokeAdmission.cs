using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> PercentageCornerJoins()
    {
        foreach (string radii in new[] { "rx='25%'", "ry='25%'", "rx='25%' ry='12.5%'" })
        foreach (string join in new[] { "miter", "round", "bevel" })
        foreach (string scope in new[] { "direct", "use", "symbol", "viewport", "pattern" }) yield return [radii, join, scope];
    }

    [Theory]
    [MemberData(nameof(PercentageCornerJoins))]
    public async Task Percentage_corners_use_the_same_join_admission_for_attributes_and_css(string radii, string join, string scope)
    {
        byte[] input = Svg("stroke-linejoin='" + join + "'"), expectedInput = Svg("style='stroke-linejoin:" + join + "'");
        byte[] expected = Render(expectedInput);
        Assert.Equal(expected, Render(input));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        byte[] Svg(string declaration)
        {
            string shape = "<rect id='s' x='4' y='4' width='24' height='24' " + radii + " " + declaration + " fill='red' stroke='blue' stroke-width='2'/>";
            string body = scope switch
            {
                "use" => "<defs>" + shape + "</defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='q' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#q'/>",
                "viewport" => "<svg width='32' height='32' viewBox='0 0 40 40'>" + shape + "</svg>",
                "pattern" => "<defs><pattern id='q' patternUnits='userSpaceOnUse' width='32' height='32'>" + shape + "</pattern></defs><rect width='32' height='32' fill='url(#q)'/>",
                _ => shape
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
        }
        static byte[] Render(byte[] svg) => SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    [Theory]
    [InlineData("bevel", "butt", false)]
    [InlineData("bevel", "round", false)]
    [InlineData("bevel", "square", false)]
    [InlineData("bevel", "butt", true)]
    [InlineData("bevel", "round", true)]
    [InlineData("bevel", "square", true)]
    [InlineData("round", "butt", false)]
    [InlineData("round", "round", false)]
    [InlineData("round", "square", true)]
    [InlineData("miter", "butt", true)]
    public async Task Inert_line_joins_share_attribute_and_css_admission(string join, string cap, bool instance)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource _) = CreateExamplePack(CreateTriangleFont());
        foreach (string paint in new[] { "red", "url(#l)", "url(#p)" })
        {
            byte[] input = Svg("stroke-linejoin='" + join + "'"), expectedInput = Svg("style='stroke-linejoin:" + join + "'");
            byte[] expected = SvgConverter.ConvertToPng(expectedInput, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

            byte[] Svg(string declaration)
            {
                const string definitions = "<linearGradient id='l'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4' fill='red'/></pattern>";
                string shape = "<line id='s' x1='4' y1='4' x2='28' y2='28' stroke='" + paint + "' stroke-width='3' stroke-linecap='" + cap + "' " + declaration + "/>";
                string body = instance ? "<defs>" + definitions + shape + "</defs><use href='#s'/>" : "<defs>" + definitions + "</defs>" + shape;
                return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
            }
        }
        Assert.Equal(0, provider.RequestCount);
    }
}
