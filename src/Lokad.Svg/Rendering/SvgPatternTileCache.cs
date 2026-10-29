using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Owns one optional tile raster between consecutive compatible rectangle fills.</summary>
/// <remarks>Conversion-thread ownership only. Borrowers finish before replacement/disposal.
/// The parent scene keeps content alive. Source and mapped metadata, surface dimensions,
/// sampling maps and paint must agree; destination fill rectangles are independent.
/// Tracking uses one conservative aggregate allowance. Admission refusal leaves the
/// caller owning the supplied surface and fails the operation. Disposal releases both owners.</remarks>
internal sealed class SvgPatternTileCache : IDisposable
{
    private const long TrackingBytes = 608;
    private SvgPatternGeometry? _source;
    private SvgPatternGeometry? _mapped;
    private SvgConverter.RasterizedSurface? _surface;
    private readonly RenderBudget.BudgetReservation _reservation;

    private SvgPatternTileCache(SvgPatternGeometry source, SvgPatternGeometry mapped, SvgConverter.RasterizedSurface surface, RenderBudget.BudgetReservation reservation)
    {
        _source = source;
        _mapped = mapped;
        _surface = surface;
        _reservation = reservation;
    }

    internal static SvgPatternTileCache Retain(SvgPatternGeometry source, SvgPatternGeometry mapped, SvgConverter.RasterizedSurface surface, RenderBudget budget)
    {
        RenderBudget.BudgetReservation reservation = budget.Reserve(TrackingBytes, SvgMemoryCategory.Temporary);
        try { return new(source, mapped, surface, reservation); }
        catch { reservation.Dispose(); throw; }
    }

    internal bool MatchesSource(SvgPatternGeometry source) => _source is {} cached && SameTile(cached, source);

    internal bool TryBorrow(SvgPatternGeometry mapped, out SvgConverter.RasterizedSurface surface)
    {
        if (_mapped is {} cached && _surface is {} value && SameTile(cached, mapped))
        {
            surface = value;
            return true;
        }
        surface = default;
        return false;
    }

    internal static bool SameTile(SvgPatternGeometry first, SvgPatternGeometry second)
        => first.Content == second.Content
            && ReferenceEquals(first.VectorContent, second.VectorContent)
            && Equals(first.Coordinates, second.Coordinates)
            && Equals(first.PaintMatrices, second.PaintMatrices)
            && first.ContentViewBox == second.ContentViewBox
            && first.OriginX == second.OriginX && first.OriginY == second.OriginY
            && first.TileWidth == second.TileWidth && first.TileHeight == second.TileHeight
            && first.RasterSourceBox == second.RasterSourceBox
            && first.ShaderPlacement == second.ShaderPlacement
            && first.NativeImageMap == second.NativeImageMap && first.NativeImageLocal == second.NativeImageLocal
            && first.ContentPaint == second.ContentPaint && first.Opacity == second.Opacity;

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
        _source = null;
        _mapped = null;
        _reservation.Dispose();
    }
}

/// <summary>Borrows a retained tile or owns an unretained tile until one paint completes.</summary>
internal readonly record struct SvgPatternTileLease(SvgConverter.RasterizedSurface Surface, bool OwnsSurface) : IDisposable
{
    public void Dispose()
    {
        if (OwnsSurface) Surface.Dispose();
    }
}
