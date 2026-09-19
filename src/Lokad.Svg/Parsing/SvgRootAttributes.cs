using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgPaint Paint, string? Id, byte Opacity, bool IsDisplayed, bool IsVisible, double? Width, double? Height, SvgViewBox? ViewBox)
{
    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgElementAttributePolicy.ValidateRoot(reader);
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, workCounter, cancellationToken);
        SvgResolvedContainerPresentation presentation = SvgContainerPresentationResolver.Resolve(style, SvgPaint.Default, true, true, namedColors);
        return new SvgRootAttributes(presentation.Paint, reader.GetAttribute("id"), presentation.Opacity, presentation.IsDisplayed, presentation.IsVisible, SvgGeometrySyntax.ParseLength(reader.GetAttribute("width")), SvgGeometrySyntax.ParseLength(reader.GetAttribute("height")), SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio")));
    }
}
