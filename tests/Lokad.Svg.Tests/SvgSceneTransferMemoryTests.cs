using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Xunit;

namespace Lokad.Svg.Tests;

public sealed class SvgSceneTransferMemoryTests
{
    [Fact]
    public void Memory_refusal_during_content_transfer_releases_every_owner_and_allows_a_fresh_retry()
    {
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(2_000_000));
        Transfer(measured, out bool measuredTransfer);
        Assert.True(measuredTransfer);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        bool enteredTransfer = false;
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Transfer(denied, out enteredTransfer)).ErrorCode);
        Assert.True(enteredTransfer);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget retry = new RenderBudget(new HostMemoryBudget(2_000_000));
        Transfer(retry, out bool retriedTransfer);
        Assert.True(retriedTransfer);
        Assert.Equal(0, retry.Snapshot().CurrentBytes);

        static void Transfer(RenderBudget budget, out bool enteredTransfer)
        {
            enteredTransfer = false;
            using SvgDocumentState state = new(budget, CancellationToken.None);
            using SvgSceneBuilder destination = new(budget, CancellationToken.None);
            int bounds = state.Scene.AddMaskContent(new([], 255, true) { ConstantAlpha = 191 });
            int content = state.Scene.AddMaskContent(new([], 255, true) { ConstantAlpha = 127, BoundsClipContentIndex = bounds });
            SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            operation.Resolved = true; operation.Value = 255; operation.ContentIndex = content;
            SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, operation, null);
            using SvgLeafMaskMapper mapper = SvgLeafMaskMapper.CreateSceneTransfer(state, state.Scene, destination, CancellationToken.None);
            enteredTransfer = true;
            SvgLeafMaskBinding mapped = Assert.IsType<SvgLeafMaskBinding>(mapper.Map(binding));
            Assert.Equal((byte)127, destination.MaskContentAt(mapped.Operation.ContentIndex).ConstantAlpha);
            Assert.Equal(content, operation.ContentIndex);
            Assert.Equal(bounds, state.Scene.MaskContentAt(content).BoundsClipContentIndex);
        }
    }
}
