namespace Lokad.Svg.Tests;

public sealed class SvgRectangleShaderInstanceTests
{
    [Theory]
    [InlineData("linear", "source-use-paint")]
    [InlineData("linear", "use-source-paint")]
    [InlineData("linear", "source-paint-use")]
    [InlineData("radial", "source-use-paint")]
    [InlineData("radial", "use-source-paint")]
    [InlineData("radial", "source-paint-use")]
    [InlineData("pattern", "source-use-paint")]
    [InlineData("pattern", "use-source-paint")]
    [InlineData("pattern", "source-paint-use")]
    public void Referenced_clip_sources_keep_their_fill_contour_and_ignore_shader_strokes(string paint, string order)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='32' height='32' fill='blue'/></pattern>"
        };
        const string source = "<defs><rect id='s' x='4' y='4' width='16' height='16' fill='none' stroke='url(#p)' stroke-width='2'/></defs>";
        const string use = "<defs><clipPath id='c'><use href='#s'/></clipPath></defs><rect width='32' height='32' fill='blue' clip-path='url(#c)'/>";
        string resources = "<defs>" + resource + "</defs>";
        string body = order == "source-use-paint" ? source + use + resources : order == "use-source-paint" ? use + source + resources : source + resources + use;
        Assert.Equal(Render("<rect x='4' y='4' width='16' height='16' fill='blue'/>"), Render(body));

        static byte[] Render(string content) => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    public static IEnumerable<object[]> DegenerateStrokeInstances()
    {
        foreach (string paint in new[] { "linear", "radial", "pattern" })
        foreach (string dimensions in new[] { "width='0' height='20'", "width='20' height='0'", "width='20' height='20'" })
        foreach (string consumer in new[] { "use", "symbol" })
        foreach (double scale in new[] { 1d, 1.5d })
            yield return [paint, dimensions, consumer, scale];
    }

    [Theory]
    [MemberData(nameof(DegenerateStrokeInstances))]
    public void Empty_and_oversized_shader_strokes_keep_direct_source_semantics(string paint, string dimensions, string consumer, double scale)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='32' height='32' fill='blue'/></pattern>"
        };
        string shape = "<rect x='4' y='4' " + dimensions + " fill='none' stroke='url(#p)' stroke-width='24'/>";
        string source = consumer == "symbol" ? "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>" : shape.Replace("/>", " id='s'/>", StringComparison.Ordinal);
        string use = consumer == "symbol" ? "<use href='#s' width='32' height='32'/>" : "<use href='#s'/>";
        string direct = consumer == "symbol" ? "<svg width='32' height='32' viewBox='0 0 32 32'>" + shape + "</svg>" : shape;
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] expected = Render("<defs>" + resource + "</defs><g transform='translate(.5 .25)'>" + direct + "</g>");
        Assert.Equal(expected, Render("<defs>" + resource + source + "</defs><g transform='translate(.5 .25)'>" + use + "</g>"));

        byte[] Render(string body) => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>"), options, CancellationToken.None).ToArray();
    }

    public static IEnumerable<object[]> UniformStrokeInstances()
    {
        foreach (string paint in new[] { "linear", "radial", "pattern" })
        foreach (string width in new[] { ".5", "1", "1.5", "2", "3" })
        foreach (string transform in new[] { "", "translate(.25 .5)", "translate(.5 .25)", "translate(.75 .75)" })
        foreach (string opacity in new[] { "1", ".5" })
        foreach (string consumer in new[] { "direct", "use", "symbol" })
            yield return [paint, width, transform, opacity, consumer];
    }

    [Theory]
    [MemberData(nameof(UniformStrokeInstances))]
    public void Rectangle_shader_strokes_retain_native_frame_coverage(string paint, string width, string transform, string opacity, string consumer)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='32' height='32' fill='blue'/></pattern>"
        };
        byte[] expected = SvgConverter.ConvertToPng(Input(false, false), SvgRenderOptions.Default, CancellationToken.None).ToArray();
        foreach (bool forward in new[] { false, true })
            Assert.Equal(expected, SvgConverter.ConvertToPng(Input(true, forward), SvgRenderOptions.Default, CancellationToken.None).ToArray());

        byte[] Input(bool shader, bool forward)
        {
            string shape = "<rect x='4' y='4' width='20' height='20' fill='none' stroke='" + (shader ? "url(#p)" : "blue")
                + "' stroke-width='" + width + "' stroke-opacity='" + opacity + "'/>";
            string map = transform.Length == 0 ? "" : " transform='" + transform + "'";
            string body = consumer switch
            {
                "use" => "<defs>" + shape.Replace("/>", " id='source'/>", StringComparison.Ordinal) + "</defs><use href='#source'" + map + "/>",
                "symbol" => "<defs><symbol id='source' viewBox='0 0 32 32'>" + shape + "</symbol></defs><use href='#source' width='32' height='32'" + map + "/>",
                _ => "<g" + map + ">" + shape + "</g>"
            };
            string resources = "<defs>" + resource + "</defs>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + resources : resources + body) + "</svg>");
        }
    }
}
