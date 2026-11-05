using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterInputStorageTests
{
    [Theory]
    [InlineData("flood", false, 32767)]
    [InlineData("merge-source", false, 32767)]
    [InlineData("merge-empty", false, 32767)]
    [InlineData("merge-two", false, 49151)]
    [InlineData("flood", true, 32767)]
    [InlineData("merge-source", true, 32767)]
    [Trait("Layer", "Contract")]
    public void Unused_primary_inputs_need_no_pixel_plane(string graph, bool ignoredAlpha, long maximumBytes)
    {
        const int size = 64;
        byte[] source = new byte[size * size * 4];
        for (int offset = 0; offset < source.Length; offset += 4)
        { source[offset] = 7; source[offset + 1] = 31; source[offset + 2] = 63; source[offset + 3] = 128; }
        byte[] unchanged = source.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        SvgFilterDeviceMap map = ignoredAlpha ? new(1, 1) : new(1, 1)
        {
            Regions = new(SvgAffineTransform.Identity, new(size, size), new(0, 0, size, size))
            { SourceBounds = new(0, 0, size, size) }
        };
        string tail = Tail(graph, !ignoredAlpha);
        byte[] expected;
        RenderBudget control = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse(tail, parser))
        using (SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame(source, size, size, Resource(root), map, control, CancellationToken.None))
            expected = result.Pixels.ToArray();
        Assert.Equal(0, control.Snapshot().CurrentBytes);
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        string prefix = ignoredAlpha ? "" : "<feFlood x='80' y='80' width='4' height='4'/><feFlood x='80' y='80' width='4' height='4'/>";
        using (SvgRoot root = Parse(prefix + tail, parser))
        {
            SvgFilterResource resource = Resource(root);
            if (ignoredAlpha)
                resource = WithPrimary(resource, SvgFilterInput.SourceAlpha);
            long retained = parser.Snapshot().CurrentBytes;
            using HostMemoryBudget host = new(maximumBytes);
            RenderBudget raster = new(host);
            Verify();
            host.Limit = raster.Snapshot().PeakBytes - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.ApplyFullFrame(source, size, size, resource, map, raster, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = maximumBytes;
            Verify();
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() =>
            {
                using SvgFilterResult denied = SvgFilterPlayback.ApplyFullFrame(source, size, size, resource, map, raster, canceled.Token);
            }).CancellationToken);
            Assert.Equal(0, host.Reserved);
            Assert.False(host.Disposed);
            Assert.Equal(retained, parser.Snapshot().CurrentBytes);

            void Verify()
            {
                using (SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame(source, size, size, resource, map, raster, CancellationToken.None))
                    Assert.Equal(expected, result.Pixels);
                Assert.Equal(0, host.Reserved);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                Assert.Equal(unchanged, source);
            }
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("flood", -3)]
    [InlineData("flood", 0)]
    [InlineData("merge-source", -3)]
    [InlineData("merge-source", 0)]
    [Trait("Layer", "Mechanism")]
    public void Unread_primary_references_still_validate_before_pixel_allocation(string graph, int index)
    {
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse(Tail(graph, false), parser))
        {
            SvgFilterResource resource = WithPrimary(Resource(root), new(index));
            byte[] source = new byte[64 * 64 * 4];
            RenderBudget raster = new(new HostMemoryBudget(2_000_000));
            SvgException error = Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult result = SvgFilterPlayback.ApplyFullFrame(source, 64, 64, resource, new(1, 1), raster, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
            Assert.Equal("Filter graph input is outside its earlier results.", error.Message);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            Assert.InRange(raster.Snapshot().PeakBytes, 1, source.Length - 1);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    // Caller-owned test metadata overrides only the unused primary reference.
    // All original resource arrays and their owners remain borrowed from root.
    private static SvgFilterResource WithPrimary(SvgFilterResource resource, SvgFilterInput input)
        => resource with { Primitives = new([resource.Primitives.Items[0] with { Input = input }], null) };

    private static SvgRoot Parse(string graph, RenderBudget budget)
        => SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' primitiveUnits='userSpaceOnUse' x='0' y='0' width='64' height='64' color-interpolation-filters='sRGB'>"
            + graph + "</filter></defs><rect width='64' height='64' filter='url(#f)'/></svg>"), SvgRenderLimits.Default, budget, CancellationToken.None);

    private static SvgFilterResource Resource(SvgRoot root)
        => (root.FilterResources ?? throw new InvalidDataException("Missing filter resource.")).ResourceAt(0);

    private static string Tail(string graph, bool placed)
    {
        string region = placed ? " x='0' y='0' width='64' height='64'" : "";
        return graph switch
        {
            "flood" => "<feFlood" + region + " flood-color='#1965bd' flood-opacity='.5'/>",
            "merge-source" => "<feMerge" + region + "><feMergeNode in='SourceGraphic'/></feMerge>",
            "merge-empty" => "<feMerge" + region + "/>",
            "merge-two" => "<feMerge" + region + "><feMergeNode in='SourceAlpha'/><feMergeNode in='SourceGraphic'/></feMerge>",
            _ => throw new ArgumentOutOfRangeException(nameof(graph))
        };
    }
}
