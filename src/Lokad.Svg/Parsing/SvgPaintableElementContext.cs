using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Supplies one paintable element with its inherited rendering context and bounded style resolution.</summary>
internal readonly record struct SvgPaintableElementContext(SvgPaint InheritedPaint, bool InheritedDisplayed, bool InheritedVisibility, AxisTransform InheritedTransform, InlineStyle StyleSheet, IReadOnlyDictionary<string, SvgColor> NamedColors, ParserWorkCounter WorkCounter, CancellationToken CancellationToken)
{
    internal SvgComputedStyle ResolveStyle(XmlReader reader) => SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken);

    internal AxisTransform ResolveTransform(XmlReader reader) => InheritedTransform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));

    internal SvgResolvedShapePresentation ResolvePresentation(SvgComputedStyle style, bool acceptsStroke)
        => SvgShapePresentationResolver.Resolve(style, InheritedPaint, InheritedDisplayed, InheritedVisibility, acceptsStroke, NamedColors);
}
