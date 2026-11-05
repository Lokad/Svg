using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterQueryStorageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Source_bounds_use_bounded_scratch(int count)
    {
        RenderBudget sceneBudget = new();
        using (SvgRoot scene = Parse(count, sceneBudget))
        {
            SvgFilterResource resource = Resource(scene);
            _ = Query();
            long before = GC.GetAllocatedBytesForCurrentThread();
            SvgPaintBounds result = Query();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(count == 0 ? default : new SvgPaintBounds(.25f, .5f, 8.25f, 8.5f), result);
            Assert.Equal(0, allocated);

            SvgPaintBounds Query() => SvgFilterPlayback.SourceLayerBounds(resource, new(1, 1), Context(),
                new(0, 0, 8, 8), SvgFilterBoundsKind.Output, CancellationToken.None);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Stored_image_bounds_use_bounded_scratch(int count)
    {
        RenderBudget sceneBudget = new();
        using (SvgRoot scene = Parse(count, sceneBudget))
        {
            SvgFilterResource resource = Resource(scene);
            _ = Query();
            long before = GC.GetAllocatedBytesForCurrentThread();
            SvgFilterImageBounds result = Query();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(count == 0 ? default : new SvgPaintBounds(0, 0, 9, 9), result.Bounds);
            Assert.Equal(SvgFilterImageStorage.Deferred, result.Storage);
            Assert.Equal(0, allocated);

            SvgFilterImageBounds Query() => SvgFilterPlayback.SourceImageBounds(resource, new(1, 1), Context(),
                new(0, 0, 8, 8), CancellationToken.None);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    public void Source_demand_and_retained_offset_input_use_bounded_scratch(int count)
    {
        RenderBudget sceneBudget = new();
        using (SvgRoot scene = Parse(count, sceneBudget))
        {
            SvgFilterResource resource = Resource(scene);
            _ = Query(out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            SvgPaintBounds result = Query(out SvgPaintBounds? retained);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(count == 0 ? default : new SvgPaintBounds(1, 2, 4, 5), result);
            Assert.Equal(count == 0 ? null : new SvgPaintBounds(1, 2, 4, 5), retained);
            Assert.Equal(0, allocated);

            SvgPaintBounds Query(out SvgPaintBounds? input) => SvgFilterPlayback.RequiredSourceBounds(resource, new(1, 1),
                Context(), new(2, 3, 4, 5), CancellationToken.None, out input);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    private static SvgRoot Parse(int count, RenderBudget budget)
    {
        string graph = count == 0 ? "" : string.Concat(Enumerable.Repeat("<feOffset/>", count - 1)) + "<feOffset dx='.25' dy='.5'/>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'>"
            + "<defs><filter id='f'>" + graph + "</filter></defs><rect width='8' height='8' filter='url(#f)'/></svg>");
        return SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None);
    }

    private static SvgFilterResource Resource(SvgRoot scene)
        => (scene.FilterResources ?? throw new InvalidOperationException("Missing filter definitions.")).ResourceAt(0);

    private static SvgFilterRegionContext Context()
        => new(SvgAffineTransform.Identity, new(32, 32), new(-16, -16, 32, 32)) { SourceBounds = new(0, 0, 8, 8) };
}
