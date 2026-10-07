using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Supplies one paintable element with its inherited rendering context and bounded style resolution.</summary>
internal readonly record struct SvgPaintableElementContext(SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, SvgInheritedStrokeStyle InheritedStroke, InlineStyle StyleSheet, IReadOnlyDictionary<string, SvgColor> NamedColors, ParserWorkCounter WorkCounter, CancellationToken CancellationToken, double OutputMagnification, string? InheritedFillRule)
{
    /// <summary>Preserves a closed definition's source path for stroke paint supplied by a later instance.</summary>
    internal bool RetainInstanceStrokePath { get; init; }

    internal static SvgPaintableElementContext Create(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgInheritedPresentation inheritedPresentation = state.Groups.Count == 0 ? root.InheritedPresentation : state.Groups[^1].InheritedPresentation;
        SvgInheritedTextStyle inheritedTextStyle = state.Groups.Count == 0 ? root.InheritedTextStyle : state.Groups[^1].InheritedTextStyle;
        SvgInheritedStrokeStyle inheritedStroke = state.Groups.Count == 0 ? root.InheritedStroke : state.Groups[^1].InheritedStroke;
        string? inheritedFillRule = state.Groups.Count == 0 ? root.FillRule : state.Groups[^1].FillRule;
        return new SvgPaintableElementContext(inheritedPresentation, inheritedTextStyle, inheritedStroke, SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken), namedColors, workCounter, cancellationToken, state.OutputMagnification, inheritedFillRule);
    }

    internal SvgComputedStyle ResolveStyle(XmlReader reader)
    {
        SvgComputedStyle style = InheritedStroke.ApplyTo(SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken));
        return style.FillRule is null && InheritedFillRule is not null ? style with { FillRule = InheritedFillRule } : style;
    }

    internal SvgComputedStyle ResolveStyle(XmlReader reader, bool allowsDirectTextFontProperties)
    {
        SvgComputedStyle style = InheritedStroke.ApplyTo(SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken, allowsDirectTextFontProperties));
        return style.FillRule is null && InheritedFillRule is not null ? style with { FillRule = InheritedFillRule } : style;
    }

    internal SvgAffineTransform ResolveTransform(XmlReader reader) => InheritedPresentation.Transform.ComposeChecked(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform"), WorkCounter, CancellationToken));

    internal SvgResolvedShapePresentation ResolvePresentation(SvgComputedStyle style, bool acceptsStroke)
        => SvgShapePresentationResolver.Resolve(style, InheritedPresentation.Paint, InheritedPresentation.IsDisplayed, InheritedPresentation.IsVisible, acceptsStroke, NamedColors);

    internal SvgResolvedPaintableElement ResolveShape(XmlReader reader, bool acceptsStroke)
    {
        SvgComputedStyle style = ResolveStyle(reader);
        return ResolveShape(style, reader, acceptsStroke);
    }

    internal SvgResolvedPaintableElement ResolveShape(SvgComputedStyle style, XmlReader reader, bool acceptsStroke)
    {
        // Container stroke paint flows to shapes without their own; paint servers keep their dedicated rejection, except at zero width where nothing paints.
        if (style.Stroke is null && InheritedStroke.Stroke is not null && !SvgStrokeStyleSyntax.IsZeroStrokeWidth(style.Width)) SvgStrokeStyleSyntax.RejectPaintServerStroke(InheritedStroke.Stroke, reader.LocalName);
        SvgComputedStyle painted = style.Stroke is null && InheritedStroke.Stroke is not null ? style with { Stroke = InheritedStroke.Stroke } : style;
        return new SvgResolvedPaintableElement(painted, ResolveTransform(reader), ResolvePresentation(painted, acceptsStroke), OutputMagnification);
    }

    internal SvgResolvedPaintableElement ResolveShape(XmlReader reader, bool acceptsStroke, bool allowsDirectTextFontProperties)
    {
        SvgComputedStyle style = ResolveStyle(reader, allowsDirectTextFontProperties);
        return new SvgResolvedPaintableElement(style, ResolveTransform(reader), ResolvePresentation(style, acceptsStroke), OutputMagnification);
    }

    /// <summary>Resolves direct text paint and retains its inline font declarations from the same bounded style scan.</summary>
    internal SvgResolvedDirectTextElement ResolveTextShape(XmlReader reader)
    {
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), WorkCounter, CancellationToken, true);
        SvgComputedStyle style = InheritedStroke.ApplyTo(SvgComputedStyle.Resolve(reader, inline, StyleSheet));
        SvgComputedStyle textStyle = style.FillRule is null && InheritedFillRule is not null ? style with { FillRule = InheritedFillRule } : style;
        return new SvgResolvedDirectTextElement(new SvgResolvedPaintableElement(textStyle, ResolveTransform(reader), ResolvePresentation(textStyle, false), OutputMagnification), SvgInlineStyleParser.MergeFont(inline.TextFont, inline.Important, StyleSheet.TextFont, StyleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inline.WhiteSpace, inline.Important, InlineImportantProperties.WhiteSpace, StyleSheet.WhiteSpace, StyleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inline.LetterSpacing, inline.Important, InlineImportantProperties.LetterSpacing, StyleSheet.LetterSpacing, StyleSheet.Important));
    }
}

/// <summary>Pairs one computed style with the inherited transform and resolved presentation used by a shape lowerer.</summary>
internal readonly record struct SvgResolvedPaintableElement(SvgComputedStyle Style, SvgAffineTransform Transform, SvgResolvedShapePresentation Presentation, double OutputMagnification);

/// <summary>Pairs direct-text paint resolution with text declarations from its single style scan.</summary>
internal readonly record struct SvgResolvedDirectTextElement(SvgResolvedPaintableElement PaintableElement, SvgTextInlineFontProperties InlineFont, string? WhiteSpace, string? LetterSpacing);
