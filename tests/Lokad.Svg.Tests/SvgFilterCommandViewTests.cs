using System.Security.Cryptography;
using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterCommandViewTests
{
    [Theory]
    [InlineData("<feOffset dx='1'/>")]
    [InlineData("<feGaussianBlur stdDeviation='.5'/>")]
    [InlineData("<feOffset dx='1' result='a'/><feMerge><feMergeNode in='a'/><feMergeNode in='SourceGraphic'/></feMerge>")]
    public void Retained_source_replay_does_not_copy_each_command_into_raster_storage(string graph)
    {
        long rasterAllowance = 1_000_000;
        string? expected = null;
        foreach (int count in new[] { 1, 64 })
        {
            byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><filter id='f' x='-1' y='-1' width='3' height='3'>"
                + graph + "</filter></defs><g filter='url(#f)'>"
                + string.Concat(Enumerable.Repeat("<rect x='4' y='4' width='4' height='4' fill='#c13d72'/>", count)) + "</g></svg>");
            RenderBudget parser = new(new HostMemoryBudget(1_000_000));
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            {
                RenderBudget raster = new(new HostMemoryBudget(rasterAllowance));
                string actual;
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, raster, CancellationToken.None))
                    actual = Convert.ToHexStringLower(SHA256.HashData(surface.Pixels));
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                if (expected is null)
                {
                    expected = actual;
                    rasterAllowance = raster.Snapshot().PeakBytes;
                }
                else Assert.Equal(expected, actual);
                // The scene owner retains both command ranges. More identical opaque
                // draws add replay work, requiring no additional raster command storage.
                SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, SvgRenderOptions.Default, rasterAllowance, expected);
            }
            Assert.Equal(0, parser.Snapshot().CurrentBytes);
        }
    }
}
