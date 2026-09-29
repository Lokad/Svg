namespace Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

/// <summary>Represents the resolved container state inherited by a paintable child, including its accumulated transform.</summary>
internal readonly record struct SvgInheritedPresentation(SvgPaint Paint, bool IsDisplayed, bool IsVisible, SvgAffineTransform Transform);
