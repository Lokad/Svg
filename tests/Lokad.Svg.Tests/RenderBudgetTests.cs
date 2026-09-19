using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class RenderBudgetTests
{
    [Fact]
    public void Reservation_tracks_current_peak_and_category_bytes()
    {
        RenderBudget budget = new(100);

        using (budget.Reserve(30, SvgMemoryCategory.Parser))
        using (budget.Reserve(50, SvgMemoryCategory.Surface))
        {
            RenderBudgetSnapshot snapshot = budget.Snapshot();
            Assert.Equal(80, snapshot.CurrentBytes);
            Assert.Equal(80, snapshot.PeakBytes);
            Assert.Equal(30, snapshot.BytesByCategory[(int)SvgMemoryCategory.Parser]);
            Assert.Equal(50, snapshot.BytesByCategory[(int)SvgMemoryCategory.Surface]);
        }

        RenderBudgetSnapshot released = budget.Snapshot();
        Assert.Equal(0, released.CurrentBytes);
        Assert.Equal(80, released.PeakBytes);
    }

    [Fact]
    public void Over_budget_reservation_fails_without_changing_live_bytes()
    {
        RenderBudget budget = new(100);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(80, SvgMemoryCategory.Scene);

        SvgException exception = Assert.Throws<SvgException>(() => budget.Reserve(21, SvgMemoryCategory.Geometry));
        RenderBudgetSnapshot snapshot = budget.Snapshot();

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(80, snapshot.CurrentBytes);
        Assert.Equal(80, snapshot.PeakBytes);
        Assert.Equal(1, snapshot.DeniedReservationCount);
        Assert.Equal(0, snapshot.BytesByCategory[(int)SvgMemoryCategory.Geometry]);
    }

    [Fact]
    public void Reservation_dispose_is_idempotent()
    {
        RenderBudget budget = new(10);
        RenderBudget.BudgetReservation reservation = budget.Reserve(10, SvgMemoryCategory.Output);

        reservation.Dispose();
        reservation.Dispose();

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Arithmetic_does_not_overflow_at_large_limits()
    {
        RenderBudget budget = new(long.MaxValue);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(long.MaxValue, SvgMemoryCategory.Temporary);

        SvgException exception = Assert.Throws<SvgException>(() => budget.Reserve(1, SvgMemoryCategory.Temporary));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(long.MaxValue, budget.Snapshot().CurrentBytes);
    }
}
