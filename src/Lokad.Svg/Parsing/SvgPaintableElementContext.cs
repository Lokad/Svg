using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Supplies one paintable element with its inherited rendering context and bounded style resolution.</summary>
internal readonly record struct SvgPaintableElementContext(SvgInheritedPresentation InheritedPresentation, InlineStyle StyleSheet, IReadOnlyDictionary<string, SvgColor> NamedColors, ParserWorkCounter WorkCounter, CancellationToken CancellationToken)
{
    internal static SvgPaintableElementContext Create(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgInheritedPresentation inheritedPresentation = state.Groups.Count == 0 ? root.InheritedPresentation : state.Groups[^1].InheritedPresentation;
        return new SvgPaintableElementContext(inheritedPresentation, SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken), namedColors, workCounter, cancellationToken);
    }

    internal SvgComputedStyle ResolveStyle(XmlReader reader) => SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken);

    internal SvgComputedStyle ResolveStyle(XmlReader reader, bool allowsDirectTextFontProperties) => SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken, allowsDirectTextFontProperties);

    internal AxisTransform ResolveTransform(XmlReader reader) => InheritedPresentation.Transform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));

    internal SvgResolvedShapePresentation ResolvePresentation(SvgComputedStyle style, bool acceptsStroke)
        => SvgShapePresentationResolver.Resolve(style, InheritedPresentation.Paint, InheritedPresentation.IsDisplayed, InheritedPresentation.IsVisible, acceptsStroke, NamedColors);

    internal SvgResolvedPaintableElement ResolveShape(XmlReader reader, bool acceptsStroke)
    {
        SvgComputedStyle style = ResolveStyle(reader);
        return new SvgResolvedPaintableElement(style, ResolveTransform(reader), ResolvePresentation(style, acceptsStroke));
    }

    internal SvgResolvedPaintableElement ResolveShape(XmlReader reader, bool acceptsStroke, bool allowsDirectTextFontProperties)
    {
        SvgComputedStyle style = ResolveStyle(reader, allowsDirectTextFontProperties);
        return new SvgResolvedPaintableElement(style, ResolveTransform(reader), ResolvePresentation(style, acceptsStroke));
    }
}

/// <summary>Pairs one computed style with the inherited transform and resolved presentation used by a shape lowerer.</summary>
internal readonly record struct SvgResolvedPaintableElement(SvgComputedStyle Style, AxisTransform Transform, SvgResolvedShapePresentation Presentation);
