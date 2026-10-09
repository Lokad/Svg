using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents validated rectangle attributes with native float source bounds before style-driven lowering.</summary>
internal readonly record struct SvgRectangleSyntax(double X, double Y, double Width, double Height, string? StrokeWidth, double? CornerX, double? CornerY)
{
    internal static SvgRectangleSyntax Parse(XmlReader reader, SvgViewportLengthBases viewport)
    {
        double x = SvgGeometrySyntax.ParseViewportCoordinate(reader.GetAttribute("x") ?? "0", viewport.Width, SvgViewportCoordinateKind.Rectangle);
        double y = SvgGeometrySyntax.ParseViewportCoordinate(reader.GetAttribute("y") ?? "0", viewport.Height, SvgViewportCoordinateKind.Rectangle);
        double width = SvgGeometrySyntax.ParseViewportCoordinate(reader.GetAttribute("width") ?? "0", viewport.Width, SvgViewportCoordinateKind.Rectangle);
        double height = SvgGeometrySyntax.ParseViewportCoordinate(reader.GetAttribute("height") ?? "0", viewport.Height, SvgViewportCoordinateKind.Rectangle);
        double? cornerX = ParseCornerRadius(reader.GetAttribute("rx"), viewport.Width);
        double? cornerY = ParseCornerRadius(reader.GetAttribute("ry"), viewport.Height);
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle dimensions cannot be negative.");
        // SVG rectangle operands and XYWH endpoint additions use float precision.
        // Keep those endpoints before scene mapping; validation above still checks
        // the original values, including negative dimensions before narrowing.
        float nativeX = (float)x, nativeY = (float)y;
        float nativeRight = nativeX + (float)width, nativeBottom = nativeY + (float)height;
        return new SvgRectangleSyntax(nativeX, nativeY, (double)nativeRight - nativeX, (double)nativeBottom - nativeY, reader.GetAttribute("stroke-width"), cornerX, cornerY);
    }

    private static double? ParseCornerRadius(string? value, double? basis)
    {
        if (value is null) return null;
        double radius = SvgGeometrySyntax.ParseViewportCoordinate(value, basis, SvgViewportCoordinateKind.Rectangle);
        if (radius < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Rectangle corner radii cannot be negative.");
        return radius;
    }
}
