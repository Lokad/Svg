namespace Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Represents the resolved container state inherited by a paintable child, including its accumulated transform.</summary>
internal readonly record struct SvgInheritedPresentation(SvgPaint Paint, bool IsDisplayed, bool IsVisible, SvgAffineTransform Transform)
{
    /// <summary>Retains the budget-owned local fill URL independently of numeric opacity and solid paint.</summary>
    internal string? PaintServerFill { get; init; }
    // Vector effect is initially none; children borrow this only for explicit inherit.
    internal bool NonScalingStroke { get; init; }
    internal SvgPaintOrder PaintOrder { get; init; }
    internal SvgMarkerStyle Markers { get; init; }
    /// <summary>Stores this element's opacity independently of accumulated opacity and masks; absence uses the initial value.</summary>
    internal SvgPaintOpacity? OwnOpacity { get; init; }
    /// <summary>Keeps this element's own computed display separate from cumulative suppression.</summary>
    internal SvgDisplay ElementDisplay { get; init; }
    internal SvgPaintOpacity ElementOpacity => OwnOpacity ?? SvgPaintOpacity.Opaque;
}
