using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgRootBoxOwnershipTests
{
    [Theory]
    [InlineData("padding-right:3px", true, 1, 4_096)]
    [InlineData("border:2px solid rgba(220,20,0,.5)", false, 1, 4_096)]
    [InlineData("border-top:2px solid rgba(220,20,0,.25);border-left:3px solid rgba(0,40,220,.7)", false, 2, 65_536)]
    public void Root_box_paths_release_allowance_after_refusal_and_retry(string declarations, bool oversizedContent, int surfaces, int scratchAllowance)
    {
        string body = oversizedContent ? "<rect x='-10' y='-10' width='150' height='120' fill='lime'/>" : "<rect width='4' height='4' fill='lime'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='101' height='81' style='" + declarations + "'>" + body + "</svg>");
        RenderBudget sceneBudget = new(new HostMemoryBudget(1_000_000));
        using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None);
        RenderBudget rasterBudget = new(new HostMemoryBudget(1_000_000));
        SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] expected;
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, rasterBudget, CancellationToken.None))
        {
            Assert.Equal((152, 122), (surface.Width, surface.Height));
            if (oversizedContent)
            {
                Assert.Equal(new byte[] { 0, 255, 0, 255 }, surface.Pixels.AsSpan(0, 4).ToArray());
                Assert.Equal(64, surface.Pixels[(10 * 152 + 151) * 4 + 3]);
                Assert.Equal(16, surface.Pixels[(121 * 152 + 151) * 4 + 3]);
            }
            expected = surface.Pixels.ToArray();
        }
        long peak = rasterBudget.Snapshot().PeakBytes;
        // Distinguish borrowed rectangle/frame coverage from an owned opacity
        // source; the scratch allowance cannot hide another full-size surface.
        Assert.InRange(peak, 152 * 122 * 4 * surfaces, 152 * 122 * 4 * surfaces + scratchAllowance);
        Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
        SvgRasterOwnershipAssertions.RefusalReleasesAndRetriesBorrowedScene(root, sceneBudget, options, peak, expected);
        root.Dispose();
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }
}
