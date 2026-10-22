using Lokad.Svg.Encoding;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LengthTokenCases()
    {
        foreach ((string kind, string value, string unit) in new[]
        {
            ("root", "8px", "px"), ("root", "6pt", "pt"),
            ("rect", "4px", "px"), ("rect", "50%", "%"),
            ("circle", "2px", "px"), ("circle", "25%", "%"), ("ellipse", "2px", "px"),
            ("line", "4px", "px"), ("nested", "4px", "px"), ("nested", "50%", "%"),
            ("image", "4px", "px"), ("image", "50%", "%"), ("use", "2px", "px"), ("use", "25%", "%"),
            ("pattern", "4px", "px"), ("mask", "8px", "px"), ("mask", "100%", "%"),
            ("filter", "8px", "px"), ("filter", "100%", "%"), ("gradient", "50%", "%"), ("stop", "100%", "%"),
            ("stroke", "2px", "px"), ("dash-offset", "1px", "px"), ("dash-array", "2px", "px"),
            ("marker", "2px", "px"), ("marker", "25%", "%"), ("marker", "1.5pt", "pt"),
            ("marker", ".01em", "em"), ("marker", ".01ex", "ex"),
            ("font", "200px", "px"), ("font", "150pt", "pt"), ("letter-spacing", "1px", "px"),
            ("word-spacing", "1px", "px"), ("text-x", "1px", "px"), ("text-path", "1px", "px"), ("text-path", "25%", "%")
        }) yield return [kind, value, unit];
    }

    [Theory]
    [MemberData(nameof(LengthTokenCases))]
    public async Task Length_numbers_and_units_remain_one_token_across_consumers(string kind, string value, string unit)
    {
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(catalog, new[] { new SvgFontFaceRequest("Example", 400, false) }, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(Input(value), options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(Input(" " + value + " "), options, CancellationToken.None).ToArray());
        using (SvgPreparedDocument control = await SvgPreparedDocument.CreateAsync(Input(value), catalog, provider, SvgRenderOptions.Default, CancellationToken.None))
            Assert.Equal(expected, control.ConvertToPng(CancellationToken.None).ToArray());
        foreach (string gap in new[] { " ", "\t", "\n", "\u00a0", "\u2003", "\u202f", "\u3000" })
        {
            byte[] input = Input(value[..^unit.Length] + gap + unit);
            SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
            Assert.True(error.ErrorCode is SvgErrorCode.MalformedInput or SvgErrorCode.UnsupportedFeature);
            error = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
            Assert.True(error.ErrorCode is SvgErrorCode.MalformedInput or SvgErrorCode.UnsupportedFeature);
            if (gap[0] < 128) continue;
            Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(Input(gap + value + gap), options, CancellationToken.None));
        }

        byte[] Input(string token)
        {
            string text = "<text x='0' y='7' font-family='Example' font-size='200'>A</text>";
            string source = kind switch
            {
                "root" => "<svg xmlns='http://www.w3.org/2000/svg' width='" + token + "' height='8'><rect width='4' height='4' fill='red'/></svg>",
                "rect" => "<rect width='" + token + "' height='4'/>",
                "circle" => "<circle cx='4' cy='4' r='" + token + "'/>",
                "ellipse" => "<ellipse cx='4' cy='4' rx='" + token + "' ry='2'/>",
                "line" => "<line x1='0' y1='4' x2='" + token + "' y2='4' stroke='red'/>",
                "nested" => "<svg width='" + token + "' height='8'><rect width='4' height='4'/></svg>",
                "image" => "<image width='" + token + "' height='4' href='data:image/png;base64," + Convert.ToBase64String(PngEncoder.Encode(1, 1, [255, 0, 0, 255], CancellationToken.None)) + "'/>",
                "use" => "<defs><rect id='s' width='2' height='2'/></defs><use href='#s' x='" + token + "'/>",
                "pattern" => "<defs><pattern id='p' width='" + token + "' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4'/></pattern></defs><rect width='8' height='8' fill='url(#p)'/>",
                "mask" => "<defs><mask id='m' maskUnits='userSpaceOnUse' width='" + token + "' height='8'><rect width='8' height='8' fill='white'/></mask></defs><rect width='8' height='8' mask='url(#m)'/>",
                "filter" => "<defs><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='" + token + "' height='8'><feFlood flood-color='red'/></filter></defs><rect width='4' height='4' filter='url(#f)'/>",
                "gradient" => "<defs><linearGradient id='p' x2='" + token + "'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
                "stop" => "<defs><linearGradient id='p'><stop stop-color='red'/><stop offset='" + token + "' stop-color='blue'/></linearGradient></defs><rect width='8' height='8' fill='url(#p)'/>",
                "stroke" or "dash-offset" or "dash-array" => "<path d='M1 1H7V7' fill='none' stroke='red' " + (kind == "stroke" ? "stroke-width" : kind == "dash-offset" ? "stroke-dashoffset" : "stroke-dasharray") + "='" + token + "'/>",
                "marker" => "<defs><marker id='m' markerWidth='" + token + "' markerHeight='2' markerUnits='userSpaceOnUse' style='font-family:Example;font-size:200px'><rect width='2' height='2'/></marker></defs><path d='M1 1H7' marker-start='url(#m)'/>",
                "font" => text.Replace("200", token, StringComparison.Ordinal),
                "letter-spacing" or "word-spacing" => text.Replace("<text ", "<text " + kind + "='" + token + "' ", StringComparison.Ordinal),
                "text-x" => text.Replace("x='0'", "x='" + token + "'", StringComparison.Ordinal),
                _ => "<defs><path id='p' d='M0 7H8'/></defs><text font-family='Example' font-size='200'><textPath href='#p' startOffset='" + token + "'>A</textPath></text>"
            };
            return kind == "root" ? System.Text.Encoding.UTF8.GetBytes(source) : Document(source);
        }
    }
}
