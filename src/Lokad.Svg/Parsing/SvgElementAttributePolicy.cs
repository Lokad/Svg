using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Validates the deliberately narrow XML attribute subsets before element-specific lowering.</summary>
internal static class SvgElementAttributePolicy
{
    internal static void ValidateRoot(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Root, "SVG root", workCounter, cancellationToken);

    internal static void ValidateGroup(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Group, "Group", workCounter, cancellationToken);

    /// <summary>Validates the switch container against the shared group attribute subset.</summary>
    internal static void ValidateSwitch(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Group, "Switch", workCounter, cancellationToken);

    internal static void ValidateRectangle(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Rectangle, "Rectangle", workCounter, cancellationToken);

    internal static void ValidateLine(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Line, "Line", workCounter, cancellationToken);

    internal static void ValidateUse(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Use, "Use", workCounter, cancellationToken);

    internal static void ValidatePath(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Path, "Path", workCounter, cancellationToken);

    internal static void ValidatePointList(XmlReader reader, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.PointList, elementName, workCounter, cancellationToken);
    internal static void ValidateCircle(XmlReader reader, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Circle, elementName, workCounter, cancellationToken);

    /// <summary>Validates the initial direct-text attribute subset before text extraction and layout.</summary>
    internal static void ValidateText(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Text, "Text", workCounter, cancellationToken);

    /// <summary>Validates the whitespace-and-letter-spacing nested span subset before direct-text extraction.</summary>
    internal static void ValidateTextSpan(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.TextSpan, "Text span", workCounter, cancellationToken);

    internal static void ValidateDefinitionContainer(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            workCounter.Consume(cancellationToken);
            if (reader.NamespaceURI.Length == 0 && reader.LocalName == "id")
            {
                if (reader.Value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG identifiers and class values exceed the configured syntax limit.");
                continue;
            }
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Definition container attributes are not implemented.");
        }
        reader.MoveToElement();
    }

    internal static void ValidateSymbol(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Symbol, "Symbol", workCounter, cancellationToken);

    internal static void ValidateNestedViewport(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.NestedViewport, "Nested viewport", workCounter, cancellationToken);

    internal static void ValidateClipPath(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.ClipPath, "Clip path", workCounter, cancellationToken);

    internal static void ValidateMask(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Mask, "Mask", workCounter, cancellationToken);

    internal static void ValidatePattern(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Pattern, "Pattern", workCounter, cancellationToken);

    internal static void ValidateLinearGradient(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.LinearGradient, "Linear gradient", workCounter, cancellationToken);
    internal static void ValidateRadialGradient(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.RadialGradient, "Radial gradient", workCounter, cancellationToken);

    internal static void ValidateGradientStop(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.GradientStop, "Gradient stop", workCounter, cancellationToken);

    internal static void ValidateImage(XmlReader reader, ParserWorkCounter workCounter, CancellationToken cancellationToken) => Validate(reader, SvgElementKind.Image, "Image", workCounter, cancellationToken);

