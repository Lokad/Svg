using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents geometry parsed from policy-validated circle and ellipse elements.</summary>
internal readonly record struct SvgEllipseSyntax(double CenterX, double CenterY, double RadiusX, double RadiusY)
{
    internal static SvgEllipseSyntax ParseCircle(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double centerX = ParseCoordinate(reader.GetAttribute("cx") ?? "0", viewport.Width);
        double centerY = ParseCoordinate(reader.GetAttribute("cy") ?? "0", viewport.Height);
        double radius = ParseRadius(reader.GetAttribute("r") ?? "0", viewport.NormalizedDiagonal, "Circle radius cannot be negative.");
        return new SvgEllipseSyntax(centerX, centerY, radius, radius);
    }

    internal static SvgEllipseSyntax ParseEllipse(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double centerX = ParseCoordinate(reader.GetAttribute("cx") ?? "0", viewport.Width);
        double centerY = ParseCoordinate(reader.GetAttribute("cy") ?? "0", viewport.Height);
        double? radiusX = reader.GetAttribute("rx") is {} x ? ParseRadius(x, viewport.Width, "Ellipse radii cannot be negative.") : null;
        double? radiusY = reader.GetAttribute("ry") is {} y ? ParseRadius(y, viewport.Height, "Ellipse radii cannot be negative.") : null;
        // A missing axis copies the other used radius, after its own percentage basis resolves.
        return new SvgEllipseSyntax(centerX, centerY, radiusX ?? radiusY ?? 0, radiusY ?? radiusX ?? 0);
    }

    private static double ParseRadius(string value, double? basis, string negativeMessage)
    {
        double radius = ParseCoordinate(value, basis);
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, negativeMessage);
        return radius;
    }
    private static double ParseCoordinate(string value, double? basis)
        => SvgGeometrySyntax.ParseViewportCoordinate(value, basis, SvgViewportCoordinateKind.Oval);
}
