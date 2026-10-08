using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns an immutable vector replay scene for pattern tiles and mask contents.</summary>
/// <remarks>The parent scene adopts this owner once. Paints and masks borrow it until parent disposal;
/// rasterization borrows its scene without disposing it. Creation charges metadata before allocation.
/// Conversion is single-threaded; callers must not dispose this owner during replay. Cleanup observes no cancellation.</remarks>
internal sealed class SvgVectorContent : IDisposable
{
    private SvgSceneStorage? _scene;
    private RenderBudget.BudgetReservation? _reservation;

    private SvgVectorContent(SvgSceneStorage scene, RenderBudget.BudgetReservation reservation)
    {
        _scene = scene;
        _reservation = reservation;
    }

    internal SvgSceneStorage Scene => _scene ?? throw new ObjectDisposedException(nameof(SvgVectorContent));

    /// <summary>Adopts a sealed replay scene, including disposal on allocation/accounting failure.</summary>
    internal static SvgVectorContent Create(SvgSceneStorage scene, RenderBudget budget)
    {
        RenderBudget.BudgetReservation? reservation = null;
        try
        {
            reservation = budget.Reserve(128, SvgMemoryCategory.Scene);
            return new(scene, reservation);
        }
        catch { reservation?.Dispose(); scene.Dispose(); throw; }
    }

    /// <summary>Releases replay commands, geometry and metadata once; borrowed paint references own no storage.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _scene, null)?.Dispose();
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}

/// <summary>Stores one owned vector-scene reference in the budgeted value-type scene arena.</summary>
internal readonly record struct SvgVectorContentOwner(SvgVectorContent Content) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