    private static void Validate(XmlReader reader, SvgElementKind kind, string elementName, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (!reader.HasAttributes) return;
        while (reader.MoveToNextAttribute())
        {
            workCounter.Consume(cancellationToken);
            if (IsAllowed(reader, kind))
            {
                bool isInheritedStrokeStyle = reader.LocalName == "stroke-dasharray" || reader.LocalName == "stroke-dashoffset" || reader.LocalName == "stroke-linecap" || reader.LocalName == "stroke-linejoin";
                bool isInheritedContainer = isInheritedStrokeStyle && kind is SvgElementKind.Group or SvgElementKind.Root;
                if (reader.NamespaceURI.Length == 0) SvgStrokeStyleSyntax.ValidateAttribute(reader, reader.LocalName, reader.Value, kind is SvgElementKind.Path or SvgElementKind.PointList or SvgElementKind.Circle or SvgElementKind.Line || isInheritedContainer, kind == SvgElementKind.Line || isInheritedContainer, kind == SvgElementKind.Rectangle || isInheritedContainer, (reader.LocalName == "stroke-linejoin" && kind == SvgElementKind.Rectangle && HasNonzeroRoundedCorners(reader)) || isInheritedContainer, kind == SvgElementKind.Path || kind == SvgElementKind.Circle || (reader.LocalName == "stroke-linejoin" && kind == SvgElementKind.Rectangle && HasNonzeroRoundedCorners(reader)) || isInheritedContainer, workCounter, cancellationToken);
                if (reader.NamespaceURI.Length == 0 && reader.LocalName == "font-variant") SvgTextFontSyntax.ValidateFontVariant(reader.Value.AsSpan());
                if (reader.NamespaceURI.Length == 0 && reader.LocalName == "color-interpolation") SvgGradientSyntax.ValidateColorInterpolation(reader.Value.AsSpan());
                if (reader.NamespaceURI.Length == 0 && reader.LocalName == "overflow") SvgNestedViewportSyntax.ValidateOverflow(reader.Value.AsSpan());
                if (reader.NamespaceURI.Length == 0 && reader.LocalName == "preserveAspectRatio" && kind == SvgElementKind.Image) _ = SvgImageSyntax.ParsePlacementAspectRatio(reader.Value);;
                if (reader.NamespaceURI.Length == 0 && reader.LocalName == "clip-rule") SvgClipPathSyntax.ValidateClipRule(reader.Value.AsSpan());
                continue;
            }
            throw new SvgException(SvgErrorCode.UnsupportedFeature, elementName + " attribute '" + reader.Name + "' is not implemented.");
        }
        reader.MoveToElement();
    }

