namespace Lokad.Svg.Tests;

[Trait("Layer", "Rendering")]
public sealed class SvgZeroRadiusPaintTests
{
    public static IEnumerable<object[]> SolidContours()
    {
        foreach (string corners in new[] { "rx='0' ry='4'", "rx='4' ry='0'" })
        foreach (string width in new[] { ".5", "1" })
        foreach (string pose in new[] { "", "translate(.5 .25)", "scale(.75 1.1)", "rotate(15 16 16)" })
        foreach (string consumer in new[] { "direct", "use", "symbol" })
        foreach (double scale in new[] { 1d, 1.5d })
        foreach (string opacity in new[] { "1", ".5", ".75" })
        foreach (string fillOpacity in new[] { "1", ".5" })
            yield return [corners, width, pose, consumer, scale, opacity, fillOpacity];
    }

    [Theory]
    [MemberData(nameof(SolidContours))]
    public void A_single_zero_radius_preserves_the_native_path_start(string corners, string width, string pose, string consumer, double scale, string opacity, string fillOpacity)
    {
        var (control, input, options) = SolidInputs(corners, width, pose, consumer, scale, opacity, fillOpacity);
        SvgRasterTestHelper.AssertPixelsEqual(control, input, options);
    }

    [Theory]
    [InlineData("rx='0' ry='4'", ".5", "translate(.5 .25)", "use", 1.5, ".75", ".5")]
    [InlineData("rx='4' ry='0'", "1", "rotate(15 16 16)", "symbol", 1d, ".5", "1")]
    [InlineData("rx='0' ry='4'", "1", "scale(.75 1.1)", "direct", 1.5, "1", ".5")]
    public void Complete_png_zero_radius_paths_preserve_the_native_start(string corners, string width, string pose, string consumer, double scale, string opacity, string fillOpacity)
    {
        var (control, input, options) = SolidInputs(corners, width, pose, consumer, scale, opacity, fillOpacity);
        SvgRasterTestHelper.AssertPngEqual(control, input, options);
    }

    private static (byte[] Control, byte[] Input, SvgRenderOptions Options) SolidInputs(string corners, string width, string pose, string consumer, double scale, string opacity, string fillOpacity)
    {
        string style = "fill='red' fill-opacity='" + fillOpacity + "' stroke='blue' stroke-width='" + width + "' stroke-opacity='" + opacity + "'";
        string rectangle = "<rect x='4' y='4' width='20' height='20' " + style + " " + corners + "/>";
        string path = "<path d='M4 4H24V24H4Z' " + style + "/>";
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        return (Input(path), Input(rectangle), options);

        byte[] Input(string shape)
        {
            string definitions = "", body = shape;
            if (consumer == "use") { definitions = shape.Replace("/>", " id='s'/>", StringComparison.Ordinal); body = "<use href='#s'/>"; }
            else if (consumer == "symbol") { definitions = "<symbol id='s' viewBox='0 0 32 32'>" + shape + "</symbol>"; body = "<use href='#s' width='32' height='32'/>"; }
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs><g transform='" + pose + "'>" + body + "</g></svg>");
        }
    }

    public static IEnumerable<object[]> SymbolStrokeMaps()
    {
        foreach (string units in new[] { "box", "user" })
        foreach (string shape in new[] { "zero-rect", "rounded-rect", "ellipse", "path", "polygon" })
        foreach (string pose in new[] { "", "translate(.5 .25)", "scale(.75 1.1)", "rotate(15 16 16)" })
        foreach (string box in new[] { "identity", "nonuniform", "offset" })
        foreach (double scale in new[] { 1d, 1.5d })
            if (box != "nonuniform" || shape is not ("rounded-rect" or "ellipse") || pose == "rotate(15 16 16)")
                yield return [units, shape, pose, box, scale];
    }

    [Theory]
    [MemberData(nameof(SymbolStrokeMaps))]
    public void Symbol_linear_strokes_keep_their_source_and_instance_maps(string units, string shape, string pose, string box, double scale)
    {
        var (control, input, options) = SymbolInputs(units, shape, pose, box, scale);
        SvgRasterTestHelper.AssertPixelsEqual(control, input, options);
    }

    [Theory]
    [InlineData("user", "polygon", "translate(.5 .25)", "offset", 1.5)]
    [InlineData("box", "ellipse", "rotate(15 16 16)", "nonuniform", 1d)]
    public void Complete_png_symbol_strokes_preserve_source_and_instance_maps(string units, string shape, string pose, string box, double scale)
    {
        var (control, input, options) = SymbolInputs(units, shape, pose, box, scale);
        SvgRasterTestHelper.AssertPngEqual(control, input, options);
    }

    private static (byte[] Control, byte[] Input, SvgRenderOptions Options) SymbolInputs(string units, string shape, string pose, string box, double scale)
    {
        string gradient = "<linearGradient id='p'" + (units == "user" ? " gradientUnits='userSpaceOnUse' x1='2' y1='2' x2='28' y2='24'" : " x2='1' y2='1'") + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        const string style = "fill='green' stroke='url(#p)' stroke-width='2' stroke-opacity='.75'";
        string leaf = shape switch
        {
            "zero-rect" => "<rect x='4' y='4' width='20' height='20' rx='0' ry='4' " + style + "/>",
            "rounded-rect" => "<rect x='4' y='4' width='20' height='20' rx='4' ry='4' " + style + "/>",
            "ellipse" => "<ellipse cx='14' cy='14' rx='10' ry='8' " + style + "/>",
            "path" => "<path d='M4 4H24V24H4Z' " + style + "/>",
            _ => "<polygon points='4,4 24,4 24,24 4,24' " + style + "/>"
        };
        (string viewBox, string width, string height, string x, string y) = box switch
        {
            "nonuniform" => ("0 0 32 32", "40", "24", "0", "0"),
            "offset" => ("-4 -4 40 40", "28", "28", "2", "1"),
            _ => ("0 0 32 32", "32", "32", "0", "0")
        };
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
        return (Input(false), Input(true), options);

        byte[] Input(bool instance)
        {
            string definition = instance ? "<symbol id='s' viewBox='" + viewBox + "' preserveAspectRatio='none'>" + leaf + "</symbol>" : "";
            string placement = "x='" + x + "' y='" + y + "' width='" + width + "' height='" + height + "'";
            string body = instance ? "<use href='#s' " + placement + "/>" : "<svg " + placement + " viewBox='" + viewBox + "' preserveAspectRatio='none'>" + leaf + "</svg>";
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + gradient + definition + "</defs><g transform='" + pose + "' opacity='.5'>" + body + "</g></svg>");
        }
    }
}
