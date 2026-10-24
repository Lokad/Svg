namespace Lokad.Svg.Tests;

public sealed class SvgShaderHairlineTests
{
    public static IEnumerable<object[]> NativeThinPaints()
    {
        foreach (string paint in new[] { "linear-flat", "linear", "radial", "pattern-flat", "pattern" })
        foreach (string shape in new[] { "rect", "line", "polyline", "quadratic", "cubic" })
        foreach ((string width, string alpha, string expectedAlpha) in new[]
        {
            (".25", "1", "0.24705882352941178"),
            (".25", ".75", "0.1843137254901961"),
            (".5", "1", "0.4980392156862745"),
            (".5", ".75", "0.37254901960784315")
        })
        foreach (string fill in new[] { "none", "red" })
            yield return [paint, shape, width, alpha, expectedAlpha, fill];
    }

    [Theory]
    [MemberData(nameof(NativeThinPaints))]
    public void Thin_shader_strokes_preserve_native_hairline_coverage_and_paint_alpha(string paint, string shape, string width, string alpha, string expectedAlpha, string fill)
    {
        string resource = paint switch
        {
            "linear-flat" => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "linear" => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='20'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            "pattern-flat" => "<pattern id='p' patternUnits='userSpaceOnUse' width='16' height='16'><rect width='16' height='16' fill='blue'/></pattern>",
            _ => "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>"
        };
        string element = shape switch
        {
            "rect" => "<path d='M4 4H24V24H4Z' ",
            "line" => "<line x1='4' y1='6' x2='26' y2='23' ",
            "polyline" => "<polyline points='4,24 4,4 24,4 24,24' ",
            "quadratic" => "<path d='M4 24Q4 4 24 4' ",
            _ => "<path d='M4 24C4 4 24 4 24 24' "
        };
        Assert.Equal(Render("1", expectedAlpha), Render(width, alpha));

        byte[] Render(string strokeWidth, string strokeAlpha)
        {
            string leaf = element + "fill='" + fill + "' stroke='url(#p)' stroke-width='" + strokeWidth + "' stroke-opacity='" + strokeAlpha + "'/>";
            return SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resource + "</defs><g transform='translate(.5 .25)'>" + leaf + "</g></svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        }
    }
}
