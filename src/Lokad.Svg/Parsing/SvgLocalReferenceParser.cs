using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded local-use syntax independently from definition routing and resolution.</summary>
internal static class SvgLocalReferenceParser
{
    internal static SvgUseReference ParseUse(XmlReader reader, SvgPaintableElementContext context)
    {
        SvgComputedStyle style = context.ResolveStyle(reader);
        if (style.Color is not null || style.Fill is not null || style.FillOpacity is not null || style.Stroke is not null || style.FillRule is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Paint styles on use elements are not implemented.");
        SvgElementAttributePolicy.ValidateUse(reader);
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A use element requires an href.");
        if (href.Length < 2 || href[0] != '#') throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local rectangle use references are implemented.");
        if (href.Length - 1 > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Local reference identifiers exceed the configured syntax limit.");
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        return new SvgUseReference(href[1..], x, y, SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")), SvgColorParser.ParseOpacity(style.Opacity), SvgShapePresentationResolver.ParseDisplay(style.Display) && SvgShapePresentationResolver.ParseVisibility(style.Visibility, true));
    }
}
