using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded nested viewport attributes before clip-box construction.</summary>
internal readonly record struct SvgNestedViewportSyntax(double X, double Y, double Width, double Height)
{
    internal static SvgNestedViewportSyntax Parse(XmlReader reader)
    {
        double x = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("x") ?? "0");
        double y = SvgGeometrySyntax.ParseRectangleCoordinate(reader.GetAttribute("y") ?? "0");
        string? widthValue = reader.GetAttribute("width");
        string? heightValue = reader.GetAttribute("height");
        if (widthValue is null || heightValue is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Nested viewport dimensions default to 100 percent, which is not implemented.");
        double width = SvgGeometrySyntax.ParseRectangleCoordinate(widthValue);
        double height = SvgGeometrySyntax.ParseRectangleCoordinate(heightValue);
        if (width < 0 || height < 0) throw new SvgException(SvgErrorCode.MalformedInput, "Nested viewport dimensions cannot be negative.");
        return new SvgNestedViewportSyntax(x, y, width, height);
    }
}