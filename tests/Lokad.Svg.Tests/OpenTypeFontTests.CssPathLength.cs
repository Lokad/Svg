namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> CssPathLengthCases()
    {
        foreach (string element in new[] { "line", "rect", "path", "polyline", "circle" })
            foreach (string context in new[] { "direct", "group", "use", "symbol", "pattern" })
                foreach (bool rule in new[] { false, true }) yield return [element, context, rule];
    }

    [Theory]
    [MemberData(nameof(CssPathLengthCases))]
    public async Task Path_length_is_an_xml_number_not_a_css_property(string element, string context, bool rule)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        byte[] ordinary = Input("", "");
        byte[] calibrated = Input("pathLength='12'", "");
        Assert.NotEqual(Render(ordinary), Render(calibrated));
        using (SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(calibrated, catalog, provider, SvgRenderOptions.Default, CancellationToken.None))
            Assert.Equal(Render(calibrated), prepared.ConvertToPng(CancellationToken.None).ToArray());

        foreach (string property in new[] { "pathLength", "pathlength", "PATHLENGTH" })
        {
            string declaration = property + ":12!important;" + property + ":24";
            byte[] input = Input(rule ? "class='target'" : "style='" + declaration + "'", rule ? "<style>.target{" + declaration + "}</style>" : "");
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => Render(input)).ErrorCode);
            SvgException error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
        }

        byte[] Input(string extra, string sheet)
        {
            string coordinates = element switch
            {
                "line" => "x1='0' y1='4' x2='8' y2='4'",
                "rect" => "x='1' y='1' width='6' height='6'",
                "path" => "d='M0 4H8'",
                "polyline" => "points='0 4 8 4'",
                _ => "cx='4' cy='4' r='3'"
            };
            string node = "<" + element + " " + coordinates + " fill='none' stroke='red' stroke-width='2' stroke-dasharray='3 1' " + extra;
            string body = context switch
            {
                "group" => "<g>" + node + "/></g>",
                "use" => "<defs>" + node + " id='s'/></defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + node + "/></symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='s' width='8' height='8' patternUnits='userSpaceOnUse'>" + node + "/></pattern></defs><rect width='8' height='8' fill='url(#s)'/>",
                _ => node + "/>"
            };
            return Document(sheet + body);
        }
    }
}
