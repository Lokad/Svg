using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Represents the bounded line attributes after XML syntax validation and before style-driven lowering.</summary>
internal readonly record struct SvgLineSyntax(int X1, int Y1, int X2, int Y2, string? StrokeWidth)
{
    internal static SvgLineSyntax Parse(XmlReader reader)
    {
        SvgElementAttributePolicy.ValidateLine(reader);
        int x1 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("x1") ?? "0");
        int y1 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("y1") ?? "0");
        int x2 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("x2") ?? "0");
        int y2 = SvgGeometrySyntax.ParseInteger(reader.GetAttribute("y2") ?? "0");
        return new SvgLineSyntax(x1, y1, x2, y2, reader.GetAttribute("stroke-width"));
    }
}
