using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEllipseDashSourceTests
{
    [Theory]
    [InlineData(false, false, ".5")]
    [InlineData(false, false, "2")]
    [InlineData(false, true, ".5")]
    [InlineData(false, true, "2")]
    [InlineData(true, false, ".5")]
    [InlineData(true, false, "2")]
    [InlineData(true, true, ".5")]
    [InlineData(true, true, "2")]
    public void Oval_dashes_retain_cut_conics_through_group_and_use_placement(bool ellipse, bool nonScaling, string width)
    {
        string style = " fill='none' stroke='blue' stroke-width='" + width + "' stroke-dasharray='3 2' stroke-dashoffset='1'" + (nonScaling ? " vector-effect='non-scaling-stroke'" : "");
        string shape = ellipse ? "<ellipse id='mark' cx='8' cy='8' rx='6' ry='4'" + style + "/>" : "<circle id='mark' cx='8' cy='8' r='5'" + style + "/>";
        foreach (string placement in new[] { "direct", "group", "use" })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(Document(shape, placement));
            RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
            {
                SvgAuthoredPath centerline = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries, value => value.AuthoredPath?.StrokeStyle is not null).AuthoredPath);
                Assert.Contains(centerline.Commands.ToArray(), command => command.Verb == SvgAuthoredPathVerb.Conic);
                SvgAuthoredPath outline = Assert.IsType<SvgAuthoredPath>(centerline.StrokeOutline);
                Assert.Contains(outline.Commands.ToArray(), command => command.Verb is SvgAuthoredPathVerb.Quadratic or SvgAuthoredPathVerb.Conic);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
                Assert.Contains(surface.Pixels, value => value != 0);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        static string Document(string shape, string placement)
        {
            string content = placement switch
            {
                "direct" => shape,
                "group" => "<g transform='scale(2)'>" + shape + "</g>",
                "use" => "<defs>" + shape + "</defs><use href='#mark' transform='translate(8 0)'/>",
                _ => throw new ArgumentOutOfRangeException(nameof(placement)),
            };
            return "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + content + "</svg>";
        }
    }

    [Theory]
    [InlineData("<circle cx='8' cy='8' r='0'")]
    [InlineData("<ellipse cx='8' cy='8' rx='0' ry='4'")]
    [InlineData("<ellipse cx='8' cy='8' rx='6' ry='0'")]
    public void Zero_radius_dashed_ovals_paint_nothing_and_release_owners(string shape)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + shape + " fill='none' stroke='blue' stroke-width='2' stroke-dasharray='3 2'/></svg>");
        RenderBudget budget = new(SvgRenderLimits.Default.MaximumAccountedBytes);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
            Assert.All(surface.Pixels, value => Assert.Equal((byte)0, value));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
