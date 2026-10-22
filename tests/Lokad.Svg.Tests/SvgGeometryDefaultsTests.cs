using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgGeometryDefaultsTests
{
    public static IEnumerable<object[]> InertGeometry()
    {
        foreach (string geometry in new[] { "<circle/>", "<ellipse/>", "<path/>", "<image width='8' height='8'/>", "<image href='' width='8' height='8'/>" })
        foreach (string scope in new[] { "direct", "use", "symbol", "pattern" }) yield return [geometry, scope];
    }

    [Theory]
    [MemberData(nameof(InertGeometry))]
    public void Omitted_geometry_and_image_sources_preserve_other_drawing(string geometry, string scope)
    {
        Assert.Equal(Render(Wrap("")), Render(Wrap(geometry)));

        string Wrap(string content) => scope switch
        {
            "direct" => "<rect width='16' height='16' fill='red'/>" + content,
            "use" => "<rect width='16' height='16' fill='red'/><defs><g id='s'>" + content + "</g></defs><use href='#s'/>",
            "symbol" => "<rect width='16' height='16' fill='red'/><defs><symbol id='s'>" + content + "</symbol></defs><use href='#s'/>",
            _ => "<defs><pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'>" + content + "</pattern></defs><rect width='16' height='16' fill='url(#p)'/>"
        };
    }

    [Theory]
    [InlineData("rx='4'", "rx='4' ry='4'")]
    [InlineData("ry='4'", "rx='4' ry='4'")]
    [InlineData("rx='25%'", "rx='8' ry='8'")]
    [InlineData("ry='25%'", "rx='4' ry='4'")]
    public void Missing_ellipse_axis_copies_the_other_resolved_radius(string actual, string expected)
        => Assert.Equal(Render("<ellipse cx='12' cy='8' " + expected + "/>"), Render("<ellipse cx='12' cy='8' " + actual + "/>"));

    [Theory]
    [InlineData("<circle r='-1'/>")]
    [InlineData("<ellipse ry='-1'/>")]
    [InlineData("<image width='-1'/>")]
    public void Invalid_geometry_still_rejects_without_a_drawable_source(string geometry)
        => Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Render(geometry)).ErrorCode);

    [Fact]
    public void Missing_path_geometry_remains_empty_under_clips_and_all_paint_types()
    {
        const string definitions = "<defs><linearGradient id='l'><stop stop-color='red'/></linearGradient><radialGradient id='r'><stop stop-color='red'/></radialGradient><pattern id='p' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4'/></pattern><clipPath id='c'><rect width='8' height='8'/></clipPath></defs>";
        foreach (string paint in new[] { "red", "url(#l)", "url(#r)", "url(#p)" })
        {
            string attributes = "fill='" + paint + "' stroke='" + paint + "' clip-path='url(#c)'";
            Assert.Equal(Render(definitions), Render(definitions + "<path " + attributes + "/>"));
        }
        RenderBudget budget = new(1_000_000);
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><path/></svg>"), SvgRenderLimits.Default, budget, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Render(string content)
        => SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='16'>" + content + "</svg>"), SvgRenderOptions.Default, CancellationToken.None).ToArray();
}
