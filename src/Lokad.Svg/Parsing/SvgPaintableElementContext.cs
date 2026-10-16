using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Supplies one paintable element with its inherited rendering context and bounded style resolution.</summary>
internal readonly record struct SvgPaintableElementContext(SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, SvgInheritedStrokeStyle InheritedStroke, InlineStyle StyleSheet, IReadOnlyDictionary<string, SvgColor> NamedColors, ParserWorkCounter WorkCounter, CancellationToken CancellationToken, double OutputMagnification, string? InheritedFillRule)
{
    /// <summary>Maps this construction's scene coordinates to the outermost viewport, including deferred instance placement.</summary>
    internal SvgAffineTransform StrokeHostMap { get; init; } = SvgAffineTransform.Identity;
    internal SvgViewportLengthBases LengthBases { get; init; }
    internal bool DeferPaintOpacity { get; init; }
    internal SvgRecordingScope RecordingScope { get; init; }
    internal bool RetainInstanceStrokePath { get; init; }
    internal SvgComputedStyle? AuthoredStyle { get; init; }
    internal string? InheritedClipRule { get; init; }
    internal static SvgPaintableElementContext Create(XmlReader reader, bool directChild, SvgRootAttributes root, SvgDocumentState state, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgInheritedPresentation inheritedPresentation = state.Groups.Count == 0 ? root.InheritedPresentation : state.Groups[^1].InheritedPresentation;
        // Opacity is not inherited through an intervening viewport or defs node.
        // Other presentation keeps its existing independently inherited state.
        int opacityParentDepth = state.Groups.Count == 0 ? -1 : state.Groups[^1].Depth;
        if (state.EffectiveFrame is SvgViewportFrame parentViewport && parentViewport.Depth > opacityParentDepth)
        {
            inheritedPresentation = inheritedPresentation with { OwnOpacity = parentViewport.ElementOpacity };
            opacityParentDepth = parentViewport.Depth;
        }
        if (state.DefinitionsDepth > opacityParentDepth)
            inheritedPresentation = inheritedPresentation with { OwnOpacity = SvgPaintOpacity.Opaque };
        SvgInheritedTextStyle inheritedTextStyle = state.Groups.Count == 0 ? root.InheritedTextStyle : state.Groups[^1].InheritedTextStyle;
        SvgInheritedStrokeStyle inheritedStroke = state.Groups.Count == 0 ? root.InheritedStroke : state.Groups[^1].InheritedStroke;
        string? inheritedFillRule = state.Groups.Count == 0 ? root.FillRule : state.Groups[^1].FillRule;
        int styleParentDepth = state.Groups.Count == 0 ? -1 : state.Groups[^1].Depth;
        if (state.EffectiveFrame is SvgViewportFrame viewport && viewport.Depth > styleParentDepth && viewport.TextContext?.Inheritance is {} viewportStyle)
        {
            inheritedPresentation = viewportStyle.Presentation;
            inheritedTextStyle = viewportStyle.TextStyle;
            inheritedStroke = viewportStyle.Stroke;
            inheritedFillRule = viewportStyle.FillRule;
            styleParentDepth = viewport.Depth;
        }
        if (state.DefinitionsDepth > styleParentDepth && state.DefinitionInheritance is {} definitionStyle)
        {
            inheritedPresentation = definitionStyle.Presentation;
            inheritedTextStyle = definitionStyle.TextStyle;
            inheritedStroke = definitionStyle.Stroke;
            inheritedFillRule = definitionStyle.FillRule;
        }
        SvgViewportLengthBases lengthBases = new(state.EffectiveFrame?.ViewportWidth ?? state.SymbolViewBox?.Width ?? root.UserViewportWidth,
            state.EffectiveFrame?.ViewportHeight ?? state.SymbolViewBox?.Height ?? root.UserViewportHeight);
        if (state.MaskObjectBoundingBoxContent && reader.Depth > state.MaskDepth) lengthBases = new(1, 1);
        return new SvgPaintableElementContext(inheritedPresentation, inheritedTextStyle, inheritedStroke, SvgDocumentStyleResolver.Resolve(reader, directChild, state, workCounter, cancellationToken), namedColors, workCounter, cancellationToken, state.OutputMagnification, inheritedFillRule) { StrokeHostMap = state.OutputMap, InheritedClipRule = state.Groups.Count == 0 ? root.ClipRule : state.Groups[^1].ClipRule, RecordingScope = state.Groups.Count == 0 ? new(1) : state.Groups[^1].RecordingScope, LengthBases = lengthBases };
    }

    internal SvgComputedStyle ResolveStyle(XmlReader reader)
    {
        SvgComputedStyle style = InheritedStroke.ApplyTo(AuthoredStyle ?? SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken));
        if (AuthoredStyle is not null) style = style with { Stroke = style.Stroke ?? InheritedStroke.Stroke };
        style = style.ResolveVectorEffect(InheritedPresentation.NonScalingStroke).ResolveOpacity(InheritedPresentation.ElementOpacity) with { PaintOrder = style.PaintOrder ?? InheritedPresentation.PaintOrder };
        if (DeferPaintOpacity) style = style with { Opacity = null, ComputedOpacity = null };
        if (style.Fill is null && InheritedPresentation.PaintServerFill is string fill) style = style with { Fill = fill };
        return style.FillRule is null && InheritedFillRule is not null ? style with { FillRule = InheritedFillRule } : style;
    }

    internal SvgComputedStyle ResolveStyle(XmlReader reader, bool allowsDirectTextFontProperties)
    {
        SvgComputedStyle style = InheritedStroke.ApplyTo(AuthoredStyle ?? SvgComputedStyleResolver.Resolve(reader, StyleSheet, WorkCounter, CancellationToken, allowsDirectTextFontProperties));
        if (AuthoredStyle is not null) style = style with { Stroke = style.Stroke ?? InheritedStroke.Stroke };
        style = style.ResolveVectorEffect(InheritedPresentation.NonScalingStroke).ResolveOpacity(InheritedPresentation.ElementOpacity) with { PaintOrder = style.PaintOrder ?? InheritedPresentation.PaintOrder };
        if (DeferPaintOpacity) style = style with { Opacity = null, ComputedOpacity = null };
        if (style.Fill is null && InheritedPresentation.PaintServerFill is string fill) style = style with { Fill = fill };
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
        return new SvgResolvedPaintableElement(painted, ResolveTransform(reader), ResolvePresentation(painted, acceptsStroke), OutputMagnification) { StrokeHostMap = StrokeHostMap };
    }

    internal SvgResolvedPaintableElement ResolveShape(XmlReader reader, bool acceptsStroke, bool allowsDirectTextFontProperties)
    {
        SvgComputedStyle style = ResolveStyle(reader, allowsDirectTextFontProperties);
        return new SvgResolvedPaintableElement(style, ResolveTransform(reader), ResolvePresentation(style, acceptsStroke), OutputMagnification) { StrokeHostMap = StrokeHostMap };
    }

    /// <summary>Resolves direct text paint and retains its inline font declarations from the same bounded style scan.</summary>
    internal SvgResolvedDirectTextElement ResolveTextShape(XmlReader reader)
    {
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), WorkCounter, CancellationToken, true);
        SvgComputedStyle style = InheritedStroke.ApplyTo(SvgComputedStyle.Resolve(reader, inline, StyleSheet));
        style = style.ResolveVectorEffect(InheritedPresentation.NonScalingStroke).ResolveOpacity(InheritedPresentation.ElementOpacity) with { PaintOrder = style.PaintOrder ?? InheritedPresentation.PaintOrder };
        SvgComputedStyle textStyle = style.FillRule is null && InheritedFillRule is not null ? style with { FillRule = InheritedFillRule } : style;
        if (textStyle.Fill is null && InheritedPresentation.PaintServerFill is string fill) textStyle = textStyle with { Fill = fill };
        // Stroke is validated on emitted content after a child span can replace it.
        textStyle = textStyle with { Stroke = textStyle.Stroke ?? InheritedStroke.Stroke };
        return new SvgResolvedDirectTextElement(new SvgResolvedPaintableElement(textStyle, ResolveTransform(reader), ResolvePresentation(SvgTextPaintStyle.ForColorResolution(textStyle) with { Stroke = "none", Linecap = null, Linejoin = null, Dasharray = null, Dashoffset = null, Width = null, Miterlimit = null, StrokeOpacity = null }, false), OutputMagnification), SvgInlineStyleParser.MergeFont(inline.TextFont, inline.Important, StyleSheet.TextFont, StyleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inline.WhiteSpace, inline.Important, InlineImportantProperties.WhiteSpace, StyleSheet.WhiteSpace, StyleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inline.LetterSpacing, inline.Important, InlineImportantProperties.LetterSpacing, StyleSheet.LetterSpacing, StyleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inline.WordSpacing, inline.Important, InlineImportantProperties.WordSpacing, StyleSheet.WordSpacing, StyleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inline.TextDecoration, inline.Important, InlineImportantProperties.TextDecoration, StyleSheet.TextDecoration, StyleSheet.Important));
    }
}

/// <summary>Pairs one computed style with the inherited transform and resolved presentation used by a shape lowerer.</summary>
internal readonly record struct SvgResolvedPaintableElement(SvgComputedStyle Style, SvgAffineTransform Transform, SvgResolvedShapePresentation Presentation, double OutputMagnification)
{
    internal SvgAffineTransform StrokeHostMap { get; init; } = SvgAffineTransform.Identity;
}

/// <summary>Pairs direct-text paint resolution with text declarations from its single style scan.</summary>
internal readonly record struct SvgResolvedDirectTextElement(SvgResolvedPaintableElement PaintableElement, SvgTextInlineFontProperties InlineFont, string? WhiteSpace, string? LetterSpacing, string? WordSpacing, string? TextDecoration);
