using System.Xml;

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
        bool inheritedHasOpacityScope,
        IReadOnlyDictionary<string, SvgColor> namedColors)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(inheritedTextStyle);
        ArgumentNullException.ThrowIfNull(namedColors);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(computedStyle, inheritedPresentation.Paint, inheritedPresentation.IsDisplayed, inheritedPresentation.IsVisible, namedColors);
        if (presentation.Opacity != 255 && inheritedHasOpacityScope) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested group opacity requires separate compositing calibration.");
        AxisTransform transform = inheritedPresentation.Transform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));
        SvgInheritedTextStyle textStyle = SvgTextInheritance.Resolve(reader, inheritedTextStyle, SvgInlineStyleParser.MergeFont(inlineStyle.TextFont, inlineStyle.Important, styleSheet.TextFont, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.WhiteSpace, inlineStyle.Important, InlineImportantProperties.WhiteSpace, styleSheet.WhiteSpace, styleSheet.Important), SvgInlineStyleParser.MergeWithImportance(inlineStyle.LetterSpacing, inlineStyle.Important, InlineImportantProperties.LetterSpacing, styleSheet.LetterSpacing, styleSheet.Important));
        SvgInheritedStrokeStyle stroke = inheritedStroke.ResolveChild(computedStyle);
        return new SvgInheritedContainer(
            new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, transform),
            textStyle,
            computedStyle.FillRule ?? inheritedFillRule,
            inheritedHasOpacityScope || presentation.Opacity != 255,
            presentation.Opacity,
            stroke);
    }
}

/// <summary>Carries one resolved group or link container state to its children.</summary>
internal readonly record struct SvgInheritedContainer(
    SvgInheritedPresentation Presentation,
    SvgInheritedTextStyle TextStyle,
    string? FillRule,
    bool HasOpacityScope,
    byte Opacity,
    SvgInheritedStrokeStyle Stroke);
