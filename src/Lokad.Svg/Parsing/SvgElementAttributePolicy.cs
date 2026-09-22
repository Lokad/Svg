using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Validates the deliberately narrow XML attribute subsets before element-specific lowering.</summary>
internal static class SvgElementAttributePolicy
{
    internal static void ValidateRoot(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Root, "SVG root", workCounter, cancellationToken);

    internal static void ValidateGroup(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Group, "Group", workCounter, cancellationToken);

    internal static void ValidateRectangle(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Rectangle, "Rectangle", workCounter, cancellationToken);

    internal static void ValidateLine(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Line, "Line", workCounter, cancellationToken);

    internal static void ValidateUse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Use, "Use", workCounter, cancellationToken);

    internal static void ValidatePath(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Path, "Path", workCounter, cancellationToken);

    internal static void ValidatePointList(XmlReader reader, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.PointList, elementName, workCounter, cancellationToken);

    /// <summary>Validates the initial direct-text attribute subset before text extraction and layout.</summary>
    internal static void ValidateText(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Text, "Text", workCounter, cancellationToken);

    /// <summary>Validates the whitespace-and-letter-spacing nested span subset before direct-text extraction.</summary>
    internal static void ValidateTextSpan(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.TextSpan, "Text span", workCounter, cancellationToken);

    internal static void ValidateDefinitionContainer(XmlReader reader)
    {
        if (reader.HasAttributes) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Definition container attributes are not implemented.");
    }

    internal static void ValidateSymbol(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Symbol, "Symbol", workCounter, cancellationToken);

    internal static void ValidateNestedViewport(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.NestedViewport, "Nested viewport", workCounter, cancellationToken);

    internal static void ValidateClipPath(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.ClipPath, "Clip path", workCounter, cancellationToken);

    internal static void ValidateMask(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Mask, "Mask", workCounter, cancellationToken);

    internal static void ValidatePattern(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Pattern, "Pattern", workCounter, cancellationToken);

    internal static void ValidateLinearGradient(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.LinearGradient, "Linear gradient", workCounter, cancellationToken);

    internal static void ValidateGradientStop(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.GradientStop, "Gradient stop", workCounter, cancellationToken);

    internal static void ValidateImage(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Image, "Image", workCounter, cancellationToken);

    private static void Validate(XmlReader reader, SvgElementKind kind, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            workCounter.Consume(cancellationToken);
            if (IsAllowed(reader, kind)) continue;
            throw new SvgException(SvgErrorCode.UnsupportedFeature, elementName + " attribute '" + reader.Name + "' is not implemented.");
        }
        reader.MoveToElement();
    }

    private static bool IsAllowed(XmlReader reader, SvgElementKind kind)
    {
        if ((kind is SvgElementKind.Root or SvgElementKind.Group) && (reader.Prefix == "xmlns" || reader.Name == "xmlns")) return true;
        if ((kind is SvgElementKind.Group or SvgElementKind.Use or SvgElementKind.Image or SvgElementKind.LinearGradient) && reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href") return true;
        if ((kind is SvgElementKind.Text or SvgElementKind.TextSpan) && reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace" && reader.LocalName == "space") return true;
        if (reader.NamespaceURI.Length != 0) return false;
        bool allowed = kind switch
        {
            SvgElementKind.Root => reader.LocalName is "id" or "width" or "height" or "viewBox" or "preserveAspectRatio" or "font-family" or "font-weight" or "font-style" or "font-size" or "letter-spacing" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style",
            SvgElementKind.Group => reader.LocalName is "id" or "class" or "href" or "font-family" or "font-weight" or "font-style" or "font-size" or "letter-spacing" or "color" or "fill" or "fill-opacity" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path",
            SvgElementKind.Rectangle => reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "rx" or "ry" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "mask",
            SvgElementKind.Line => reader.LocalName is "id" or "class" or "x1" or "y1" or "x2" or "y2" or "color" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "mask",
            SvgElementKind.Use => reader.LocalName is "id" or "class" or "href" or "x" or "y" or "width" or "height" or "transform" or "fill" or "opacity" or "display" or "visibility" or "style" or "clip-path" or "mask",
            SvgElementKind.Path => reader.LocalName is "id" or "class" or "d" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "mask",
            SvgElementKind.PointList => reader.LocalName is "id" or "class" or "points" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "mask",
            SvgElementKind.Text => reader.LocalName is "id" or "class" or "x" or "y" or "dx" or "dy" or "text-anchor" or "font-family" or "font-weight" or "font-style" or "font-size" or "letter-spacing" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "clip-path" or "mask",
            SvgElementKind.TextSpan => reader.LocalName is "id" or "class" or "dx" or "dy" or "letter-spacing" or "style",
            SvgElementKind.Symbol => reader.LocalName is "id" or "viewBox" or "preserveAspectRatio",
            SvgElementKind.ClipPath => reader.LocalName is "id" or "clipPathUnits",
            SvgElementKind.Mask => reader.LocalName is "id" or "mask-type",
            SvgElementKind.Pattern => reader.LocalName is "id" or "x" or "y" or "width" or "height" or "patternUnits" or "patternContentUnits",
            SvgElementKind.LinearGradient => reader.LocalName is "id" or "href" or "x1" or "y1" or "x2" or "y2" or "gradientUnits" or "spreadMethod" or "gradientTransform",
            SvgElementKind.GradientStop => reader.LocalName is "offset" or "stop-color" or "stop-opacity",
            SvgElementKind.Image => reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "href" or "opacity" or "display" or "visibility" or "style" or "clip-path" or "mask",
            SvgElementKind.NestedViewport => reader.LocalName is "x" or "y" or "width" or "height",
            _ => false
        };
        if (allowed && reader.LocalName is ("id" or "class") && reader.Value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG identifiers and class values exceed the configured syntax limit.");
        if (allowed && IsScalarValueAttribute(reader.LocalName) && reader.Value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG scalar attribute values exceed the configured syntax limit.");
        return allowed;
    }

    private static bool IsScalarValueAttribute(string name) => name is "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "clip-path" or "clipPathUnits" or "mask" or "mask-type" or "patternUnits" or "patternContentUnits" or "x1" or "y1" or "x2" or "y2" or "offset" or "stop-color" or "stop-opacity" or "gradientUnits" or "spreadMethod" or "x" or "y" or "dx" or "dy" or "x1" or "y1" or "x2" or "y2" or "width" or "height" or "rx" or "ry" or "viewBox" or "preserveAspectRatio" or "text-anchor" or "font-family" or "font-weight" or "font-style" or "font-size" or "letter-spacing" or "space";

    private enum SvgElementKind { Root, Group, Rectangle, Line, Use, Path, PointList, Text, TextSpan, Symbol, NestedViewport, ClipPath, Mask, Image, Pattern, LinearGradient, GradientStop }
}
