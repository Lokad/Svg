using System.Security.Cryptography;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterCaptureStorageTests
{
    [Fact]
    public void Clipped_offset_capture_keeps_scene_borrowers_after_refusal_cancellation_and_retry()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs><clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter></defs><g clip-path='url(#c)'><g filter='url(#f)'><g transform='rotate(12 16 16)'><rect x='4' y='4' width='20' height='20' fill='red'/><rect x='3' y='3' width='22' height='22' fill='blue' fill-opacity='.25'/></g></g></g></svg>"u8.ToArray();
        SvgRasterOwnershipAssertions.SeparateBudgetsPreserveBorrowedSceneAfterFailures(input, SvgRenderOptions.Default, 1_000_000);
    }

    [Theory]
    [InlineData("blur", false, 14_000, "b87b908ae8d80829fb2075856aed017e08a8f93377b06faa3e2348a87f85d437")]
    [InlineData("blur", true, 16_300, "54dee6faf8f1807feb7449642ca84ce5740b14d57ad063f9188b2301838dd22b")]
    [InlineData("matrix", false, 32_530, "b9e85dc01c9ff87b19e6319a8671f0b0ba23dd0efdedbc11c0ff4cfc43d8994f")]
    [InlineData("offset", false, 19_780, "61c82d8f29def03761a573a6c565d4f0eed916a613a7af2932fdcd55193b3d34")]
    public void Filter_source_fits_one_capture_when_aligned_and_retries_after_refusal(
        string graph, bool wide, long maximumRasterBytes, string expectedRgbaSha256)
    {
        string primitive = graph switch
        {
            "blur" => "<feGaussianBlur stdDeviation='.5'/>",
            "matrix" => "<feColorMatrix type='saturate' values='.5'/>",
            "offset" => "<feOffset dx='.3' dy='.7'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(graph))
        };
        string box = wide ? "x='-8' y='-8' width='48' height='48'" : "x='0' y='0' width='32' height='32'";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>"
            + "<linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32' color-interpolation-filters='sRGB'>"
            + primitive + "</filter></defs><g filter='url(#f)'><rect " + box + " fill='url(#g)'/></g></svg>");
        byte[] unchanged = input.ToArray();
        RenderBudget sceneBudget = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, CancellationToken.None))
        {
            long retainedScene = sceneBudget.Snapshot().CurrentBytes;
            using HostMemoryBudget host = new(maximumRasterBytes);
            RenderBudget raster = new(host);
            CheckPixels();
            long peak = raster.Snapshot().PeakBytes;
            Assert.InRange(peak, 1, maximumRasterBytes);
            Assert.Equal(0, host.Reserved);
            host.Limit = peak - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() =>
                SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, canceled.Token)).CancellationToken);
            host.Limit = maximumRasterBytes;
            CheckPixels();
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            Assert.False(host.Disposed);
            Assert.Equal(retainedScene, sceneBudget.Snapshot().CurrentBytes);

            void CheckPixels()
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None);
                Assert.Equal(expectedRgbaSha256, Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
            }
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
