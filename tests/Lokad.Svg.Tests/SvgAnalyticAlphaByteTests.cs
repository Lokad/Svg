using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgAnalyticAlphaByteTests
{
    [Fact]
    public void Partial_triangle_narrows_to_byte_before_additive_coverage()
    {
        // Independent native platform triples are recorded for the same curve
        // in original-analytic-alpha-byte.json. A renewed cubic segment makes
        // a triangle approximation exceed its quarter-strip alpha: the native
        // byte wraps before accumulation, preserving this fully covered pixel.
        byte[] source = "<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40'><path transform='translate(.25) scale(1 .23)' d='M4 4H76V100H4L8 96C16 90 38 81 48 78C58 75 33 69 20 65C7 61 27 18 38 8Z' fill='#80b2ed'/></svg>"u8.ToArray();
        using SvgRoot root = SvgRootParser.Parse(source, SvgRenderLimits.Default, CancellationToken.None);
        var polygon = Assert.Single(root.Geometries).Polygon;
        RenderBudget budget = new(new HostMemoryBudget(1_000_000));
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(polygon.Points, polygon.ContourEnds, [], false, null,
            polygon.Mapping, polygon.QuadSpans, polygon.CubicSpans, 4, 77, 0, 23, 80, 40, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            walker.PrepareRow(17, CancellationToken.None);
            byte[] pixel = new byte[4];
            walker.PaintPixel(50 - 4, pixel, SvgAnalyticPaint.Solid(polygon.Color), SvgAnalyticClipCoverage.Full);
            Assert.Equal(new byte[] { 128, 178, 237, 255 }, pixel);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
