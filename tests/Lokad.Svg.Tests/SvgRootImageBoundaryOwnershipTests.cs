using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgRootImageBoundaryOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Root_boundary_borrows_coverage_and_preserves_refusal_cleanup_and_retry(bool group)
    {
        const string paints = "<rect x='-2' y='-2' width='30' height='30' fill='#2080c0'/><rect x='8' y='4' width='20' height='20' fill='#c04020'/>";
        byte[] input = TextEncoding.UTF8.GetBytes("<!-- SPDX-License-Identifier: CC0-1.0 -->\n<svg xmlns='http://www.w3.org/2000/svg' width='17' height='11'>"
            + (group ? "<g opacity='.73'>" + paints + "</g>" : paints) + "</svg>\n");
        RenderBudget sceneBudget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        {
            long retained = sceneBudget.Snapshot().CurrentBytes;
            SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default);
            RenderBudget rasterBudget = new(new HostMemoryBudget(1_000_000));
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, options, rasterBudget, CancellationToken.None))
            {
                Assert.Equal((26, 17), (surface.Width, surface.Height));
                Assert.Equal(group ? new byte[] { 192, 63, 30, 93 } : [138, 85, 85, 192],
                    surface.Pixels.AsSpan((10 * 26 + 25) * 4, 4).ToArray());
                expected = surface.Pixels.ToArray();
            }
            // The boundary owns no tape. Only the output and, for an opacity group,
            // its existing isolated surface are admitted by the raster budget.
            long peak = 26 * 17 * 4 * (group ? 2L : 1L);
            Assert.Equal(peak, rasterBudget.Snapshot().PeakBytes);
            Assert.Equal(0, rasterBudget.Snapshot().CurrentBytes);
            HostMemoryBudget limitedHost = new(peak - 1);
            RenderBudget limited = new(limitedHost);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.Rasterize(root, options, limited, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, limited.Snapshot().CurrentBytes);
            Assert.Equal(retained, sceneBudget.Snapshot().CurrentBytes);
            limitedHost.Limit = peak;
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, limited, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, limited.Snapshot().CurrentBytes);

            if (group)
            {
                using CancellationTokenSource cancellation = new();
                using (BackgroundInitializationProbe.Arm(cancellation.Cancel, static () => { }))
                {
                    SvgRenderOptions background = new(null, 1.5, new(1, 2, 3, 255), SvgRenderLimits.Default);
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                        SvgConverter.Rasterize(root, background, limited, cancellation.Token)).CancellationToken);
                }
                Assert.Equal(0, limited.Snapshot().CurrentBytes);
                using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, limited, CancellationToken.None);
                Assert.Equal(expected, retry.Pixels);
            }
            Assert.Equal(retained, sceneBudget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }
}
