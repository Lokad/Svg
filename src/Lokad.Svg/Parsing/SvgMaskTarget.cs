using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Keeps a target's object-space bounds and mask region beside its complete placement.</summary>
/// <remarks>Values own no storage. Bounds are collected before target placement; region and content
/// units remain independent. Mapping charges bounded native corner visits and observes cancellation.</remarks>
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

    /// <summary>Encloses an axis-aligned mask's source clip before placement, preserving fractional poses.</summary>
    /// <remarks>Returns no descriptor when the ordinary scene-space enclosure suffices. Values own
    /// no storage; four boundary visits charge shared work and cancellation before mapping.</remarks>
    internal static SvgClipBox? SourcePaintRegion(string id, SvgAffineTransform map, SvgRectangle bounds, SvgClipBox sceneRegion,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (!map.IsAxisAligned || !map.TryInvert(out SvgAffineTransform inverse)) return null;
        for (int index = 0; index < 4; index++) work.Consume(token);
        SvgMaskTarget target = Create(id, map, inverse.Apply(bounds), state);
        SvgClipBox box = target.Region;
        float left = MathF.Floor((float)box.X), top = MathF.Floor((float)box.Y);
        float right = MathF.Ceiling((float)box.Right), bottom = MathF.Ceiling((float)box.Bottom);
        SvgClipBox placed = map.Apply(new SvgClipBox(left, top, right - left, bottom - top));
        if (placed.X == MathF.Floor((float)sceneRegion.X) && placed.Y == MathF.Floor((float)sceneRegion.Y)
            && placed.Right == MathF.Ceiling((float)sceneRegion.Right) && placed.Bottom == MathF.Ceiling((float)sceneRegion.Bottom)) return null;
        return placed;
    }

    internal SvgClipBox SceneBounds(ParserWorkCounter work, CancellationToken token)
    {
        SvgPaintBounds source = new((float)Region.X, (float)Region.Y, (float)Region.Right, (float)Region.Bottom);
        SvgPaintBounds mapped = source.Map(Map, work, token);
        return new(mapped.Left, mapped.Top, Math.Max(0, mapped.Right - mapped.Left), Math.Max(0, mapped.Bottom - mapped.Top));
    }
}
