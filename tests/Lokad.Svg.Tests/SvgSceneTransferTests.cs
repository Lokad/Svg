using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSceneTransferTests
{
    [Fact]
    public void Materialized_content_rebinds_bounds_and_enclosing_clips_without_changing_sources_or_sibling_sharing()
    {
        RenderBudget budget = new(2_000_000);
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgSceneBuilder destination = new(budget, CancellationToken.None))
        {
            SvgSceneBuilder source = state.Scene;
            int enclosing = source.AddMaskContent(new([], 255, true) { ConstantAlpha = 191 });
            int child = source.AddMaskContent(new([], 255, true) { ConstantAlpha = 127, BoundsClipContentIndex = enclosing });
            destination.AddMaskContent(new([], 255, true) { ConstantAlpha = 63 });
            SvgLeafMaskOperation outer = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            outer.Resolved = true; outer.Value = 255; outer.RegionKind = SvgLayerRegionKind.AffineViewport;
            outer.ContentIndex = enclosing; outer.Region = new(0, 0, 8, 8);
            SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            operation.Resolved = true; operation.Value = 255; operation.RegionKind = SvgLayerRegionKind.AffineViewport;
            operation.ContentIndex = child; operation.EnclosingClip = outer; operation.Region = new(1, 1, 6, 6);
            SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, operation, null);
            using (SvgLeafMaskMapper mapper = SvgLeafMaskMapper.CreateSceneTransfer(state, source, destination, CancellationToken.None))
            {
                SvgLeafMaskBinding mapped = Assert.IsType<SvgLeafMaskBinding>(mapper.Map(binding));
                Assert.Same(mapped, mapper.Map(binding));
                SvgLeafMaskBinding sibling = SvgLeafMaskComposition.NewBinding(state, operation, null);
                Assert.Same(mapped.Operation, Assert.IsType<SvgLeafMaskBinding>(mapper.Map(sibling)).Operation);
                SvgLeafMaskOperation mappedOuter = Assert.IsType<SvgLeafMaskOperation>(mapped.Operation.EnclosingClip);
                Assert.Equal(2, mapped.Operation.ContentIndex);
                Assert.Equal(1, mappedOuter.ContentIndex);
                Assert.Equal(1, destination.MaskContentAt(2).BoundsClipContentIndex);
                Assert.Equal((byte)127, destination.MaskContentAt(2).ConstantAlpha);
                Assert.Equal((byte)191, destination.MaskContentAt(1).ConstantAlpha);
                Assert.Equal((byte)63, destination.MaskContentAt(0).ConstantAlpha);
                Assert.Equal(0, outer.ContentIndex); Assert.Equal(1, operation.ContentIndex);
                Assert.Equal(0, source.MaskContentAt(1).BoundsClipContentIndex);
                Assert.Same(outer, operation.EnclosingClip);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Cancelled_transfer_releases_scratch_and_preserves_source_for_retry()
    {
        RenderBudget budget = new(2_000_000);
        using (SvgDocumentState state = new(budget, CancellationToken.None))
        using (SvgSceneBuilder destination = new(budget, CancellationToken.None))
        {
            int sourceIndex = state.Scene.AddMaskContent(new([], 255, true) { ConstantAlpha = 191 });
            SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            operation.Resolved = true; operation.Value = 255; operation.ContentIndex = sourceIndex;
            SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, operation, null);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            long before = budget.Snapshot().CurrentBytes;
            using (SvgLeafMaskMapper denied = SvgLeafMaskMapper.CreateSceneTransfer(state, state.Scene, destination,
                cancellation.Token))
            {
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => denied.Map(binding)).CancellationToken);
            }
            Assert.Equal(before, budget.Snapshot().CurrentBytes);
            Assert.Equal(sourceIndex, operation.ContentIndex);
            Assert.Equal((byte)191, state.Scene.MaskContentAt(sourceIndex).ConstantAlpha);
            using SvgLeafMaskMapper retry = SvgLeafMaskMapper.CreateSceneTransfer(state, state.Scene, destination, CancellationToken.None);
            SvgLeafMaskBinding mapped = Assert.IsType<SvgLeafMaskBinding>(retry.Map(binding));
            Assert.Equal((byte)191, destination.MaskContentAt(mapped.Operation.ContentIndex).ConstantAlpha);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
