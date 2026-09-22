using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded embedded-image reference syntax before PNG decoding.</summary>
internal readonly record struct SvgImageSyntax(string Href, double X, double Y, double? Width, double? Height)
{
    internal static SvgImageSyntax Parse(XmlReader reader, double? baseWidth, double? baseHeight)
    {
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "An image element requires an href.");
        double x = ParsePosition(reader.GetAttribute("x"), baseWidth);
        double y = ParsePosition(reader.GetAttribute("y"), baseHeight);
        double? width = ParseDimension(reader.GetAttribute("width"), baseWidth);
        double? height = ParseDimension(reader.GetAttribute("height"), baseHeight);
        return new SvgImageSyntax(href, x, y, width, height);
    }

    private static double? ParseDimension(string? value, double? baseValue)
    {
        if (value is null) return null;
        double parsed = SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
        if (parsed < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Image dimensions cannot be negative.");
        return parsed;
    }

    private static double ParsePosition(string? value, double? baseValue)
    {
        if (value is null) return 0;
        return SvgGeometrySyntax.ParseViewportDimension(value, baseValue);
    }
}