    private static bool IsAllowed(XmlReader reader, SvgElementKind kind)
    {
        if ((kind is SvgElementKind.Root or SvgElementKind.Group) && (reader.Prefix == "xmlns" || reader.Name == "xmlns")) return true;
        if (reader.NamespaceURI.Length == 0 && (reader.LocalName is "role" or "tabindex" or "focusable" or "pointer-events" or "cursor" || reader.LocalName.StartsWith("aria-", StringComparison.Ordinal) || reader.LocalName.StartsWith("data-", StringComparison.Ordinal))) return true;
        if ((kind is SvgElementKind.Group or SvgElementKind.Use or SvgElementKind.Image or SvgElementKind.LinearGradient or SvgElementKind.RadialGradient) && reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href") return true;
        if ((kind is SvgElementKind.Root or SvgElementKind.Group or SvgElementKind.Text or SvgElementKind.TextSpan) && reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace" && reader.LocalName == "space") return true;
        if (reader.NamespaceURI.Length != 0 && (reader.NamespaceURI == "http://www.w3.org/1999/xlink" || reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace")) return false;
        if (reader.NamespaceURI.Length != 0) return true;
        bool allowed = kind switch
        {
            SvgElementKind.Root => reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "viewBox" or "preserveAspectRatio" or "version" or "baseProfile" or "enable-background" or "overflow" or "font-family" or "font-weight" or "font-style" or "font-variant" or "font-size" or "letter-spacing" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-opacity" or "stroke-width" or "stroke-miterlimit" or "opacity" or "display" or "visibility" or "clip-rule" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation" or "style",
            SvgElementKind.Group => reader.LocalName is "id" or "class" or "href" or "target" or "rel" or "hreflang" or "download" or "type" or "title" or "font-family" or "font-weight" or "font-style" or "font-variant" or "font-size" or "letter-spacing" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-opacity" or "stroke-width" or "stroke-miterlimit" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.Rectangle => reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "rx" or "ry" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-opacity" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.Line => reader.LocalName is "id" or "class" or "x1" or "y1" or "x2" or "y2" or "color" or "stroke" or "stroke-opacity" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.Use => reader.LocalName is "id" or "class" or "href" or "x" or "y" or "width" or "height" or "transform" or "fill" or "stroke" or "stroke-width" or "opacity" or "display" or "visibility" or "style" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.Path => reader.LocalName is "id" or "class" or "d" or "vector-effect" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-opacity" or "stroke-width" or "stroke-miterlimit" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.PointList => reader.LocalName is "id" or "class" or "points" or "vector-effect" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-opacity" or "stroke-width" or "stroke-miterlimit" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.Circle => reader.LocalName is "id" or "class" or "cx" or "cy" or "r" or "rx" or "ry" or "vector-effect" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-opacity" or "stroke-width" or "stroke-miterlimit" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.Text => reader.LocalName is "id" or "class" or "x" or "y" or "dx" or "dy" or "text-anchor" or "font-family" or "font-weight" or "font-style" or "font-variant" or "font-size" or "letter-spacing" or "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "opacity" or "display" or "visibility" or "style" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.TextSpan => reader.LocalName is "id" or "class" or "dx" or "dy" or "letter-spacing" or "style",
            SvgElementKind.Symbol => reader.LocalName is "id" or "viewBox" or "preserveAspectRatio",
            SvgElementKind.ClipPath => reader.LocalName is "id" or "clipPathUnits" or "clip-rule",
            SvgElementKind.Mask => reader.LocalName is "id" or "mask-type" or "x" or "y" or "width" or "height" or "maskUnits" or "maskContentUnits",
            SvgElementKind.Pattern => reader.LocalName is "id" or "x" or "y" or "width" or "height" or "patternUnits" or "patternContentUnits" or "patternTransform",
            SvgElementKind.LinearGradient => reader.LocalName is "id" or "href" or "x1" or "y1" or "x2" or "y2" or "gradientUnits" or "spreadMethod" or "gradientTransform",
            SvgElementKind.RadialGradient => reader.LocalName is "id" or "href" or "cx" or "cy" or "r" or "fx" or "fy" or "fr" or "gradientUnits" or "spreadMethod" or "gradientTransform",
            SvgElementKind.GradientStop => reader.LocalName is "offset" or "stop-color" or "stop-opacity",
            SvgElementKind.Image => reader.LocalName is "id" or "class" or "x" or "y" or "width" or "height" or "href" or "preserveAspectRatio" or "opacity" or "display" or "visibility" or "style" or "transform" or "clip-path" or "clip-rule" or "mask" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "pathLength" or "color-interpolation",
            SvgElementKind.NestedViewport => reader.LocalName is "x" or "y" or "width" or "height" or "viewBox" or "preserveAspectRatio" or "overflow" or "opacity",
            _ => false
        };
        if (allowed && reader.LocalName is ("id" or "class") && reader.Value.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG identifiers and class values exceed the configured syntax limit.");
        if (allowed && IsScalarValueAttribute(reader.LocalName) && reader.Value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG scalar attribute values exceed the configured syntax limit.");
        return allowed;
    }

    /// <summary>Reports whether a rectangle element carries nonzero rounded corners, gating round joins to the outline lowering path.</summary>
    internal static bool HasNonzeroRoundedCorners(XmlReader reader)
    {
        return IsNonzeroCornerRadius(reader.GetAttribute("rx")) || IsNonzeroCornerRadius(reader.GetAttribute("ry"));
    }

    private static bool IsNonzeroCornerRadius(string? value)
    {
        return value is not null && SvgGeometrySyntax.ParseRectangleCoordinate(value) != 0;
    }

    private static bool IsScalarValueAttribute(string name) => name is "color" or "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "stroke-miterlimit" or "stroke-opacity" or "stroke-linecap" or "stroke-linejoin" or "stroke-dasharray" or "stroke-dashoffset" or "vector-effect" or "clip-rule" or "opacity" or "display" or "visibility" or "version" or "baseProfile" or "focusable" or "target" or "rel" or "hreflang" or "download" or "type" or "clip-path" or "clipPathUnits" or "mask" or "mask-type" or "patternUnits" or "patternContentUnits" or "x1" or "y1" or "x2" or "y2" or "offset" or "stop-color" or "stop-opacity" or "gradientUnits" or "spreadMethod" or "cx" or "cy" or "r" or "fx" or "fy" or "fr" or "x" or "y" or "dx" or "dy" or "x1" or "y1" or "x2" or "y2" or "width" or "height" or "rx" or "ry" or "viewBox" or "preserveAspectRatio" or "overflow" or "text-anchor" or "font-family" or "font-weight" or "font-style" or "font-size" or "letter-spacing" or "space";

    private enum SvgElementKind { Root, Group, Rectangle, Line, Use, Path, PointList, Circle, Text, TextSpan, Symbol, NestedViewport, ClipPath, Mask, Image, Pattern, LinearGradient, RadialGradient, GradientStop }
}
