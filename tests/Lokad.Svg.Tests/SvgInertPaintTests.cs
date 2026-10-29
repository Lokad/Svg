namespace Lokad.Svg.Tests;

/// <summary>Original inert-server inputs, shared by canonical rendering and prepared-lifetime checks.</summary>
[Trait("Layer", "Rendering")]
public sealed class SvgInertPaintTests
{
    public static IEnumerable<object[]> PaintCases()
    {
        foreach (string server in new[] { "linear", "radial", "empty-pattern", "zero-pattern", "hidden-pattern" })
        foreach (string shape in new[] { "rect", "rounded", "circle", "ellipse", "path", "curve", "cubic", "polygon", "polyline", "line" })
        foreach (bool fill in new[] { false, true })
        foreach (string context in new[] { "direct", "use", "symbol", "mask", "marker", "pattern" })
        foreach (string paint in new[] { "red", "url(#l)", "url(#r)", "url(#p)" })
        {
            // Nested pattern paints retain their explicit support restriction.
            if (context == "pattern" && (server.Contains("pattern", StringComparison.Ordinal) || paint == "url(#p)")) continue;
            yield return [server, shape, fill, context, paint];
        }
    }

    public static IEnumerable<object[]> TemplateCases()
    {
        foreach (string server in new[] { "linear", "radial", "empty-pattern", "zero-pattern", "hidden-pattern" })
        foreach (bool fill in new[] { false, true })
        foreach (bool objectBox in new[] { false, true })
        foreach (string consumer in new[] { "direct", "use", "symbol", "mask" })
        foreach (bool forward in new[] { false, true })
        foreach (string map in new[] { "translate(1.5 .5)", "rotate(7 16 16)", "scale(.75)" })
        foreach (string effects in new[] { "clip-path='url(#c)' mask='url(#m)' opacity='.6'", "filter='url(#f)'", "marker-end='url(#mark)' paint-order='stroke fill markers'" })
            yield return [server, fill, objectBox, consumer, forward, map, effects];
    }

    [Theory]
    [MemberData(nameof(PaintCases))]
    public void Inert_paint_servers_preserve_the_independent_fill_or_stroke(string server, string shape, bool fill, string context, string paint)
        => AssertSamePixels(PaintInput(server, shape, fill, context, paint, "none"), PaintInput(server, shape, fill, context, paint, "url(#e)"));

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public void Inert_template_paints_preserve_maps_effects_and_forward_replay(string server, bool fill, bool objectBox, string consumer, bool forward, string map, string effects)
        => AssertSamePixels(TemplateInput(server, fill, objectBox, consumer, forward, map, effects, "none"), TemplateInput(server, fill, objectBox, consumer, forward, map, effects, "url(#e)"));

    private static void AssertSamePixels(byte[] control, byte[] input)
    {
        byte[] expected = SvgRasterTestHelper.RenderPixels(control, out int expectedWidth, out int expectedHeight);
        byte[] actual = SvgRasterTestHelper.RenderPixels(input, out int actualWidth, out int actualHeight);
        Assert.Equal((expectedWidth, expectedHeight), (actualWidth, actualHeight));
        Assert.Equal(expected, actual);
    }

