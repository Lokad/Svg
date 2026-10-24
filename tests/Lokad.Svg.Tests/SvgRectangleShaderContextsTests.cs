namespace Lokad.Svg.Tests;

public sealed class SvgRectangleShaderContextsTests
{
    public static IEnumerable<object[]> PaintContexts()
    {
        foreach (string paint in new[] { "linear-user", "linear-box", "radial-user", "radial-box", "pattern-user", "pattern-box" })
        foreach (string width in new[] { ".5", "2", "24" })
        foreach (string pose in new[] { "", "translate(.5 .25)", "scale(.75 1.1)" })
        foreach (string consumer in new[] { "direct", "use", "symbol", "group-use", "viewport", "clip", "mask", "filter" })
        foreach (double scale in new[] { 1d, 1.5d })
        foreach (string opacity in new[] { "1", ".5" })
            yield return [paint, width, pose, consumer, scale, opacity];
    }

    [Theory]
    [MemberData(nameof(PaintContexts))]
    public void Shader_instances_preserve_direct_paint_coordinates_and_composition(string paint, string width, string pose, string consumer, double scale, string opacity)
    {
        string resource = paint switch
        {
            "linear-user" => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='32' y2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "linear-box" => "<linearGradient id='p' x2='1' y2='1'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial-user" => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='16'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            "radial-box" => "<radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            "pattern-user" => "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>",
            _ => "<pattern id='p' width='.25' height='.25' viewBox='0 0 8 8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>"
        };
        string fill = width == "2" ? "fill='url(#q)'" : opacity == ".5" ? "fill='green'" : "fill='none'";
        string shape = "<rect x='4' y='4' width='20' height='20' " + fill + " stroke='url(#p)' stroke-width='" + width + "' stroke-opacity='.75'/>";
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        Assert.Equal(SvgConverter.ConvertToPng(Input(false), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(Input(true), options, CancellationToken.None).ToArray());

        byte[] Input(bool instance)
        {
            string extra = "", inner = shape;
            if (instance)
            {
                if (consumer == "symbol") { extra = "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>"; inner = "<use href='#s' width='32' height='32'/>"; }
                else if (consumer == "group-use") { extra = "<g id='s'>" + shape + "</g>"; inner = "<use href='#s'/>"; }
                else if (consumer != "direct") { extra = shape.Replace("/>", " id='s'/>", StringComparison.Ordinal); inner = "<use href='#s'/>"; }
            }
            else if (consumer == "symbol") inner = "<svg width='32' height='32' viewBox='0 0 32 32'>" + shape + "</svg>";
            string body = "<g transform='" + pose + "' opacity='" + opacity + "'>" + inner + "</g>";
            body = consumer switch
            {
                "viewport" => "<svg x='1' y='1' width='30' height='30' viewBox='0 0 32 32'>" + body + "</svg>",
                "clip" => "<g clip-path='url(#c)'>" + body + "</g>",
                "mask" => "<g mask='url(#m)'>" + body + "</g>",
                "filter" => "<g filter='url(#f)'>" + body + "</g>",
                _ => body
            };
            const string effects = "<clipPath id='c'><rect x='2' y='2' width='26' height='26'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.5'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter>";
            const string otherPaint = "<linearGradient id='q'><stop stop-color='yellow'/><stop offset='1' stop-color='cyan'/></linearGradient>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resource + otherPaint + effects + extra + "</defs>" + body + "</svg>");
        }
    }
}
