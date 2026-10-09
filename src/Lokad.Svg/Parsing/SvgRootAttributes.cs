using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, SvgInheritedStrokeStyle InheritedStroke, string? Id, string? Class, byte Opacity, SvgRootLength? Width, SvgRootLength? Height, SvgViewBox? ViewBox, string? FillRule)
{
    internal SvgPaintOpacity SourceOpacity { get; init; }
    internal string? ClipRule { get; init; }

    /// <summary>Supplies the explicit containing viewport for percentage root dimensions during lowering.</summary>
    internal SvgViewport? ContainingViewport { get; init; }

    /// <summary>Percentage lengths use the root viewBox extent when present, independently of output pixel dimensions.</summary>
    internal double? UserViewportWidth => ViewBox?.Width ?? Width?.Resolve(ContainingViewport?.Width, 300) ?? ContainingViewport?.Width;

    internal double? UserViewportHeight => ViewBox?.Height ?? Height?.Resolve(ContainingViewport?.Height, 150) ?? ContainingViewport?.Height;

    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateRoot(reader, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, null, true, true, namedColors);
        SvgTextWhitespaceMode rootWhitespace = ValidateXmlSpace(reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace"));
        SvgRootSyntax syntax = SvgRootSyntax.Parse(reader, workCounter, cancellationToken);
        return new SvgRootAttributes(new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, SvgAffineTransform.Identity) { PaintServerFill = presentation.PaintServerFill }, SvgTextInheritance.Resolve(reader, SvgInheritedTextStyle.Default.WithWhitespaceMode(rootWhitespace), inline.TextFont, inline.WhiteSpace, inline.LetterSpacing), new SvgInheritedStrokeStyle(style.Linecap, style.Linejoin, style.Dasharray, style.Dashoffset, style.Width, style.Miterlimit, style.StrokeOpacity, style.Stroke), reader.GetAttribute("id"), reader.GetAttribute("class"), presentation.Opacity, ParseRootStyleLength(inline.GeometryWidth) ?? syntax.Width, ParseRootStyleLength(inline.GeometryHeight) ?? syntax.Height, syntax.ViewBox, style.FillRule) { SourceOpacity = SvgColorParser.ParsePaintOpacity(style.Opacity), ClipRule = style.ClipRule };
        static SvgRootLength? ParseRootStyleLength(string? value)
        {
            if (value is null) return null;
            ReadOnlySpan<char> token = value.AsSpan().Trim();
            if (token.Equals("auto".AsSpan(), StringComparison.OrdinalIgnoreCase) || token.Equals("inherit".AsSpan(), StringComparison.OrdinalIgnoreCase)) return null;
            try { return SvgGeometrySyntax.ParseRootLength(value); }
            catch (SvgException) { return null; }
        }

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

