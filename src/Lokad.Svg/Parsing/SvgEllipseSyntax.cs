using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents geometry parsed from policy-validated circle and ellipse elements.</summary>
internal readonly record struct SvgEllipseSyntax(double CenterX, double CenterY, double RadiusX, double RadiusY)
{
    internal static SvgEllipseSyntax ParseCircle(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double centerX = ParseCoordinate(reader.GetAttribute("cx") ?? "0", viewport.Width);
        double centerY = ParseCoordinate(reader.GetAttribute("cy") ?? "0", viewport.Height);
        double radius = ParseRadius(reader.GetAttribute("r"), viewport.NormalizedDiagonal, "Circle radius is required.", "Circle radius cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radius, radius);
    }

    internal static SvgEllipseSyntax ParseEllipse(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double centerX = ParseCoordinate(reader.GetAttribute("cx") ?? "0", viewport.Width);
        double centerY = ParseCoordinate(reader.GetAttribute("cy") ?? "0", viewport.Height);
        double radiusX = ParseRadius(reader.GetAttribute("rx"), viewport.Width, "Ellipse horizontal radius is required.", "Ellipse radii cannot be negative.");
        double radiusY = ParseRadius(reader.GetAttribute("ry"), viewport.Height, "Ellipse vertical radius is required.", "Ellipse radii cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radiusX, radiusY);
    }

    private static double ParseRadius(string? value, double? basis, string missingMessage, string negativeMessage)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, missingMessage);
        double radius = ParseCoordinate(value, basis);
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, negativeMessage);
        return radius;
    }
    private static double ParseCoordinate(string value, double? basis)
        => SvgGeometrySyntax.ParseViewportCoordinate(value, basis, SvgViewportCoordinateKind.Oval);
}
