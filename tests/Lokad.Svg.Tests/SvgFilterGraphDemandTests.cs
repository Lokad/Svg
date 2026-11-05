using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterGraphDemandTests
{
    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData("flood")]
    [InlineData("blur")]
    [InlineData("offset")]
    [InlineData("matrix")]
    [InlineData("merge")]
    [InlineData("shadow")]
    [InlineData("blend")]
    public void Unread_branches_need_no_pixel_storage(string branch)
    {
        byte[] source = Source(), unchanged = source.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse(Branch(branch) + "<feFlood flood-color='#1965bd' flood-opacity='.5'/>", parser))
        {
            using HostMemoryBudget host = new(32_767);
            RenderBudget raster = new(host);
            using (SvgFilterResult result = Apply(source, Resource(root), raster, CancellationToken.None))
                for (int offset = 0; offset < source.Length; offset += 4)
                    Assert.Equal(new byte[] { 13, 51, 95, 128 }, result.Pixels.AsSpan(offset, 4).ToArray());
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Contract")]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_secondary_and_merge_branches_survive_refusal_cancellation_and_retry(bool merge)
    {
        byte[] source = Source(), unchanged = source.ToArray();
        const string selected = "<feFlood flood-color='#1965bd' flood-opacity='.5' result='color'/><feColorMatrix in='color' type='saturate' values='.5' result='live'/>";
        string tail = merge ? "<feMerge><feMergeNode in='SourceGraphic'/><feMergeNode in='live'/></feMerge>"
            : "<feBlend in='SourceGraphic' in2='live' mode='screen'/>";
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        byte[] expected;
        using (SvgRoot control = Parse(selected + tail, parser))
        using (SvgFilterResult result = Apply(source, Resource(control), new RenderBudget(), CancellationToken.None))
            expected = result.Pixels.ToArray();
        using (SvgRoot root = Parse(selected + Branch("blur") + tail, parser))
        {
            SvgFilterResource resource = Resource(root);
            using HostMemoryBudget host = new(source.Length * 4 - 1);
            RenderBudget raster = new(host);
            Verify();
            host.Limit = raster.Snapshot().PeakBytes - 1;
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult result = Apply(source, resource, raster, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            host.Limit = source.Length * 4 - 1;
            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() =>
            {
                using SvgFilterResult result = Apply(source, resource, raster, canceled.Token);
            }).CancellationToken);
            Assert.Equal(0, host.Reserved);
            Verify();
            Assert.False(host.Disposed);

            void Verify()
            {
                using (SvgFilterResult result = Apply(source, resource, raster, CancellationToken.None))
                    Assert.Equal(expected, result.Pixels);
                Assert.Equal(0, host.Reserved);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                Assert.Equal(unchanged, source);
            }
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Unread_pixels_keep_region_defaults_for_a_later_alpha_generating_matrix()
    {
        const string matrix = "<feColorMatrix in='empty' values='0 0 0 0 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 1'/>";
        byte[] source = Source(), unchanged = source.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        RenderBudget control = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse("<feMerge x='8' y='8' width='16' height='16' result='empty'/>" + matrix, parser))
        using (SvgFilterResult result = Render(Resource(root), control)) { }
        using (SvgRoot root = Parse("<feOffset x='8' y='8' width='16' height='16' result='unused'/>"
            + "<feMerge result='empty'/>" + matrix, parser))
        {
            using HostMemoryBudget host = new(control.Snapshot().PeakBytes + source.Length - 1);
            RenderBudget raster = new(host);
            using (SvgFilterResult result = Render(Resource(root), raster))
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    bool inside = x >= 8 && x < 24 && y >= 8 && y < 24;
                    Assert.Equal(new byte[] { inside ? (byte)255 : (byte)0, 0, 0, inside ? (byte)255 : (byte)0 },
                        result.Pixels.AsSpan((y * 64 + x) * 4, 4).ToArray());
                }
            Assert.Equal(0, host.Reserved);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            Assert.Equal(unchanged, source);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(0, control.Snapshot().CurrentBytes);

        SvgFilterResult Render(SvgFilterResource resource, RenderBudget budget)
            => SvgFilterPlayback.ApplyFullFrame(source, 64, 64, resource, new(1, 1)
            {
                Regions = new(SvgAffineTransform.Identity, new(64, 64), new(0, 0, 64, 64))
                { SourceBounds = new(0, 0, 64, 64) }
            }, budget, CancellationToken.None);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("primary", -3)]
    [InlineData("primary", 0)]
    [InlineData("secondary", -3)]
    [InlineData("secondary", 0)]
    [InlineData("merge", -3)]
    [InlineData("merge", 0)]
    public void Unread_nodes_still_validate_each_reference(string role, int index)
    {
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        string branch = role == "merge" ? Branch("merge") : Branch("blend");
        using (SvgRoot root = Parse(branch + "<feFlood/>", parser))
        {
            SvgFilterResource resource = Resource(root);
            SvgFilterPrimitive[] primitives = resource.Primitives.Items.ToArray();
            if (role == "primary") primitives[0] = primitives[0] with { Input = new(index) };
            else if (role == "secondary") primitives[0] = primitives[0] with { SecondInput = new(index) };
            else
            {
                SvgFilterInput[] inputs = resource.MergeInputs.Items.ToArray();
                inputs[0] = new(index);
                resource = resource with { MergeInputs = new(inputs, null) };
            }
            resource = resource with { Primitives = new(primitives, null) };
            RenderBudget raster = new(new HostMemoryBudget(2_000_000));
            SvgException error = Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult result = Apply(Source(), resource, raster, CancellationToken.None);
            });
            Assert.Equal(SvgErrorCode.MalformedInput, error.ErrorCode);
            Assert.Equal("Filter graph input is outside its earlier results.", error.Message);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false)]
    [InlineData(true)]
    public void Unread_nodes_keep_mapped_numeric_limits(bool shadow)
    {
        RenderBudget parser = new(new HostMemoryBudget(2_000_000));
        using (SvgRoot root = Parse(Branch(shadow ? "shadow" : "offset") + "<feFlood/>", parser))
        {
            SvgFilterResource resource = Resource(root);
            SvgFilterPrimitive[] primitives = resource.Primitives.Items.ToArray();
            primitives[0] = shadow ? primitives[0] with { SigmaX = 136 }
                : primitives[0] with { OffsetX = (double)int.MaxValue + 1 };
            resource = resource with { Primitives = new(primitives, null) };
            RenderBudget raster = new(new HostMemoryBudget(2_000_000));
            SvgException error = Assert.Throws<SvgException>(() =>
            {
                using SvgFilterResult result = Apply(Source(), resource, raster, CancellationToken.None);
            });
            Assert.Equal(shadow ? SvgErrorCode.UnsupportedFeature : SvgErrorCode.LimitExceeded, error.ErrorCode);
            Assert.Equal(shadow ? "Drop-shadow deviations above 135 device pixels per axis are not implemented."
                : "Filter offset exceeds the numeric range.", error.Message);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static byte[] Source()
    {
        byte[] source = new byte[64 * 64 * 4];
        for (int offset = 0; offset < source.Length; offset += 4)
        { source[offset] = 7; source[offset + 1] = 31; source[offset + 2] = 63; source[offset + 3] = 128; }
        return source;
    }

    private static SvgFilterResult Apply(byte[] source, SvgFilterResource resource, RenderBudget budget, CancellationToken token)
        => SvgFilterPlayback.ApplyFullFrame(source, 64, 64, resource, new(1, 1), budget, token);

    private static SvgRoot Parse(string graph, RenderBudget budget)
        => SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs><filter id='f' color-interpolation-filters='sRGB'>"
            + graph + "</filter></defs><rect width='64' height='64' filter='url(#f)'/></svg>"), SvgRenderLimits.Default, budget, CancellationToken.None);

    private static SvgFilterResource Resource(SvgRoot root)
        => (root.FilterResources ?? throw new InvalidDataException("Missing filter resource.")).ResourceAt(0);

    private static string Branch(string branch) => branch switch
    {
        "flood" => "<feFlood flood-color='red' result='unused'/>",
        "blur" => "<feGaussianBlur in='SourceAlpha' stdDeviation='.5' result='unused'/>",
        "offset" => "<feOffset in='SourceAlpha' dx='.25' dy='.5' result='unused'/>",
        "matrix" => "<feColorMatrix in='SourceAlpha' type='saturate' values='.5' result='unused'/>",
        "merge" => "<feMerge result='unused'><feMergeNode in='SourceGraphic'/><feMergeNode in='SourceAlpha'/></feMerge>",
        "shadow" => "<feDropShadow in='SourceAlpha' stdDeviation='.5' dx='.25' dy='.5' result='unused'/>",
        "blend" => "<feBlend in='SourceAlpha' in2='SourceGraphic' mode='screen' result='unused'/>",
        _ => throw new ArgumentOutOfRangeException(nameof(branch)),
    };
}
