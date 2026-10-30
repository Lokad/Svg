using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterImageStorageTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<feOffset dx='0' dy='0'/>", true, "Deferred")]
    [InlineData("<feGaussianBlur stdDeviation='0'/>", true, "Deferred")]
    [InlineData("<feGaussianBlur stdDeviation='.5'/>", false, "Padded")]
    [InlineData("<feGaussianBlur stdDeviation='.5'/>", true, "Cropped")]
    [InlineData("<feGaussianBlur stdDeviation='.5' result='b'/><feOffset in='b' dx='0' dy='0'/>", true, "Cropped")]
    [InlineData("<feGaussianBlur stdDeviation='.5' result='b'/><feMerge><feMergeNode in='b'/></feMerge>", true, "Cropped")]
    [InlineData("<feGaussianBlur stdDeviation='.5' result='b'/><feFlood flood-color='red'/>", true, "Deferred")]
    public void Bounds_retain_materialization_and_padding_history(string graph, bool cropped, string expected)
    {
        RenderBudget parser = new(new HostMemoryBudget(1_000_000));
        RenderBudget scratch = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Parse(graph, parser))
        {
            SvgFilterRegionContext context = Context(cropped);
            SvgFilterImageBounds image = SvgFilterPlayback.SourceImageBounds(Resource(root), new(1, 1), context, new(4, 4, 12, 12), scratch, CancellationToken.None);
            Assert.Equal(Enum.Parse<SvgFilterImageStorage>(expected), image.Storage);
            Assert.Equal(SvgFilterPlayback.SourceLayerBounds(Resource(root), new(1, 1), context, new(4, 4, 12, 12), SvgFilterBoundsKind.StoredImage, scratch, CancellationToken.None), image.Bounds);
            if (cropped) Assert.Equal(new SvgPaintBounds(5, 5, 11, 11), image.Bounds);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Materialization_uses_the_same_float_deviation_boundary_as_the_kernel()
    {
        foreach (float sigma in new[] { .03f, float.BitIncrement(.03f) })
        {
            RenderBudget scratch = new(new HostMemoryBudget(1_000_000));
            using SvgFilterResource resource = new(new(new(0, false), new(0, false), new(16, false), new(16, false), SvgFilterUnits.UserSpace),
                SvgFilterUnits.UserSpace, SvgFilterColorSpace.Srgb,
                new([new(SvgFilterPrimitiveKind.GaussianBlur, SvgFilterInput.SourceGraphic, sigma, sigma, 0, 0, default)], null), null);
            SvgFilterImageBounds image = SvgFilterPlayback.SourceImageBounds(resource, new(1, 1), Context(false), new(4, 4, 12, 12), scratch, CancellationToken.None);
            Assert.Equal(sigma == .03f ? SvgFilterImageStorage.Deferred : SvgFilterImageStorage.Padded, image.Storage);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public void Metadata_query_releases_admission_and_observes_cancellation()
    {
        RenderBudget parser = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Parse("<feGaussianBlur stdDeviation='.5'/>", parser))
        {
            RenderBudget refused = new(new HostMemoryBudget(1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgFilterPlayback.SourceImageBounds(Resource(root), new(1, 1), Context(true), new(4, 4, 12, 12), refused, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, refused.Snapshot().CurrentBytes);
            RenderBudget scratch = new(new HostMemoryBudget(1_000_000));
            using CancellationTokenSource cancellation = new(); cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                SvgFilterPlayback.SourceImageBounds(Resource(root), new(1, 1), Context(true), new(4, 4, 12, 12), scratch, cancellation.Token)).CancellationToken);
            Assert.Equal(SvgFilterImageStorage.Cropped, SvgFilterPlayback.SourceImageBounds(Resource(root), new(1, 1), Context(true), new(4, 4, 12, 12), scratch, CancellationToken.None).Storage);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<feOffset dx='0' dy='0'/>", "LinearRgbRoundTrip")]
    [InlineData("<feGaussianBlur stdDeviation='0'/>", "LinearRgbRoundTrip")]
    [InlineData("<feMerge><feMergeNode in='SourceGraphic'/></feMerge>", "LinearRgbRoundTrip")]
    [InlineData("<feGaussianBlur in='SourceAlpha' stdDeviation='0'/>", "SourceAlpha")]
    public void Source_aliases_retain_their_sampling_color_filter(string graph, string expected)
    {
        RenderBudget parser = new(new HostMemoryBudget(1_000_000));
        RenderBudget scratch = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Parse(graph, parser))
        {
            byte[] source = new byte[16 * 16 * 4];
            for (int offset = 0; offset < source.Length; offset += 4)
            { source[offset] = 20; source[offset + 1] = 40; source[offset + 2] = 80; source[offset + 3] = 128; }
            byte[] unchanged = source.ToArray();
            using (SvgFilterResult image = SvgFilterPlayback.ApplyFullFrame(source, 16, 16, Resource(root), new(1, 1), scratch, CancellationToken.None))
                Assert.Equal(Enum.Parse<SvgFilterDeferredColorFilter>(expected), image.SamplingColorFilter);
            Assert.Equal(unchanged, source);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(string graph, RenderBudget budget)
        => SvgRootParser.Parse(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs>"
            + "<filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='16' height='16'>" + graph
            + "</filter></defs><rect x='4' y='4' width='8' height='8' filter='url(#f)'/></svg>"), SvgRenderLimits.Default, budget, null, CancellationToken.None);

    private static SvgFilterResource Resource(SvgRoot root)
        => (root.FilterResources ?? throw new InvalidDataException("Missing filter resources.")).ResourceAt(0);

    private static SvgFilterRegionContext Context(bool cropped)
        => new(SvgAffineTransform.Identity, new(16, 16), cropped ? new(5, 5, 11, 11) : new(0, 0, 16, 16));
}
