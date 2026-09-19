using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Parses the bounded root viewport syntax independently of root presentation resolution.</summary>
internal readonly record struct SvgRootSyntax(double? Width, double? Height, SvgViewBox? ViewBox)
{
    internal static SvgRootSyntax Parse(XmlReader reader)
        => new(SvgGeometrySyntax.ParseLength(reader.GetAttribute("width")), SvgGeometrySyntax.ParseLength(reader.GetAttribute("height")), SvgGeometrySyntax.ParseViewBox(reader.GetAttribute("viewBox"), reader.GetAttribute("preserveAspectRatio")));
}
