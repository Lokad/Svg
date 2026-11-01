using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Mechanism")]
public sealed class SvgNestedPatternTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Nested_tile_paints_keep_source_order_and_share_retained_content(bool forward, bool polygon)
    {
        const string child = "<pattern id='child' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4' fill='red'/></pattern>";
        const string alias = "<pattern id='alias' href='#child'/>";
        string leaf = polygon ? "<path d='M0 0L8 0L0 8Z' fill='url(#alias)'/>"
            : "<rect width='8' height='8' fill='url(#alias)'/>";
        string parent = "<pattern id='parent' width='8' height='8' patternUnits='userSpaceOnUse'>" + leaf + "</pattern>";
        byte[] input = Input("<defs>" + (forward ? parent + alias + child : child + alias + parent)
            + "</defs><rect width='24' height='16' fill='url(#parent)'/>");
        RenderBudget budget = new(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        using (SvgConverter.RasterizedSurface image = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
        {
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels.AsSpan((1 * 24 + 1) * 4, 4).ToArray());
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, image.Pixels.AsSpan((1 * 24 + 3) * 4, 4).ToArray());
            Assert.Equal(image.Pixels.AsSpan((1 * 24 + 1) * 4, 4).ToArray(), image.Pixels.AsSpan((1 * 24 + 9) * 4, 4).ToArray());
            SvgVectorContent content = Assert.Single(root.Geometries).Pattern.VectorContent
                ?? throw new InvalidDataException("The parent tile must retain its vector scene.");
            SvgGeometry nested = Assert.Single(content.Scene.Geometries);
            Assert.Equal(polygon ? SvgGeometryKind.PatternPolygon : SvgGeometryKind.PatternFill, nested.Kind);
            Assert.NotNull(polygon ? nested.PatternPolygon.VectorContent : nested.Pattern.VectorContent);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Sibling_tiles_borrow_one_child_owner_through_an_alias()
    {
        byte[] input = Input("<defs>"
            + "<pattern id='child' width='4' height='4' patternUnits='userSpaceOnUse'><rect width='2' height='4' fill='red'/></pattern>"
            + "<pattern id='alias' href='#child'/>"
            + "<pattern id='parent' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='4' height='8' fill='url(#child)'/><rect x='4' width='4' height='8' fill='url(#alias)'/></pattern>"
            + "</defs><rect width='24' height='16' fill='url(#parent)'/>");
        RenderBudget budget = new(new HostMemoryBudget(8_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
        {
            SvgVectorContent parent = Assert.Single(root.Geometries).Pattern.VectorContent
                ?? throw new InvalidDataException("Missing parent tile.");
            Assert.Equal(2, parent.Scene.Geometries.Length);
            SvgVectorContent first = parent.Scene.Geometries[0].Pattern.VectorContent
                ?? throw new InvalidDataException("Missing child tile.");
            Assert.Same(first, parent.Scene.Geometries[1].Pattern.VectorContent);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("p", false)]
    [InlineData("q", false)]
    [InlineData("alias", true)]
    public void Cyclic_tile_content_rejects_before_rasterization_and_releases_sealed_scenes(string target, bool alias)
    {
        byte[] input = Input("<defs>"
            + "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='url(#" + target + ")'/></pattern>"
            + "<pattern id='q' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='url(#p)'/></pattern>"
            + (alias ? "<pattern id='alias' href='#p'/>" : "")
            + "</defs><rect width='24' height='16' fill='url(#p)'/>");
        RenderBudget budget = new(new HostMemoryBudget(8_000_000));
        SvgException failure = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Contains("Cyclic pattern content", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Tile_masks_participate_in_cycle_detection()
    {
        byte[] input = Input("<defs><mask id='m'><rect width='8' height='8' fill='url(#p)'/></mask>"
            + "<pattern id='p' width='8' height='8' patternUnits='userSpaceOnUse'><g mask='url(#m)'><rect width='8' height='8' fill='red'/></g></pattern>"
            + "</defs><rect width='24' height='16' fill='url(#p)'/>");
        RenderBudget budget = new(new HostMemoryBudget(8_000_000));
        SvgException failure = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, failure.ErrorCode);
        Assert.Contains("Cyclic pattern content", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void Actual_content_depth_is_bounded_independently_of_template_aliases(int depth, bool admitted)
    {
        Assert.Equal(32, SvgPatternUseReplay.MaximumContentDepth);
        System.Text.StringBuilder definitions = new("<defs>");
        for (int index = depth - 1; index >= 0; index--)
            definitions.Append("<pattern id='p").Append(index).Append("' width='8' height='8' patternUnits='userSpaceOnUse'><rect width='8' height='8' fill='")
                .Append(index == 0 ? "red" : "url(#p" + (index - 1) + ")").Append("'/></pattern>");
        definitions.Append("</defs><rect width='24' height='16' fill='url(#p").Append(depth - 1).Append(")'/>");
        byte[] input = Input(definitions.ToString());
        RenderBudget budget = new(new HostMemoryBudget(32_000_000));
        if (admitted)
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
            using SvgConverter.RasterizedSurface image = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels.AsSpan(0, 4).ToArray());
        }
        else
            Assert.Equal(SvgErrorCode.LimitExceeded,
                Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] Input(string content)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='16'>" + content + "</svg>");
}
