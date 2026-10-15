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

    internal SvgClipBox SceneBounds(ParserWorkCounter work, CancellationToken token)
    {
        SvgPaintBounds source = new((float)Region.X, (float)Region.Y, (float)Region.Right, (float)Region.Bottom);
        SvgPaintBounds mapped = source.Map(Map, work, token);
        return new(mapped.Left, mapped.Top, Math.Max(0, mapped.Right - mapped.Left), Math.Max(0, mapped.Bottom - mapped.Top));
    }
}
