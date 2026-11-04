using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectangleSourceTests
{
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
