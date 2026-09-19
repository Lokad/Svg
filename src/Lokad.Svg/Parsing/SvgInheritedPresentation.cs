namespace Lokad.Svg.Parsing;

/// <summary>Represents the resolved container state inherited by a paintable child, including its accumulated transform.</summary>
internal readonly record struct SvgInheritedPresentation(SvgPaint Paint, bool IsDisplayed, bool IsVisible, AxisTransform Transform);
