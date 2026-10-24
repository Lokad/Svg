using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgViewportContextStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_allowance_covers_its_allocation_and_releases_failed_registration(bool realViewport)
    {
        RenderBudget budget = new(1_000_000);
        using CancellationTokenSource cancellation = new();
        using (RenderBudget.BudgetReservation borrowed = budget.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgDocumentState state = new(budget, new(100_000), cancellation.Token))
            {
                SvgInheritedContainer? inherited = realViewport
                    ? new(new(default, true, true, SvgAffineTransform.Identity), SvgInheritedTextStyle.Default, null, 255, SvgInheritedStrokeStyle.Default)
                    : null;
                SvgAffineViewportClip viewport = new(new(0, 0, 32, 32), SvgAffineTransform.Identity);
                _ = SvgViewportTextContext.Create(state, inherited, SvgAffineTransform.Identity, viewport);
                // Warm construction and owner-list capacity before measuring the
                // independently retained context and its reservation token.
                long retained = budget.Snapshot().CurrentBytes;
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                _ = SvgViewportTextContext.Create(state, inherited, SvgAffineTransform.Identity, viewport);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                long charged = budget.Snapshot().CurrentBytes - retained;
                Assert.True(allocated > 0);
                Assert.True(charged >= allocated, $"Charged {charged} bytes for {allocated} allocated bytes.");
                retained = budget.Snapshot().CurrentBytes;
                using (RenderBudget.BudgetReservation pressure = budget.Reserve(budget.AvailableBytes - allocated + 1, SvgMemoryCategory.Surface))
                {
                    long held = budget.Snapshot().CurrentBytes;
                    Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                        SvgViewportTextContext.Create(state, inherited, SvgAffineTransform.Identity, viewport)).ErrorCode);
                    Assert.Equal(held, budget.Snapshot().CurrentBytes);
                    Assert.Equal(2, state.LeafMaskOwners.Count);
                }
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                _ = SvgViewportTextContext.Create(state, inherited, SvgAffineTransform.Identity, viewport);
                Assert.Equal(3, state.LeafMaskOwners.Count);
                _ = SvgViewportTextContext.Create(state, inherited, SvgAffineTransform.Identity, viewport);
                Assert.Equal(4, state.LeafMaskOwners.Count);
                retained = budget.Snapshot().CurrentBytes;
                // The next registry insertion must grow its backing store,
                // observing cancellation after the context allowance is held.
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                    SvgViewportTextContext.Create(state, inherited, SvgAffineTransform.Identity, viewport)).CancellationToken);
                Assert.Equal(retained, budget.Snapshot().CurrentBytes);
                Assert.Equal(4, state.LeafMaskOwners.Count);
            }
            Assert.Equal(73, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
