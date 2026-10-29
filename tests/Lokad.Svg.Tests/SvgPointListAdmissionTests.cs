using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPointListAdmissionTests
{
    public static IEnumerable<object[]> PointLists()
    {
        foreach (string shape in new[] { "polygon", "polyline" })
        foreach (string points in new[] { "", "8 8", "8 8 24 24", "8 8 8 8 24 8 24 24 8 24",
            "8 8 24 8 24 8 24 24 8 24", "8 8 24 8 24 24 8 24 8 24", "8 8 24 8 24 24 8 24 8 8" })
        foreach (string spelling in new[] { "integer", "decimal", "exponent" }) yield return [shape, points, spelling];
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [MemberData(nameof(PointLists))]
    public void Equivalent_point_lists_and_paths_share_degenerate_geometry_rules(string shape, string points, string spelling)
    {
        foreach (var pair in PaintInputs(shape, points, spelling))
            SvgRasterTestHelper.AssertPixelsEqual(pair.Control, pair.Input, SvgRenderOptions.Default);
        if (points.Length == 0)
            SvgRasterTestHelper.AssertPixelsEqual(Document(""), Document("<" + shape + "/>"), SvgRenderOptions.Default);
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("polygon", "", "integer", "url(#p)", false)]
    [InlineData("polygon", "8 8 24 24", "decimal", "url(#r)", true)]
    [InlineData("polyline", "8 8 24 8 24 8 24 24 8 24", "exponent", "url(#l)", false)]
    public void Complete_png_point_lists_preserve_degenerate_and_repeated_vertex_paint(string shape, string points, string spelling, string paint, bool stroke)
    {
        var pair = PaintInputs(shape, points, spelling).Single(pair => pair.Paint == paint && pair.Stroke == stroke);
        SvgRasterTestHelper.AssertPngEqual(pair.Control, pair.Input, SvgRenderOptions.Default);
    }

    private static IEnumerable<(string Paint, bool Stroke, string Control, string Input)> PaintInputs(string shape, string points, string spelling)
    {
        string tokens = string.Join(' ', points.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => value + (spelling == "decimal" ? ".0" : spelling == "exponent" ? "e0" : "")));
        string path = PathData(points, shape == "polygon");
        const string definitions = "<defs><linearGradient id='l'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='r'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='2' height='4' fill='red'/></pattern></defs>";
        foreach (string paint in new[] { "red", "url(#l)", "url(#r)", "url(#p)" })
        foreach (string style in new[] { "fill='" + paint + "'", "fill='none' stroke='" + paint + "' stroke-width='2' stroke-linecap='round'" })
        {
            yield return (paint, style.StartsWith("fill='none'", StringComparison.Ordinal),
                Document(definitions + "<path d='" + path + "' " + style + "/>"),
                Document(definitions + "<" + shape + " points='" + tokens + "' " + style + "/>"));
        }
    }

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("polygon", false)]
    [InlineData("polygon", true)]
    [InlineData("polyline", false)]
    [InlineData("polyline", true)]
    public void Repeated_authored_vertices_keep_markers_through_affine_and_instance_replay(string shape, bool instance)
    {
        const string points = "8 8 8 8 24 8 24 24 8 24 8 24";
        const string marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='4' markerHeight='4' orient='auto' overflow='visible'><path d='M0 0L4 2L0 4Z' fill='red'/></marker></defs>";
        const string style = "fill='none' stroke='blue' stroke-width='2' marker-start='url(#m)' marker-mid='url(#m)' marker-end='url(#m)'";
        string candidate = "<" + shape + " points='" + points + "' " + style + "/>", control = "<path d='" + PathData(points, shape == "polygon") + "' " + style + "/>";
        foreach (string content in new[] { "", "transform='translate(1 2) scale(.75)'" })
        {
            string Draw(string leaf) => marker + (instance ? "<defs><g id='s'>" + leaf + "</g></defs><use href='#s' " + content + "/>" : "<g " + content + ">" + leaf + "</g>");
            Assert.Equal(Render(Draw(control)), Render(Draw(candidate)));
        }

        static byte[] Render(string content)
            => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(Document(content)), SvgRenderOptions.Default, CancellationToken.None).ToArray();
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Repeated_vertices_still_consume_authored_limits_and_release_storage()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='" + string.Join(' ', Enumerable.Repeat("1 1", 100)) + "'/></svg>");
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        SvgRenderLimits limits = new(defaults.MaximumInputBytes, defaults.MaximumDimension,
            defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, 4, defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, limits, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static string PathData(string points, bool closed)
    {
        string[] tokens = points.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? "" : "M" + string.Join("L", Enumerable.Range(0, tokens.Length / 2).Select(index => tokens[index * 2] + " " + tokens[index * 2 + 1])) + (closed ? "Z" : "");
    }

    private static string Document(string content)
        => "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>";
}
