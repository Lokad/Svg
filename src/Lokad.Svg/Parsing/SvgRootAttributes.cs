using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, SvgInheritedStrokeStyle InheritedStroke, SvgRootLength? Width, SvgRootLength? Height, SvgViewBox? ViewBox, string? FillRule)
{
    internal SvgPaintOpacity SourceOpacity { get; init; }
    internal string? ClipRule { get; init; }

    /// <summary>Supplies the explicit containing viewport for percentage root dimensions during lowering.</summary>
    internal SvgViewport? ContainingViewport { get; init; }

    /// <summary>Percentage lengths use the viewBox extent or resolved logical viewport, independently of output pixel dimensions.</summary>
    internal double? UserViewportWidth => ViewBox?.Width ?? SvgDocumentSizing.ResolveViewportSide(Width, ContainingViewport?.Width, 300);

    internal double? UserViewportHeight => ViewBox?.Height ?? SvgDocumentSizing.ResolveViewportSide(Height, ContainingViewport?.Height, 150);

    /// <summary>Applies the collected root stylesheet before inherited presentation and intrinsic dimensions are resolved.</summary>
    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, BudgetedParserList<CssRule> rules, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateRoot(reader, workCounter, cancellationToken);
        InlineStyle sheet = SvgRootStyleSheetParser.ResolveRoot(reader.GetAttribute("id"), reader.GetAttribute("class"), rules, workCounter, cancellationToken);
        return ParseValues(reader, namedColors, sheet, workCounter, cancellationToken);
    }

    private static SvgRootAttributes ParseValues(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, InlineStyle sheet, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline, sheet).ResolveOpacity(SvgPaintOpacity.Opaque).ResolveDisplay(SvgDisplay.Inline);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, null, true, true, namedColors);
        SvgTextWhitespaceMode rootWhitespace = ValidateXmlSpace(reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace"));
        SvgRootSyntax syntax = SvgRootSyntax.ParseComputed(reader, inline, sheet, workCounter, cancellationToken);
        SvgInheritedTextStyle text = SvgTextInheritance.Resolve(reader, SvgInheritedTextStyle.Default.WithWhitespaceMode(rootWhitespace),
            SvgInlineStyleParser.MergeFont(inline.TextFont, inline.Important, sheet.TextFont, sheet.Important),
            SvgInlineStyleParser.MergeWithImportance(inline.WhiteSpace, inline.Important, InlineImportantProperties.WhiteSpace, sheet.WhiteSpace, sheet.Important),
            SvgInlineStyleParser.MergeWithImportance(inline.LetterSpacing, inline.Important, InlineImportantProperties.LetterSpacing, sheet.LetterSpacing, sheet.Important),
            SvgInlineStyleParser.MergeWithImportance(inline.WordSpacing, inline.Important, InlineImportantProperties.WordSpacing, sheet.WordSpacing, sheet.Important));
        SvgTextPaintStyle decorationPaint = new(presentation.Paint, true, true, style.Stroke, 255)
        { StrokeStyle = SvgInheritedStrokeStyle.Default.ResolveChild(style), PaintOrder = style.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers };
        SvgTextDecorationStyle? decoration = SvgTextDecorationSyntax.Resolve(reader, inline, sheet, null, decorationPaint.GlyphPaint);
        if (decoration is not null) text = text.WithDecoration(decoration);
        return new SvgRootAttributes(new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, SvgAffineTransform.Identity) { OwnOpacity = style.GetOpacity(), ElementDisplay = style.GetDisplay(), PaintServerFill = presentation.PaintServerFill, Markers = SvgMarkerStyle.Resolve(style, default), PaintOrder = style.PaintOrder ?? SvgPaintOrder.FillStrokeMarkers }, text, new SvgInheritedStrokeStyle(style.Linecap, style.Linejoin, style.Dasharray, style.Dashoffset, style.Width, style.Miterlimit, style.StrokeOpacity, style.Stroke), syntax.Width, syntax.Height, syntax.ViewBox, style.FillRule) { SourceOpacity = style.GetOpacity(), ClipRule = style.ClipRule };
        static SvgTextWhitespaceMode ValidateXmlSpace(string? value)
        {
            // Only the two spec-defined values are accepted so anything else fails fast;
            // the mode seeds whitespace inheritance for text descendants.
            if (value is null || value is "default") return SvgTextWhitespaceMode.Default;
            if (value is "preserve") return SvgTextWhitespaceMode.Preserve;
            throw new SvgException(SvgErrorCode.MalformedInput, "Root xml:space values must be default or preserve.");
        }

    }
}

