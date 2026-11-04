using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterImageStorageTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(2.25, 1.5, "", 3, 4, 8, 9)]
    [InlineData(-2.25, -1.5, "", 8, 7, 13, 12)]
    [InlineData(2.25, 0, "<feMerge><feMergeNode in='shift'/></feMerge>", 3, 6, 8, 10)]
    // Native input planning retains ceil(3*sigma) even for an effective-zero paint alias.
    [InlineData(0, -1.5, "<feGaussianBlur stdDeviation='.02'/>", 5, 6, 11, 13)]
    public void Fractional_offset_retains_the_requested_input_view_through_aliases(double dx, double dy,
        string suffix, float left, float top, float right, float bottom)
    {
        string x = dx.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string y = dy.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        RenderBudget parser = new(new HostMemoryBudget(1_000_000)), scratch = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Parse("<feGaussianBlur stdDeviation='1.25 .75'/><feOffset dx='" + x
            + "' dy='" + y + "' result='shift'/>" + suffix, parser))
        {
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(16, 16), new(-10_000, -10_000, 10_000, 10_000))
            { SourceBounds = new(4, 4, 12, 12) };
            SvgFilterPlayback.RequiredSourceBounds(Resource(root), new(1, 1), context, new(6, 6, 10, 10),
                scratch, CancellationToken.None, out SvgPaintBounds? retained);
            Assert.Equal(new SvgPaintBounds(left, top, right, bottom), retained);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(2, -1, 6, 6, 10, 10)]
    [InlineData(2.25, 1.5, -8, -8, -4, -4)]
    public void Stored_offsets_and_empty_input_views_need_no_retained_image(double dx, double dy,
        float left, float top, float right, float bottom)
    {
        string x = dx.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        string y = dy.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        RenderBudget parser = new(new HostMemoryBudget(1_000_000)), scratch = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Parse("<feGaussianBlur stdDeviation='1.25 .75'/><feOffset dx='" + x + "' dy='" + y + "'/>", parser))
        {
            SvgFilterRegionContext context = Context(false) with { SourceBounds = new(4, 4, 12, 12) };
            SvgFilterPlayback.RequiredSourceBounds(Resource(root), new(1, 1), context, new(left, top, right, bottom),
                scratch, CancellationToken.None, out SvgPaintBounds? retained);
            Assert.Null(retained);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Retained_storage_is_bounded_by_the_input_image_inside_a_large_filter_region()
    {
        RenderBudget parser = new(new HostMemoryBudget(1_000_000)), scratch = new(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = Parse("<feGaussianBlur stdDeviation='1.25 .75'/><feOffset dx='2.25' dy='1.5'/>", parser))
        {
            SvgFilterRegionContext context = new(SvgAffineTransform.Identity, new(16, 16), new(-10_000, -10_000, 10_000, 10_000))
            { SourceBounds = new(4, 4, 12, 12) };
            SvgFilterPlayback.RequiredSourceBounds(Resource(root), new(1, 1), context, new(-5_000, -5_000, 5_000, 5_000),
                scratch, CancellationToken.None, out SvgPaintBounds? retained);
            Assert.Equal(new SvgPaintBounds(0, 1, 16, 15), retained);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("<feOffset dx='0' dy='0'/>", true, "Deferred")]
    [InlineData("<feGaussianBlur stdDeviation='0'/>", true, "Deferred")]
    [InlineData("<feGaussianBlur stdDeviation='.5'/>", false, "InitializedPadding")]
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
    public void Image_alias_boundary_is_distinct_from_copying_kernel_identity()
    {
        foreach (float sigma in new[] { .03f, float.BitIncrement(.03f) })
        {
            Assert.True(SvgGaussianBlur.HasIdentityKernel(new(sigma, sigma)));
            RenderBudget scratch = new(new HostMemoryBudget(1_000_000));
            using SvgFilterResource resource = new(new(new(0, false), new(0, false), new(16, false), new(16, false), SvgFilterUnits.UserSpace),
                SvgFilterUnits.UserSpace, SvgFilterColorSpace.Srgb,
                new([new(SvgFilterPrimitiveKind.GaussianBlur, SvgFilterInput.SourceGraphic, sigma, sigma, 0, 0, default)], null), null);
            SvgFilterImageBounds image = SvgFilterPlayback.SourceImageBounds(resource, new(1, 1), Context(false), new(4, 4, 12, 12), scratch, CancellationToken.None);
            Assert.Equal(sigma == .03f ? SvgFilterImageStorage.Deferred : SvgFilterImageStorage.InitializedPadding, image.Storage);
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
