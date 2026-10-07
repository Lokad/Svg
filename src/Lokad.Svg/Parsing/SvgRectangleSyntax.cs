using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents validated rectangle attributes with native float source bounds before style-driven lowering.</summary>
internal readonly record struct SvgRectangleSyntax(double X, double Y, double Width, double Height, string? StrokeWidth, double? CornerX, double? CornerY)
{
    internal static SvgRectangleSyntax Parse(XmlReader reader)
    {
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        double width = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("width") ?? "0");
        double height = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("height") ?? "0");
        double? cornerX = ParseCornerRadius(reader.GetAttribute("rx"));
        double? cornerY = ParseCornerRadius(reader.GetAttribute("ry"));
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        // SVG rectangle operands and XYWH endpoint additions use float precision.
        // Keep those endpoints before scene mapping; validation above still checks
        // the original values, including negative dimensions before narrowing.
        float nativeX = (float)x, nativeY = (float)y;
        float nativeRight = nativeX + (float)width, nativeBottom = nativeY + (float)height;
        return new SvgRectangleSyntax(nativeX, nativeY, (double)nativeRight - nativeX, (double)nativeBottom - nativeY, reader.GetAttribute("stroke-width"), cornerX, cornerY);
    }

    private static double? ParseCornerRadius(string? value)
    {
        if (value is null) return null;
        double radius = SvgGeometrySyntax.ParseRectangleCoordinate(value);
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle corner radii cannot be negative.");
        return radius;
    }
}
