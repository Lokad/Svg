using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> GradientTemplateMaps()
    {
        foreach (string parent in new[] { "linearGradient", "radialGradient" })
        foreach (string child in new[] { "linearGradient", "radialGradient" })
        foreach (string transform in new[] { "translate(.25 0)", "scale(.5)", "rotate(90 .5 .5)", "matrix(1 0 .5 1 0 0)" })
        foreach (bool replacement in new[] { false, true })
        foreach (bool vector in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (string context in new[] { "direct", "use", "symbol", "pattern" })
            yield return [parent, child, transform, replacement, vector, forward, context];
    }

    [Theory]
    [MemberData(nameof(GradientTemplateMaps))]
    public async Task Gradient_aliases_apply_the_effective_local_map_once(string parent, string child, string transform, bool replacement, bool vector, bool forward, string context)
    {
        const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        const string childTransform = "translate(0 .25)";
        string attributes = vector ? child == "linearGradient" ? "x1='0' y1='0' x2='1' y2='0'" : "cx='.5' cy='.5' r='.5'" : "";
        string original = "<" + parent + " id='base' gradientTransform='" + transform + "'>" + stops + "</" + parent + ">";
        string alias = "<" + child + " id='g' href='#base' " + attributes + (replacement ? " gradientTransform='" + childTransform + "'" : "") + "/>";
        byte[] input = Input(forward ? alias + original : original + alias);
        byte[] control = Input("<" + child + " id='g' gradientTransform='" + (replacement ? childTransform : transform) + "' " + attributes + ">" + stops + "</" + child + ">");
        byte[] expected = Render(control);
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());

        byte[] Input(string definitions)
        {
            const string rectangle = "<rect x='4' y='4' width='24' height='24' fill='url(#g)'/>";
            string body = context switch
            {
                "use" => "<defs>" + rectangle.Replace("<rect", "<rect id='s'", StringComparison.Ordinal) + "</defs><use href='#s'/>",
                "symbol" => "<defs><symbol id='s'>" + rectangle + "</symbol></defs><use href='#s'/>",
                "pattern" => "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='32' height='32'>" + rectangle + "</pattern></defs><rect width='32' height='32' fill='url(#p)'/>",
                _ => rectangle
            };
            return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + body + "</svg>");
        }
    }

    public static IEnumerable<object[]> GradientTemplateConsumers()
    {
        foreach (string kind in new[] { "linearGradient", "radialGradient" })
        foreach (bool userSpace in new[] { false, true })
        foreach (bool reflected in new[] { false, true })
        foreach (bool replacement in new[] { false, true })
        foreach (string context in new[] { "curve", "stroke", "text", "textstroke", "mask", "marker" })
        foreach (string spread in new[] { "repeat", "reflect" })
            yield return [kind, userSpace, reflected, replacement, context, spread];
    }

    [Theory]
    [MemberData(nameof(GradientTemplateConsumers))]
    public async Task Gradient_template_maps_survive_native_paint_consumers(string kind, bool userSpace, bool reflected, bool replacement, string context, string spread)
    {
        const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        const string ownStops = "<stop stop-color='green'/><stop offset='1' stop-color='blue'/>";
        string units = userSpace ? "userSpaceOnUse" : "objectBoundingBox";
        string coordinates = kind == "linearGradient"
            ? userSpace ? "x1='4' y1='4' x2='20' y2='12'" : "x1='.1' y1='.1' x2='.8' y2='.3'"
            : userSpace ? "cx='16' cy='16' r='10' fx='14' fy='15' fr='1'" : "cx='.5' cy='.5' r='.45' fx='.4' fy='.45' fr='.05'";
        string transform = reflected
            ? userSpace ? "matrix(-1 0 0 1 32 0)" : "matrix(-1 0 0 1 1 0)"
            : userSpace ? "matrix(1 .2 .3 1 2 0)" : "matrix(1 .2 .3 1 .1 0)";
        string childTransform = userSpace ? "translate(4 0)" : "translate(.2 0)";
        string selectedStops = context is "text" or "mask" or "marker" ? ownStops : stops;
        string parent = "<" + kind + " id='base' gradientUnits='" + units + "' gradientTransform='" + transform + "' spreadMethod='" + spread + "' " + coordinates + ">" + stops + "</" + kind + ">";
        string middle = "<" + kind + " id='middle' href='#base'/>";
        string alias = "<" + kind + " id='g' href='#middle'" + (replacement ? " gradientTransform='" + childTransform + "'" : "") + ">" + (selectedStops == ownStops ? ownStops : "") + "</" + kind + ">";
        string standalone = "<" + kind + " id='g' gradientUnits='" + units + "' gradientTransform='" + (replacement ? childTransform : transform) + "' spreadMethod='" + spread + "' " + coordinates + ">" + selectedStops + "</" + kind + ">";
        string content = context switch
        {
            "curve" => "<path d='M4 16Q4 4 16 4Q28 4 28 16Q28 28 16 28Q4 28 4 16Z' fill='url(#g)'/>",
            "stroke" => "<path d='M4 16Q4 4 16 4Q28 4 28 16Q28 28 16 28Q4 28 4 16Z' fill='none' stroke='url(#g)' stroke-width='3'/>",
            "text" => "<text x='4' y='24' font-family='Example' font-size='20' fill='url(#g)'>A</text>",
            "textstroke" => "<text x='4' y='24' font-family='Example' font-size='20' fill='none' stroke='url(#g)' stroke-width='1'>A</text>",
            "mask" => "<defs><mask id='m'><rect x='4' y='4' width='24' height='24' fill='url(#g)'/></mask></defs><rect x='4' y='4' width='24' height='24' fill='red' mask='url(#m)'/>",
            _ => "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='32' markerHeight='32' refX='0' refY='0'><rect x='4' y='4' width='24' height='24' fill='url(#g)'/></marker></defs><path d='M0 0H1' fill='none' marker-start='url(#m)'/>"
        };
        byte[] input = Document(alias + middle + parent);
        byte[] control = Document(standalone);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(control, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(control, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        if (context is not ("text" or "textstroke")) Assert.Equal(0, provider.RequestCount);

        byte[] Document(string definitions) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs>" + content + "</svg>");
    }

    public static IEnumerable<object[]> GradientFocalInheritance()
    {
        foreach (int parentFields in new[] { 0, 1, 2, 4, 7 })
        foreach (int childFields in new[] { 0, 1, 6 })
        foreach (bool replacement in new[] { false, true })
        foreach (bool forward in new[] { false, true })
        foreach (bool independent in new[] { false, true })
            yield return [parentFields, childFields, replacement, forward, independent];
    }

    [Theory]
    [MemberData(nameof(GradientFocalInheritance))]
    public async Task Radial_full_vectors_inherit_only_declared_focal_fields(int parentFields, int childFields, bool replacement, bool forward, bool independent)
    {
        const string stops = "<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        const string parentTransform = "matrix(1 .2 .3 1 0 0)";
        const string childTransform = "translate(.1 0)";
        string parentFocal = Focal(parentFields, ".35", ".45", ".05");
        string childFocal = Focal(childFields, ".5", ".55", ".1");
        string expectedFocal = Focal(parentFields | childFields,
            (childFields & 1) != 0 ? ".5" : ".35", (childFields & 2) != 0 ? ".55" : ".45", (childFields & 4) != 0 ? ".1" : ".05");
        string parent = "<radialGradient id='base' cx='.4' cy='.5' r='.45' gradientUnits='objectBoundingBox' gradientTransform='" + parentTransform + "' spreadMethod='reflect' " + parentFocal + ">" + stops + "</radialGradient>";
        string middle = "<radialGradient id='middle' href='#base'/>";
        string alias = "<radialGradient id='g' href='#middle' cx='.55' cy='.5' r='.45' " + childFocal
            + (replacement || independent ? " gradientTransform='" + (replacement ? childTransform : parentTransform) + "'" : "")
            + (independent ? " gradientUnits='objectBoundingBox' spreadMethod='reflect'" : "") + ">" + (independent ? stops : "") + "</radialGradient>";
        string standalone = "<radialGradient id='g' cx='.55' cy='.5' r='.45' gradientUnits='objectBoundingBox' spreadMethod='reflect' gradientTransform='" + (replacement ? childTransform : parentTransform) + "' " + expectedFocal + ">" + stops + "</radialGradient>";
        byte[] input = Document(forward ? alias + middle + parent : parent + middle + alias);
        byte[] expected = Render(Document(standalone));
        Assert.Equal(expected, Render(input));
        (var catalog, var provider, _) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(0, provider.RequestCount);

        static string Focal(int fields, string x, string y, string radius)
            => ((fields & 1) != 0 ? " fx='" + x + "'" : "") + ((fields & 2) != 0 ? " fy='" + y + "'" : "") + ((fields & 4) != 0 ? " fr='" + radius + "'" : "");
        static byte[] Document(string definitions) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + definitions + "</defs><rect x='4' y='4' width='24' height='24' fill='url(#g)'/></svg>");
    }
}
