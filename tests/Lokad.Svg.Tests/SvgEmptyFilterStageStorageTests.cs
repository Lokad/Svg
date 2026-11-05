using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgEmptyFilterStageStorageTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public void Cropped_stages_need_no_pixel_storage_before_a_later_source_draw(int count)
    {
        byte[] input = new byte[64 * 64 * 4];
        for (int index = 0; index < input.Length; index += 4)
        { input[index] = 7; input[index + 1] = 31; input[index + 2] = 211; input[index + 3] = 255; }
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse(string.Concat(Enumerable.Repeat("<feFlood x='80' y='80' width='4' height='4'/>", count))
            + "<feOffset in='SourceGraphic' x='0' y='0' width='64' height='64'/>", parser))
        {
            SvgFilterResource resource = Resource(root);
            const long oneSurface = 64 * 64 * 4;
            using HostMemoryBudget host = new(2 * oneSurface - 1);
            RenderBudget raster = new(host);
            Verify();
            Assert.InRange(raster.Snapshot().PeakBytes, oneSurface, 2 * oneSurface - 1);
            host.Limit = oneSurface - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.Apply(input, 64, 64, resource, Map(), new(0, 0, 64, 64), raster, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = 2 * oneSurface - 1;
            Verify();
            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.Apply(input, 64, 64, resource, Map(), new(0, 0, 64, 64), raster, canceled.Token);
            }).CancellationToken);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            Assert.False(host.Disposed);

            void Verify()
            {
                using (SvgFilterResult result = SvgFilterPlayback.Apply(input, 64, 64, resource, Map(), new(0, 0, 64, 64), raster, CancellationToken.None))
                    Assert.Equal(unchanged, result.Pixels);
                Assert.Equal(0, host.Reserved);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
            }
        }
        Assert.Equal(unchanged, input);
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_stage_results_remain_readable_and_transfer_only_the_returned_frame(bool bias)
    {
        string graph = "<feFlood x='80' y='80' width='4' height='4'/>" + (bias
            ? "<feColorMatrix x='0' y='0' width='64' height='64' values='0 0 0 0 .25  0 0 0 0 .5  0 0 0 0 1  0 0 0 0 1'/>" : "");
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse(graph, parser))
        {
            using HostMemoryBudget host = new(1_000_000);
            RenderBudget raster = new(host);
            byte[] source = new byte[64 * 64 * 4];
            byte[] expected;
            using (SvgFilterResult result = SvgFilterPlayback.Apply(source, 64, 64, Resource(root), Map(), new(0, 0, 64, 64), raster, CancellationToken.None))
            {
                Assert.Equal(64, result.Frame.Width); Assert.Equal(64, result.Frame.Height);
                Assert.NotEmpty(result.Pixels);
                for (int offset = 0; offset < result.Pixels.Length; offset += 4)
                {
                    Assert.Equal(bias ? (byte)64 : (byte)0, result.Pixels[offset]);
                    Assert.Equal(bias ? (byte)128 : (byte)0, result.Pixels[offset + 1]);
                    Assert.Equal(bias ? (byte)255 : (byte)0, result.Pixels[offset + 2]);
                    Assert.Equal(bias ? (byte)255 : (byte)0, result.Pixels[offset + 3]);
                }
                Assert.True(raster.Snapshot().CurrentBytes >= source.Length);
                expected = result.Pixels.ToArray();
            }
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = raster.Snapshot().PeakBytes - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.Apply(source, 64, 64, Resource(root), Map(), new(0, 0, 64, 64), raster, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = 1_000_000;
            using (SvgFilterResult retry = SvgFilterPlayback.Apply(source, 64, 64, Resource(root), Map(), new(0, 0, 64, 64), raster, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, host.Reserved);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string graph, RenderBudget budget)
        => SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' primitiveUnits='userSpaceOnUse' x='0' y='0' width='64' height='64' color-interpolation-filters='sRGB'>"
            + graph + "</filter></defs><rect width='64' height='64' filter='url(#f)'/></svg>"), SvgRenderLimits.Default, budget, CancellationToken.None);

    private static SvgFilterResource Resource(SvgRoot root)
        => (root.FilterResources ?? throw new InvalidDataException("Missing filter resource.")).ResourceAt(0);

    private static SvgFilterDeviceMap Map()
        => new(1, 1) { Regions = new(SvgAffineTransform.Identity, new(64, 64), new(0, 0, 64, 64)) { SourceBounds = new(0, 0, 64, 64) } };
}
