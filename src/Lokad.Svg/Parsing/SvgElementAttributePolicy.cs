using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Validates the deliberately narrow XML attribute subsets before element-specific lowering.</summary>
internal static class SvgElementAttributePolicy
{
    internal static void ValidateRoot(XmlReader reader) => Validate(reader, SvgElementKind.Root, "SVG root");

    internal static void ValidateGroup(XmlReader reader) => Validate(reader, SvgElementKind.Group, "Group");

    internal static void ValidateRectangle(XmlReader reader) => Validate(reader, SvgElementKind.Rectangle, "Rectangle");

    internal static void ValidateLine(XmlReader reader) => Validate(reader, SvgElementKind.Line, "Line");

    internal static void ValidateUse(XmlReader reader) => Validate(reader, SvgElementKind.Use, "Use");

    internal static void ValidatePath(XmlReader reader) => Validate(reader, SvgElementKind.Path, "Path");

    internal static void ValidatePointList(XmlReader reader, string elementName) => Validate(reader, SvgElementKind.PointList, elementName);

    internal static void ValidateDefinitionContainer(XmlReader reader)
    {
        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Definition container attributes are not implemented.");
    }

    internal static void ValidateSymbol(XmlReader reader) => Validate(reader, SvgElementKind.Symbol, "Symbol");

    private static void Validate(XmlReader reader, SvgElementKind kind, string elementName)
    {
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            if (IsAllowed(reader, kind)) continue;
            throw new SvgException(SvgErrorCode.UnsupportedFeature, elementName + " attribute '" + reader.Name + "' is not implemented.");
        }
        reader.MoveToElement();
    }

    private static bool IsAllowed(XmlReader reader, SvgElementKind kind)
    {
        if ((kind is SvgElementKind.Root or SvgElementKind.Group) && (reader.Prefix == "xmlns" || reader.Name == "xmlns")) return true;
        if ((kind is SvgElementKind.Group or SvgElementKind.Use) && reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href") return true;
        if (reader.NamespaceURI.Length != 0) return false;
        bool allowed = kind switch
        {
            SvgElementKind.Root => reader.LocalName is "id" or "width" or "height" or "viewBox" or "preserveAspectRatio" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style",
            SvgElementKind.Group => reader.LocalName is "id" or "class" or "href" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform",
            SvgElementKind.Rectangle => reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "rx" or "ry" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform",
            SvgElementKind.Line => reader.LocalName is "id" or "class" or "x1" or "y1" or "x2" or "y2" or "color" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform",
            SvgElementKind.Use => reader.LocalName is "id" or "class" or "href" or "x" or "y" or "transform" or "opacity" or "display" or "visibility" or "style",
            SvgElementKind.Path => reader.LocalName is "id" or "class" or "d" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform",
            SvgElementKind.PointList => reader.LocalName is "id" or "class" or "points" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform",
            SvgElementKind.Symbol => reader.LocalName == "id",
            _ => false
        };
        if (allowed && reader.LocalName is ("id" or "class") && reader.Value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG identifiers and class values exceed the configured syntax limit.");
        return allowed;
    }

    private enum SvgElementKind { Root, Group, Rectangle, Line, Use, Path, PointList, Symbol }
}
