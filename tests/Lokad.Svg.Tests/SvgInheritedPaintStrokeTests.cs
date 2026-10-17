using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.Tests;

public sealed class SvgInheritedPaintStrokeTests
{
    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("pattern")]
    public void Inherited_open_path_paint_matches_explicit_stroke(string paint)
    {
        string definition = Definition(paint);
        const string path = "<path d='M4.5 22 C6.5 2 25.5 2 28.5 22'/>";
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='28'><defs>" + definition + "</defs>";
        string suffix = "</svg>";
        string inherited = prefix + "<g fill='none' stroke='url(#p)' stroke-width='4' stroke-linecap='round'>" + path + "</g>" + suffix;
        string explicitPaint = prefix + "<g fill='none' stroke-width='4' stroke-linecap='round'>" + path.Replace("<path ", "<path stroke='url(#p)' ", StringComparison.Ordinal) + "</g>" + suffix;

        Assert.Equal(Render(explicitPaint), Render(inherited));
    }

    [Fact]
    public void Child_solid_and_none_replace_inherited_paint_server()
    {
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='28'><defs>" + Definition("linear") + "</defs>";
        const string paths = "<path d='M4 4L28 12' stroke='orange'/><path d='M4 20L28 24' stroke='none'/>";
        string inherited = prefix + "<g fill='none' stroke='url(#p)' stroke-width='4'>" + paths + "</g></svg>";
        string explicitPaint = prefix + "<g fill='none' stroke-width='4'>" + paths + "</g></svg>";

        Assert.Equal(Render(explicitPaint), Render(inherited));
    }

    [Fact]
    public void Zero_width_does_not_resolve_an_inherited_missing_paint()
    {
        const string inherited = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><g fill='none' stroke='url(#missing)' stroke-width='0'><path d='M1 1L7 7'/></g></svg>";
        const string empty = "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'/>";

        Assert.Equal(Render(empty), Render(inherited));
    }

    private static byte[] Render(string svg)
        => SvgConverter.ConvertToPng(TextEncoding.UTF8.GetBytes(svg), SvgRenderOptions.Default, CancellationToken.None).ToArray();

    private static string Definition(string paint) => paint switch
    {
        "linear" => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='32' y2='0'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
        "radial" => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='14' r='18'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
        "pattern" => "<pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='red'/><rect width='2' height='2' fill='blue'/></pattern>",
        _ => throw new ArgumentOutOfRangeException(nameof(paint))
    };
}
