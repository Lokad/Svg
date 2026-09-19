using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Supplies one paintable element with its inherited rendering context and bounded style resolution.</summary>
internal readonly record struct SvgPaintableElementContext(SvgPaint InheritedPaint, bool InheritedDisplayed, bool InheritedVisibility, AxisTransform InheritedTransform, InlineStyle StyleSheet, IReadOnlyDictionary<string, SvgColor> NamedColors, ParserWorkCounter WorkCounter, CancellationToken CancellationToken)
{
    internal static SvgPaintableElementContext Create(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgGroupScope? parent = state.Groups.Count == 0 ? null : state.Groups[^1];
        return new SvgPaintableElementContext(parent?.Paint ?? root.Paint, parent?.IsDisplayed ?? root.IsDisplayed, parent?.IsVisible ?? root.IsVisible, parent?.Transform ?? AxisTransform.Identity, SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken), namedColors, workCounter, cancellationToken);
    }

    internal SvgComputedStyle ResolveStyle(XmlReader reader) => SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken);

    internal AxisTransform ResolveTransform(XmlReader reader) => InheritedTransform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));

    internal SvgResolvedShapePresentation ResolvePresentation(SvgComputedStyle style, bool acceptsStroke)
        => SvgShapePresentationResolver.Resolve(style, InheritedPaint, InheritedDisplayed, InheritedVisibility, acceptsStroke, NamedColors);
}
