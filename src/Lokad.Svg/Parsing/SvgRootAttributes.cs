using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded supported root attributes before child traversal starts.</summary>
internal readonly record struct SvgRootAttributes(SvgPaint Paint, string? Id, byte Opacity, bool IsDisplayed, bool IsVisible, double? Width, double? Height, SvgViewBox? ViewBox)
{
    internal static SvgRootAttributes Parse(XmlReader reader, IReadOnlyDictionary<string, SvgColor> namedColors, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, workCounter, cancellationToken);
        SvgElementAttributePolicy.ValidateRoot(reader);
        if (style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Fill rules are only implemented on shapes.");
        SvgShapePresentationResolver.ValidateStroke(style.Stroke);
        SvgPaint paint = SvgPaintResolver.Resolve(style.Fill, style.FillOpacity, style.Color, SvgPaint.Default, namedColors);
        return new SvgRootAttributes(paint, reader.GetAttribute("id"), SvgColorParser.ParseOpacity(style.Opacity), SvgShapePresentationResolver.ParseDisplay(style.Display), SvgShapePresentationResolver.ParseVisibility(style.Visibility, true), SvgGeometrySyntax.ParseLength(reader.GetAttribute("width")), SvgGeometrySyntax.ParseLength(reader.GetAttribute("height")), SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio")));
    }
}
