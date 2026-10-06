namespace Lokad.Svg.Rendering;

/// <summary>Identifies edge-construction clipping independently of row storage and paint clipping.</summary>
/// <remarks>The bounds are an outward-rounded, canvas-clamped device clip. BitmapOrigin belongs to the owning software bitmap, even when an SVG clip narrows Bounds. Null at the preparation boundary selects the existing uncut construction. Fractional coverage remains owned by the paint consumer. This immutable value owns no storage.</remarks>
internal readonly record struct SvgAnalyticClipContext(SvgAnalyticClipBounds Bounds, SvgCpuBitmapOrigin BitmapOrigin)
{
    /// <summary>Constructs a clip for an unshifted bitmap.</summary>
    internal SvgAnalyticClipContext(SvgAnalyticClipBounds bounds) : this(bounds, new(0, 0)) { }
}
