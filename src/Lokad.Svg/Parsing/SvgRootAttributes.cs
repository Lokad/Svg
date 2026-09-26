using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgInheritedPresentation InheritedPresentation, SvgInheritedTextStyle InheritedTextStyle, string? Id, byte Opacity, double? Width, double? Height, SvgViewBox? ViewBox)
{

    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateRoot(reader, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);
        ValidateXmlSpace(reader.GetAttribute("space", "http://www.w3.org/XML/1998/namespace"));
        SvgRootSyntax syntax = SvgRootSyntax.Parse(reader);
        return new SvgRootAttributes(new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, AxisTransform.Identity), SvgTextInheritance.Resolve(reader, SvgInheritedTextStyle.Default, inline.TextFont, inline.WhiteSpace, inline.LetterSpacing), reader.GetAttribute("id"), presentation.Opacity, syntax.Width, syntax.Height, syntax.ViewBox);
        static void ValidateXmlSpace(string? value)
        {
            // Admitted but not yet wired into whitespace inheritance, matching text
            // elements which pass xml:space through validation today. Only the two
            // spec-defined values are accepted so anything else fails fast.
            if (value is null || value is "default" or "preserve") return;
            throw new SvgException(SvgErrorCode.MalformedInput, "Root xml:space values must be default or preserve.");
        }

    }
}
