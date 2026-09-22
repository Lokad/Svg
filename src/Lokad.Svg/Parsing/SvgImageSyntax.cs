using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded embedded-image reference syntax before PNG decoding.</summary>
internal readonly record struct SvgImageSyntax(string Href, double X, double Y, double? Width, double? Height)
{
    internal static SvgImageSyntax Parse(XmlReader reader)
    {
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "An image element requires an href.");
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        double? width = ParseDimension(reader.GetAttribute("width"));
        double? height = ParseDimension(reader.GetAttribute("height"));
        return new SvgImageSyntax(href, x, y, width, height);
    }

    private static double? ParseDimension(string? value)
    {
        if (value is null) return null;
        double parsed = SvgGeometrySyntax.ParseRectangleCoordinate(value);
        if (parsed < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Image dimensions cannot be negative.");
        return parsed;
    }
}