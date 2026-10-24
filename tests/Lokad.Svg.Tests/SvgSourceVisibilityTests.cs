namespace Lokad.Svg.Tests;

public sealed class SvgSourceVisibilityTests
{
    public static IEnumerable<object[]> ShaderSources()
    {
        foreach (string paint in new[] { "linear", "radial", "pattern" })
        foreach (string shape in new[] { "rect", "circle", "path", "polyline" })
        foreach (bool stroke in new[] { false, true })
        foreach (string visibility in new[] { "inherit", "omitted", "visible", "hidden" })
        foreach (string transform in new[] { "", "translate(.5 .25)", "matrix(1 .25 .125 1 0 0)" })
            yield return [paint, shape, stroke, visibility, transform,
                transform.StartsWith("matrix", StringComparison.Ordinal)
                    && (shape == "rect" && !stroke || visibility == "hidden" && (shape == "path" || shape == "polyline" && !stroke))];
    }

    [Theory]
    [MemberData(nameof(ShaderSources))]
    public void Copied_shader_sources_resolve_visibility_in_the_instance(string paint, string shape, bool stroke, string visibility, string transform, bool unsupported)
    {
        string resource = paint switch
        {
            "linear" => "<linearGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='p'><stop stop-color='blue'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' width='32' height='32' patternUnits='userSpaceOnUse'><rect width='32' height='32' fill='blue'/></pattern>"
        };
        string start = shape switch
        {
            "circle" => "<circle cx='14' cy='14' r='12'",
            "path" => "<path d='M2 2H26V26H2Z'",
            "polyline" => "<polyline points='2,2 26,2 14,26'",
            _ => "<rect x='2' y='2' width='24' height='24'"
        };
        string source = start + " id='source' " + (stroke ? "fill='none' stroke='url(#p)' stroke-width='2'" : "fill='url(#p)'")
            + (visibility == "omitted" ? "" : " visibility='" + visibility + "'") + "/>";
        string use = "<use href='#source'" + (transform.Length == 0 ? "" : " transform='" + transform + "'") + "/>";
        string expectedBody = visibility == "visible" ? "<g visibility='visible'>" + source + "</g>" + use : "<defs>" + source + "</defs>" + use;
        byte[] expectedInput = Document("<defs>" + resource + "</defs>" + expectedBody);
        byte[] expected = [];
        if (unsupported) Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(expectedInput, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
        else expected = SvgConverter.ConvertToPng(expectedInput, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        foreach (bool forward in new[] { false, true })
        {
            string body = "<g visibility='hidden'>" + source + "</g>" + use;
            string resources = "<defs>" + resource + "</defs>";
            byte[] input = Document(forward ? body + resources : resources + body);
            if (unsupported) Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
            else Assert.Equal(expected, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }

        static byte[] Document(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
    }
}
