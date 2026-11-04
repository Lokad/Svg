using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectangleSourceTests
{
    [Theory]
    [InlineData("rect", "url(#linear)", false, "visible")]
    [InlineData("rect", "url(#radial)", false, "hidden")]
    [InlineData("rect", "url(#pattern)", false, "hidden")]
    [InlineData("path", "url(#linear)", false, "hidden")]
    [InlineData("path", "url(#radial)", true, "hidden")]
    [InlineData("path", "url(#pattern)", true, "hidden")]
    [InlineData("polyline", "url(#linear)", false, "hidden")]
    [InlineData("polyline", "blue", false, "visible")]
    [Trait("Layer", "Mechanism")]
    public void Affine_instances_retain_source_commands_independently_of_paint_visibility(string shape, string paint, bool stroke, string visibility)
    {
        string start = shape switch
        {
            "path" => "<path d='M2 2H26V26H2Z'",
            "polyline" => "<polyline points='2,2 26,2 14,26'",
            _ => "<rect x='2' y='2' width='24' height='24'"
        };
        string input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + "<linearGradient id='linear'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>"
            + "<radialGradient id='radial'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>"
            + "<pattern id='pattern' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='4' height='4' fill='blue'/></pattern>"
            + start + " id='s' visibility='" + visibility + "' "
            + (stroke ? "fill='none' stroke='" + paint + "' stroke-width='2'" : "fill='" + paint + "'")
            + "/></defs><use href='#s' transform='matrix(1 .25 .125 1 0 0)'/></svg>";
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            Assert.Equal(stroke ? 2 : 1, root.Geometries.Length);
            int strokes = 0;
            foreach (SvgGeometry geometry in root.Geometries)
            {
                SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
                Assert.True(source.Commands.Length >= 4);
                if (source.StrokeStyle is not null) strokes++;
                Assert.Equal(new SvgAffineTransform(1, .25, .125, 1, 0, 0), source.SceneMap);
            }
            Assert.Equal(stroke ? 1 : 0, strokes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("blue", "leaf")]
    [InlineData("url(#p)", "leaf")]
    [InlineData("blue", "group")]
    [InlineData("url(#p)", "group")]
    [InlineData("blue", "instance")]
    [InlineData("url(#p)", "instance")]
    [Trait("Layer", "Mechanism")]
    public void Retained_rectangles_keep_native_stroke_commands_independently_of_material_and_inheritance(string stroke, string placement)
    {
        string style = " stroke='" + stroke + "' stroke-width='.5'";
        bool group = placement == "group", instance = placement == "instance";
        string input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + "<linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>"
            + "<g" + (group ? " id='s'" + style : "") + "><rect" + (group ? "" : " id='s'")
            + " x='4' y='4' width='20' height='20' fill='url(#p)'"
            + (group || instance ? "" : style) + "/></g></defs><use href='#s'"
            + (instance ? style + " transform='scale(.75 1.1)'" : "") + "/></svg>";
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes(input), SvgRenderLimits.Default, budget, null, CancellationToken.None))
        {
            Assert.Equal(2, root.Geometries.Length);
            int strokes = 0;
            foreach (SvgGeometry geometry in root.Geometries)
            {
                SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(geometry.AuthoredPath);
                Assert.Equal(5, source.Commands.Length);
                if (source.StrokeStyle is not null) strokes++;
            }
            Assert.Equal(1, strokes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
