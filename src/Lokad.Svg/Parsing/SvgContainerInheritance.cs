using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Combines inherited container state shared by font-request discovery and document lowering.</summary>
internal static class SvgContainerInheritance
{
    /// <summary>Resolves one group or link container from already-parsed style into the state its children inherit.</summary>
    internal static SvgInheritedContainer Resolve(
        XmlReader reader,
        InlineStyle inlineStyle,
        SvgComputedStyle computedStyle,
        InlineStyle styleSheet,
        SvgInheritedPresentation inheritedPresentation,
        string? inheritedFillRule,
        SvgInheritedTextStyle inheritedTextStyle,
        SvgInheritedStrokeStyle inheritedStroke,
        IReadOnlyDictionary<string, SvgColor> namedColors,
        SvgPaintResourceSelection? resources, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(inheritedTextStyle);
        ArgumentNullException.ThrowIfNull(namedColors);
        computedStyle = computedStyle.ResolveOpacity(inheritedPresentation.ElementOpacity).ResolveDisplay(inheritedPresentation.ElementDisplay);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(computedStyle, inheritedPresentation.Paint, inheritedPresentation.PaintServerFill, inheritedPresentation.IsDisplayed, inheritedPresentation.IsVisible, namedColors);
        SvgAffineTransform transform = inheritedPresentation.Transform.ComposeChecked(SvgGeometrySyntax.ParseElementTransform(reader, workCounter, cancellationToken));
        SvgInheritedTextStyle textStyle = SvgTextInheritance.Resolve(reader, inheritedTextStyle, SvgInlineStyleParser.MergeFont(inlineStyle.TextFont, inlineStyle.Important, styleSheet.TextFont, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.WhiteSpace, inlineStyle.Important, InlineImportantProperties.WhiteSpace, styleSheet.WhiteSpace, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.LetterSpacing, inlineStyle.Important, InlineImportantProperties.LetterSpacing, styleSheet.LetterSpacing, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.WordSpacing, inlineStyle.Important, InlineImportantProperties.WordSpacing, styleSheet.WordSpacing, styleSheet.Important));
        SvgInheritedStrokeStyle stroke = inheritedStroke.ResolveChild(computedStyle);
        SvgTextPaintStyle decorationPaint = new(presentation.Paint, true, true, stroke.Stroke, SvgPaintOpacity.Opaque)
        { StrokeStyle = stroke, PaintOrder = computedStyle.PaintOrder ?? inheritedPresentation.PaintOrder };
        SvgTextDecorationStyle? decoration = SvgTextDecorationSyntax.Resolve(reader, inlineStyle, styleSheet, inheritedTextStyle.Decoration, decorationPaint.GlyphPaint);
        if (decoration != textStyle.Decoration) textStyle = textStyle.WithDecoration(decoration);
        return new SvgInheritedContainer(
            new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, transform) { RecordingTransform = inheritedPresentation.RecordingTransform, TextMetrics = inheritedPresentation.TextMetrics.Enter(computedStyle.Filter, resources), OwnOpacity = computedStyle.GetOpacity(), ElementDisplay = computedStyle.GetDisplay(), PaintServerFill = presentation.PaintServerFill, Markers = SvgMarkerStyle.Resolve(computedStyle, inheritedPresentation.Markers), PaintOrder = computedStyle.PaintOrder ?? inheritedPresentation.PaintOrder },
            textStyle,
            computedStyle.FillRule ?? inheritedFillRule,
            presentation.Opacity,
            stroke);
    }
}

/// <summary>Carries one resolved group or link container state to its children.</summary>
internal readonly record struct SvgInheritedContainer(
    SvgInheritedPresentation Presentation,
    SvgInheritedTextStyle TextStyle,
    string? FillRule,
    byte Opacity,
    SvgInheritedStrokeStyle Stroke);
