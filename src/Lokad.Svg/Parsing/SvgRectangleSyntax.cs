using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded rectangle attributes after XML syntax validation and before style-driven lowering.</summary>
/// <summary>Represents geometry parsed from a policy-validated rectangle element.</summary>
internal readonly record struct SvgRectangleSyntax(double X, double Y, double Width, double Height, string? StrokeWidth)
{
    internal static SvgRectangleSyntax Parse(XmlReader reader)
    {
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        double width = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("width") ?? "0");
        double height = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("height") ?? "0");
        ValidateSquareCorners(reader.GetAttribute("rx"));
        ValidateSquareCorners(reader.GetAttribute("ry"));
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        return new SvgRectangleSyntax(x, y, width, height, reader.GetAttribute("stroke-width"));
    }

    private static void ValidateSquareCorners(string? value)
    {
        if (value is null || SvgGeometrySyntax.ParseRectangleCoordinate(value) == 0) return;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only zero rectangle corner radii are implemented.");
    }
}