    internal static byte[] PaintInput(string server, string shape, bool fill, string context, string paint, string inert)
    {
        const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        const string tile = "<rect width='2' height='4' fill='red'/><rect x='2' width='2' height='4' fill='blue'/>";
        string resources = "<linearGradient id='l'>" + stops + "</linearGradient><radialGradient id='r'>" + stops + "</radialGradient><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'>" + tile + "</pattern>";
        string empty = server switch
        {
            "linear" => "<linearGradient id='e'/>",
            "radial" => "<radialGradient id='e'/>",
            "empty-pattern" => "<pattern id='e' patternUnits='userSpaceOnUse' width='4' height='4'/>",
            "zero-pattern" => "<pattern id='e' patternUnits='userSpaceOnUse' width='0' height='4'>" + tile + "</pattern>",
            _ => "<pattern id='e' patternUnits='userSpaceOnUse' width='4' height='4' display='none'>" + tile + "</pattern>"
        };
        string attributes = "fill='" + (fill ? inert : paint) + "' stroke='" + (fill ? paint : inert) + "' stroke-width='3'";
        string node = shape switch
        {
            "rect" => "<rect x='8' y='8' width='16' height='16' " + attributes + "/>",
            "rounded" => "<rect x='8' y='8' width='16' height='16' rx='4' " + attributes + "/>",
            "circle" => "<circle cx='16' cy='16' r='8' " + attributes + "/>",
            "ellipse" => "<ellipse cx='16' cy='16' rx='9' ry='6' " + attributes + "/>",
            "polygon" => "<polygon points='8 8 24 8 24 24 8 24' " + attributes + "/>",
            "polyline" => "<polyline points='8.0 8 24.0 8 24 24.0 8 24' " + attributes + "/>",
            "line" => "<line x1='8' y1='8' x2='24' y2='24' " + attributes + "/>",
            "curve" => "<path d='M8 8Q24 8 24 24Q8 24 8 8Z' " + attributes + "/>",
            "cubic" => "<path d='M8 8C24 8 8 24 24 24L8 24Z' " + attributes + "/>",
            _ => "<path d='M8 8H24V24H8Z' " + attributes + "/>"
        };
        string body = context switch
        {
            "use" => "<defs>" + node.Insert(node.IndexOf(' '), " id='source'") + "</defs><use href='#source'/>",
            "symbol" => "<defs><symbol id='source'>" + node + "</symbol></defs><use href='#source'/>",
            "mask" => "<defs><mask id='host' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + node + "</mask></defs><rect width='32' height='32' fill='green' mask='url(#host)'/>",
            "marker" => "<defs><marker id='host' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' refX='0' refY='0' orient='0' overflow='visible'>" + node + "</marker></defs><path d='M0 0L1 0' fill='none' marker-end='url(#host)'/>",
            "pattern" => "<defs><pattern id='host' patternUnits='userSpaceOnUse' width='32' height='32'>" + node + "</pattern></defs><rect width='32' height='32' fill='url(#host)'/>",
            _ => node
        };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resources + empty + "</defs>" + body + "</svg>");
    }

    internal static byte[] TemplateInput(string server, bool fill, bool objectBox, string consumer, bool forward, string map, string effects, string inert)
    {
        string units = objectBox ? "objectBoundingBox" : "userSpaceOnUse";
        string empty = server switch
        {
            "linear" => "<linearGradient id='base' gradientUnits='" + units + "' x1='0' y1='0' x2='1' y2='0'/>",
            "radial" => "<radialGradient id='base' gradientUnits='" + units + "' cx='.5' cy='.5' r='.5'/>",
            _ => "<pattern id='base' patternUnits='" + units + "' width='" + (server == "zero-pattern" ? "0" : "4") + "' height='4'" + (server == "hidden-pattern" ? " display='none'" : "") + ">" + (server == "empty-pattern" ? "" : "<rect width='4' height='4' fill='blue'/>") + "</pattern>"
        };
        string kind = server == "linear" ? "linearGradient" : server == "radial" ? "radialGradient" : "pattern";
        string alias = "<" + kind + " id='e' href='#base'/>";
        const string resources = "<clipPath id='c'><rect x='4' y='4' width='22' height='22'/></clipPath><mask id='m' mask-type='alpha'><rect width='32' height='32' fill='white'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter><marker id='mark' markerUnits='userSpaceOnUse' markerWidth='3' markerHeight='3' orient='auto'><path d='M0 0L3 1L0 3Z' fill='blue'/></marker>";
        bool markers = effects.StartsWith("marker-", StringComparison.Ordinal);
        string node = "<path d='M8 8Q24 8 24 24Q8 24 8 8Z' fill='" + (fill ? inert : "red") + "' stroke='" + (fill ? "red" : inert) + "' stroke-width='2' transform='" + map + "' " + (markers ? effects : "") + "/>";
        string body = consumer switch
        {
            "use" => "<defs>" + node.Insert(node.IndexOf(' '), " id='source'") + "</defs><use href='#source'/>",
            "symbol" => "<defs><symbol id='source'>" + node + "</symbol></defs><use href='#source'/>",
            "mask" => "<defs><mask id='host' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'>" + node + "</mask></defs><rect width='32' height='32' fill='green' mask='url(#host)'/>",
            _ => node
        };
        if (!markers) body = "<g " + effects + ">" + body + "</g>";
        string definitions = "<defs>" + (forward ? alias + empty : empty + alias) + "</defs>";
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + resources + "</defs>" + (forward && consumer != "use" ? body + definitions : definitions + body) + "</svg>");
    }
}
