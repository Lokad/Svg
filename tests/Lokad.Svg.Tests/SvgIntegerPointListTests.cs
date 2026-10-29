using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgIntegerPointListTests
{
    [Theory]
    [InlineData("solid", false)]
    [InlineData("linear", false)]
    [InlineData("radial", false)]
    [InlineData("pattern", false)]
    [InlineData("solid", true)]
    [InlineData("linear", true)]
    [InlineData("radial", true)]
    [InlineData("pattern", true)]
    public void Integer_crossings_retain_source_winding_and_match_path_fills(string paint, bool evenOdd)
    {
        string definitions = "<linearGradient id='linear'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><radialGradient id='radial'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient><pattern id='pattern' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='4' height='4' fill='red'/></pattern>";
        string style = " fill='" + (paint == "solid" ? "red" : "url(#" + paint + ")") + "' fill-rule='" + (evenOdd ? "evenodd" : "nonzero") + "'";
        foreach (bool closed in new[] { false, true })
        foreach (string placement in new[] { "direct", "group", "use" })
        {
            string points = "<" + (closed ? "polygon" : "polyline") + " id='mark' points='0,0 8,8 8,0 0,8'" + style + "/>";
            string path = "<path id='mark' d='M0,0 L8,8 L8,0 L0,8" + (closed ? " Z" : "") + "'" + style + "/>";
            byte[] input = System.Text.Encoding.UTF8.GetBytes(Document(points, definitions, placement));
            byte[] equivalent = System.Text.Encoding.UTF8.GetBytes(Document(path, definitions, placement));
            RenderBudget budget = new(1_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, null, CancellationToken.None))
            {
                SvgAuthoredPath source = Assert.IsType<SvgAuthoredPath>(Assert.Single(scene.Geometries).AuthoredPath);
                Assert.Equal(5, source.Commands.Length);
                Assert.Equal(closed ? SvgAuthoredPathVerb.Close : SvgAuthoredPathVerb.End, source.Commands[4].Verb);
                using SvgPreparedPathContours tape = source.PrepareSource(SvgAffineTransform.Identity, evenOdd, budget, CancellationToken.None);
                Assert.Equal(4, tape.Points.Length);
                Assert.Equal(new[] { 4 }, tape.ContourEnds);
                using SvgConverter.RasterizedSurface image = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None);
                if (placement == "direct")
                {
                    // Coverage from the original retained 8x8 bowtie browser PNG.
                    Assert.Equal((byte)127, image.Pixels[(3 * image.Width + 3) * 4 + 3]);
                    Assert.Equal((byte)144, image.Pixels[(4 * image.Width + 3) * 4 + 3]);
                    Assert.Equal((byte)144, image.Pixels[(4 * image.Width + 4) * 4 + 3]);
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            Assert.Equal(SvgConverter.ConvertToPng(equivalent, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        }

        static string Document(string shape, string paints, string placement)
        {
            const string map = "translate(16 0) scale(-1 1)";
            string content = placement switch
            {
                "direct" => "<defs>" + paints + "</defs>" + shape,
                "group" => "<defs>" + paints + "</defs><g transform='" + map + "'>" + shape + "</g>",
                "use" => "<defs>" + paints + shape + "</defs><use href='#mark' transform='" + map + "'/>",
                _ => throw new ArgumentOutOfRangeException(nameof(placement)),
            };
            return "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>" + content + "</svg>";
        }
    }
}
