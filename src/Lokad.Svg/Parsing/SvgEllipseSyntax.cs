using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents geometry parsed from policy-validated circle and ellipse elements.</summary>
internal readonly record struct SvgEllipseSyntax(double CenterX, double CenterY, double RadiusX, double RadiusY)
{
    internal static SvgEllipseSyntax ParseCircle(XmlReader reader)
    {
        double centerX = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("cx") ?? "0");
        double centerY = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("cy") ?? "0");
        double radius = ParseRadius(reader.GetAttribute("r"), "Circle radius is required.", "Circle radius cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radius, radius);
    }

    internal static SvgEllipseSyntax ParseEllipse(XmlReader reader)
    {
        double centerX = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("cx") ?? "0");
        double centerY = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("cy") ?? "0");
        double radiusX = ParseRadius(reader.GetAttribute("rx"), "Ellipse horizontal radius is required.", "Ellipse radii cannot be negative.");
        double radiusY = ParseRadius(reader.GetAttribute("ry"), "Ellipse vertical radius is required.", "Ellipse radii cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radiusX, radiusY);
    }

    private static double ParseRadius(string? value, string missingMessage, string negativeMessage)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, missingMessage);
        double radius = SvgGeometrySyntax.ParseRectangleCoordinate(value);
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, negativeMessage);
        return radius;
    }
}