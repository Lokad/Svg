using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterGeometryOwnershipTests
{
    [Theory]
    [InlineData(1, 1, 13_640, "e88d305ea57e332241acebcf16d7b9a2a00ec9854366dac7032e282973eff08b")]
    [InlineData(64, 1, 18_180, "cb4b962422f61e8a6dd2a7f4b475789f20d5895f174dbfe6c5a2082a0dbd6af2")]
    [InlineData(1, 96, 13_640, "e88d305ea57e332241acebcf16d7b9a2a00ec9854366dac7032e282973eff08b")]
    public void Affine_replay_borrows_geometry_and_preserves_parent_indices_after_failures(
        int sourceCount, int precedingSiblings, long rasterAllowance, string expectedRgbaSha256)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='48' height='40'><defs>"
            + "<linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32' color-interpolation-filters='sRGB'><feGaussianBlur stdDeviation='.5'/></filter></defs>"
            + string.Concat(Enumerable.Repeat("<rect x='0' y='0' width='2' height='2' fill='green'/>", precedingSiblings))
            + "<g transform='matrix(1 .125 .2 1 1 1)' filter='url(#f)' opacity='.7'><line x1='2' y1='2' x2='18' y2='18'/>"
            + string.Concat(Enumerable.Repeat("<rect x='4' y='4' width='8' height='8' fill='url(#g)'/>", sourceCount))
            + "</g><rect x='42' y='34' width='3' height='3' fill='pink'/></svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            Assert.Equal(sourceCount + precedingSiblings + 2, scene.Geometries.Length);
            SvgGeometry[] geometries = scene.Geometries.ToArray();
            SvgDrawCommand[] commands = scene.Commands.ToArray();
            long retained = parser.Snapshot().CurrentBytes;
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, SvgRenderOptions.Default, rasterAllowance, expectedRgbaSha256);
            Assert.Equal(retained, parser.Snapshot().CurrentBytes);
            Assert.Equal(geometries, scene.Geometries);
            Assert.Equal(commands, scene.Commands.ToArray());
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
