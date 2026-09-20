using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgInheritedPresentation InheritedPresentation, string? Id, byte Opacity, double? Width, double? Height, SvgViewBox? ViewBox)
{
    internal SvgInheritedTextStyle InheritedTextStyle { get; init; }

    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateRoot(reader, workCounter, cancellationToken);
        InlineStyle inline = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgComputedStyle style = SvgComputedStyle.Resolve(reader, inline);
        SvgResolvedPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);
        SvgRootSyntax syntax = SvgRootSyntax.Parse(reader);
        return new SvgRootAttributes(new SvgInheritedPresentation(presentation.Paint, presentation.IsDisplayed, presentation.IsVisible, AxisTransform.Identity), reader.GetAttribute("id"), presentation.Opacity, syntax.Width, syntax.Height, syntax.ViewBox) { InheritedTextStyle = SvgTextInheritance.Resolve(reader, SvgInheritedTextStyle.Default, inline.TextFont) };
    }
}
