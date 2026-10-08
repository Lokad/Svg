using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns an immutable vector tile scene and its source-coordinate grid.</summary>
/// <remarks>The parent scene adopts this owner once. Pattern paints borrow it until parent disposal;
/// rasterization borrows its scene without disposing it. Creation charges metadata before allocation.
/// Conversion is single-threaded; callers must not dispose this owner during replay. Cleanup observes no cancellation.</remarks>
internal sealed class SvgPatternContent : IDisposable
{
    private SvgSceneStorage? _scene;
    private RenderBudget.BudgetReservation? _reservation;

    private SvgPatternContent(SvgSceneStorage scene, SvgPatternTile tile, RenderBudget.BudgetReservation reservation)
    {
        _scene = scene;
        _reservation = reservation;
        SourceBox = new(tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight,
            SvgViewBoxAlignment.None, SvgViewBoxAlignment.None, SvgViewBoxFit.None);
    }

    internal SvgSceneStorage Scene => _scene ?? throw new ObjectDisposedException(nameof(SvgPatternContent));
    internal SvgViewBox SourceBox { get; }

    /// <summary>Adopts a sealed tile scene, including disposal on allocation/accounting failure.</summary>
    internal static SvgPatternContent Create(SvgSceneStorage scene, SvgPatternTile tile, RenderBudget budget)
    {
        RenderBudget.BudgetReservation? reservation = null;
        try
        {
            reservation = budget.Reserve(128, SvgMemoryCategory.Scene);
            return new(scene, tile, reservation);
        }
        catch { reservation?.Dispose(); scene.Dispose(); throw; }
    }

    /// <summary>Releases tile commands, geometry and metadata once; borrowed paint references own no storage.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _scene, null)?.Dispose();
        Interlocked.Exchange(ref _reservation, null)?.Dispose();
    }
}

/// <summary>Stores one owned tile reference in the budgeted value-type scene arena.</summary>
internal readonly record struct SvgPatternContentOwner(SvgPatternContent Content) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
