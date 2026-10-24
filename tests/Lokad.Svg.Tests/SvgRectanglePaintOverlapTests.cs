namespace Lokad.Svg.Tests;

public sealed class SvgRectanglePaintOverlapTests
{
    public static IEnumerable<object[]> OverlapCases()
    {
        foreach (string paint in new[] { "solid", "linear", "radial", "pattern", "linear-colour", "radial-colour", "pattern-colour" })
        foreach (string strokeAlpha in new[] { "1", ".5", ".75" })
        foreach (string fillAlpha in new[] { "1", ".5" })
        foreach (string fill in new[] { "red", "url(#q)" })
        foreach (string pose in new[] { "", "translate(.5 .25)", "scale(.75 1.1)" })
        foreach (double scale in new[] { 1d, 1.5d })
        foreach (string consumer in new[] { "direct", "use", "symbol" })
            if (paint != "solid" || fill != "url(#q)" || consumer == "direct"
                || consumer == "use" && pose != "scale(.75 1.1)")
                yield return [paint, strokeAlpha, fillAlpha, fill, pose, scale, consumer];
    }

    [Theory]
    [MemberData(nameof(OverlapCases))]
    public void Combined_rectangle_paints_match_native_equivalent_ordered_draws(string paint, string strokeAlpha, string fillAlpha, string fill, string pose, double scale, string consumer)
    {
        string resource = paint switch
        {
            "solid" => "",
            "linear" => "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
            "pattern" => "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='32' height='32' fill='blue'/></pattern>",
            "linear-colour" => "<linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial-colour" => "<radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' width='5' height='5' patternUnits='userSpaceOnUse'><rect width='5' height='5' fill='blue'/><rect width='2.5' height='5' fill='red'/></pattern>"
        };
        const string dimensions = "x='4' y='4' width='20' height='20'";
        string stroke = paint == "solid" ? "blue" : "url(#p)";
        string shape = "<rect " + dimensions + " fill='" + fill + "' fill-opacity='" + fillAlpha + "' stroke='" + stroke + "' stroke-opacity='" + strokeAlpha + "' stroke-width='2'/>";
        string separate = "<rect " + dimensions + " fill='" + fill + "' fill-opacity='" + fillAlpha + "'/><rect " + dimensions + " fill='none' stroke='" + stroke + "' stroke-opacity='" + strokeAlpha + "' stroke-width='2'/>";
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        string definitions = "", actual = shape;
        if (consumer == "use") { definitions = shape.Replace("/>", " id='s'/>", StringComparison.Ordinal); actual = "<use href='#s'/>"; }
        else if (consumer == "symbol") { definitions = "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>"; actual = "<use href='#s' width='32' height='32'/>"; }
        Assert.Equal(Render(separate, ""), Render(actual, definitions));

        byte[] Render(string body, string source) => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resource + "<linearGradient id='q'><stop stop-color='yellow'/><stop offset='1' stop-color='cyan'/></linearGradient>" + source + "</defs><g transform='" + pose + "'>" + body + "</g></svg>"), options, CancellationToken.None).ToArray();
    }

    public static IEnumerable<object[]> LayerCases()
    {
        foreach (bool mask in new[] { false, true })
        foreach (string opacity in new[] { "1", ".5", ".75" })
        foreach (string fill in new[] { "green", "url(#q)" })
        foreach (string width in new[] { ".5", "2" })
        foreach (double scale in new[] { 1d, 1.5d })
        foreach (string consumer in new[] { "direct", "use", "symbol" })
            yield return [mask, opacity, fill, width, scale, consumer];
    }

    [Theory]
    [MemberData(nameof(LayerCases))]
    public void Fractional_stroke_drawing_bounds_survive_group_opacity_and_masks(bool mask, string opacity, string fill, string width, double scale, string consumer)
    {
        const string dimensions = "x='4' y='4' width='20' height='20'";
        string shape = "<rect " + dimensions + " fill='" + fill + "' stroke='url(#p)' stroke-width='" + width + "' stroke-opacity='.75'/>";
        string separate = "<rect " + dimensions + " fill='" + fill + "'/><rect " + dimensions + " fill='none' stroke='url(#p)' stroke-width='" + width + "' stroke-opacity='.75'/>";
        string definitions = "", actual = shape;
        if (consumer == "use") { definitions = shape.Replace("/>", " id='s'/>", StringComparison.Ordinal); actual = "<use href='#s'/>"; }
        else if (consumer == "symbol") { definitions = "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>"; actual = "<use href='#s' width='32' height='32'/>"; }
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.Equal(Render(separate, ""), Render(actual, definitions));

        byte[] Render(string content, string source)
        {
            string body = "<g transform='scale(.75 1.1)' opacity='" + opacity + "'>" + content + "</g>";
            if (mask) body = "<g mask='url(#m)'>" + body + "</g>";
            return SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><linearGradient id='q'><stop stop-color='yellow'/><stop offset='1' stop-color='cyan'/></linearGradient><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.5'/></mask>" + source + "</defs>" + body + "</svg>"), options, CancellationToken.None).ToArray();
        }
    }
}
