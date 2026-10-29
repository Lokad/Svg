using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Keeps a target's object-space bounds and mask region beside its complete placement.</summary>
/// <remarks>Values own no storage. Bounds are collected before target placement; region and content
/// units remain independent. Mapping observes cancellation during bounded native corner visits.</remarks>
internal readonly record struct SvgMaskTarget(SvgAffineTransform Map, SvgRectangle ObjectBounds, SvgClipBox Region)
{
    internal static SvgMaskTarget Create(string id, SvgAffineTransform map, SvgRectangle bounds, SvgDocumentState state)
    {
        SvgClipBox region;
        if (state.MaskRegions.TryGetValue(id, out SvgClipBox explicitRegion)) region = explicitRegion;
        else if (state.MaskFractionRegions.TryGetValue(id, out SvgClipBox fractions))
            region = new(bounds.X + fractions.X * bounds.Width, bounds.Y + fractions.Y * bounds.Height,
                fractions.Width * bounds.Width, fractions.Height * bounds.Height);
        else region = SvgMaskSyntax.MaskRegion(bounds);
        return new(map, bounds, region);
    }

    /// <summary>Encloses the union of an axis-aligned mask region and target visual bounds before placement.</summary>
    /// <remarks>Returns no descriptor when the ordinary scene-space enclosure suffices. Values own
    /// no storage; visual mapping and four enclosure boundaries observe cancellation.</remarks>
    internal static SvgClipBox? SourcePaintRegion(string id, SvgAffineTransform map, SvgRectangle bounds, SvgPaintBounds sceneVisualBounds, SvgClipBox sceneRegion,
        SvgDocumentState state, CancellationToken token)
    {
        if (!map.IsAxisAligned || !map.TryInvert(out SvgAffineTransform inverse)) return null;
        token.ThrowIfCancellationRequested();
        SvgMaskTarget target = Create(id, map, inverse.Apply(bounds), state);
        SvgClipBox region = target.Region;
        SvgPaintBounds visual = sceneVisualBounds.Map(inverse, token);
        double x = Math.Min(region.X, visual.Left), y = Math.Min(region.Y, visual.Top);
        SvgClipBox box = new(x, y, Math.Max(region.Right, visual.Right) - x,
            Math.Max(region.Bottom, visual.Bottom) - y);
        float left = MathF.Floor((float)box.X), top = MathF.Floor((float)box.Y);
        float right = MathF.Ceiling((float)box.Right), bottom = MathF.Ceiling((float)box.Bottom);
        SvgClipBox placed = map.Apply(new SvgClipBox(left, top, right - left, bottom - top));
        if (placed.X == MathF.Floor((float)sceneRegion.X) && placed.Y == MathF.Floor((float)sceneRegion.Y)
            && placed.Right == MathF.Ceiling((float)sceneRegion.Right) && placed.Bottom == MathF.Ceiling((float)sceneRegion.Bottom)) return null;
        return placed;
    }

    internal SvgClipBox SceneBounds(CancellationToken token)
    {
        SvgPaintBounds source = new((float)Region.X, (float)Region.Y, (float)Region.Right, (float)Region.Bottom);
        SvgPaintBounds mapped = source.Map(Map, token);
        return new(mapped.Left, mapped.Top, Math.Max(0, mapped.Right - mapped.Left), Math.Max(0, mapped.Bottom - mapped.Top));
    }
}
