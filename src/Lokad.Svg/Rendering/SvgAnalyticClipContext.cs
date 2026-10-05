namespace Lokad.Svg.Rendering;

/// <summary>Identifies edge-construction clipping independently of row storage and paint clipping.</summary>
/// <remarks>The bounds are an outward-rounded, canvas-clamped device clip. Null at the preparation boundary selects the existing uncut construction. Fractional coverage remains owned by the paint consumer.</remarks>
internal readonly record struct SvgAnalyticClipContext(SvgAnalyticClipBounds Bounds);
