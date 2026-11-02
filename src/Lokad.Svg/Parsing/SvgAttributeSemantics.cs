using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Separates syntactic attribute applicability from implemented rendering features.</summary>
/// <remarks>Literal SVG1.1/SVG2 regular and geometry names are scoped to their defining elements; presentation names
/// remain conservatively meaningful. Unknown names and static UI metadata supply no rendering values.
/// This storage-free query never reads or retains values, mutates the reader or resolves resources.
/// Callers retain XML/structural limits and check cancellation per attribute; independent readers may query concurrently.</remarks>
internal static class SvgAttributeSemantics
{
    internal static bool IsInert(string element, XmlReader reader)
    {
        if (reader.NamespaceURI.Length != 0) return false;
        string name = reader.LocalName;
        return name is "role" or "tabindex" or "focusable" or "autofocus" or "pointer-events" or "cursor"
            || name.StartsWith("aria-", StringComparison.Ordinal) || name.StartsWith("data-", StringComparison.Ordinal)
            || !IsDefinedFor(element, name);
    }

    // Applicability only: an applicable attribute can still require an unsupported feature.
    // Retain legacy known-feature guards; do not treat a removed standard name as a typo.
    private static bool IsDefinedFor(string element, string attribute) => attribute switch
    {
        "baseProfile" or "contentScriptType" or "contentStyleType" or "playbackorder" or "timelinebegin"
            or "version" or "zoomAndPan" =>
            element is "svg",
        "clipPathUnits" => element is "clipPath",
        "crossorigin" => element is "image",
        "cx" or "cy" => element is "circle" or "ellipse" or "radialGradient",
        "d" => element is "path",
        "download" or "hreflang" or "ping" or "referrerpolicy" or "rel" or "target" => element is "a",
        "dx" or "dy" => element is "feDropShadow" or "feOffset" or "text" or "tspan",
        "edgeMode" => element is "feGaussianBlur",
        "externalResourcesRequired" =>
            element is "a" or "circle" or "clipPath" or "defs" or "ellipse" or "filter" or "g" or "image"
                or "line" or "linearGradient" or "marker" or "mask" or "path" or "pattern" or "polygon"
                or "polyline" or "radialGradient" or "rect" or "svg" or "switch" or "symbol" or "text"
                or "textPath" or "tspan" or "use",
        "filterRes" or "filterUnits" or "primitiveUnits" => element is "filter",
        "fr" or "fx" or "fy" => element is "radialGradient",
        "gradientTransform" or "gradientUnits" or "spreadMethod" => element is "linearGradient" or "radialGradient",
        "height" or "width" =>
            element is "feBlend" or "feComposite" or "feDropShadow" or "feFlood" or "feGaussianBlur"
                or "feMerge" or "feOffset" or "filter" or "image" or "mask" or "pattern" or "rect" or "svg"
                or "symbol" or "use",
        "href" => element is "a" or "image" or "linearGradient" or "pattern" or "radialGradient" or "textPath" or "use",
        "in" => element is "feBlend" or "feComposite" or "feDropShadow" or "feGaussianBlur" or "feMergeNode" or "feOffset",
        "in2" => element is "feBlend" or "feComposite",
        "k1" or "k2" or "k3" or "k4" or "operator" => element is "feComposite",
        "lengthAdjust" or "textLength" => element is "text" or "textPath" or "tspan",
        "markerHeight" or "markerUnits" or "markerWidth" or "orient" => element is "marker",
        "maskContentUnits" or "maskUnits" => element is "mask",
        "media" or "title" => element is "style",
        "method" or "path" or "side" or "spacing" or "startOffset" => element is "textPath",
        "mode" => element is "feBlend",
        "offset" => element is "stop",
        "pathLength" => element is "circle" or "ellipse" or "line" or "path" or "polygon" or "polyline" or "rect",
        "patternContentUnits" or "patternTransform" or "patternUnits" => element is "pattern",
        "points" => element is "polygon" or "polyline",
        "preserveAspectRatio" => element is "image" or "marker" or "pattern" or "svg" or "symbol",
        "r" => element is "circle" or "radialGradient",
        "refX" or "refY" => element is "marker" or "symbol",
        "requiredExtensions" or "requiredFeatures" or "systemLanguage" =>
            element is "a" or "circle" or "clipPath" or "defs" or "ellipse" or "g" or "image" or "line"
                or "mask" or "path" or "pattern" or "polygon" or "polyline" or "rect" or "svg" or "switch"
                or "text" or "textPath" or "tspan" or "use",
        "result" =>
            element is "feBlend" or "feComposite" or "feDropShadow" or "feFlood" or "feGaussianBlur"
                or "feMerge" or "feOffset",
        "role" =>
            element is "a" or "circle" or "ellipse" or "g" or "image" or "line" or "path" or "polygon"
                or "polyline" or "rect" or "svg" or "switch" or "symbol" or "text" or "textPath" or "tspan"
                or "use",
        "rotate" => element is "text" or "tspan",
        "rx" or "ry" => element is "ellipse" or "rect",
        "stdDeviation" => element is "feDropShadow" or "feGaussianBlur",
        "type" => element is "a" or "style",
        "viewBox" => element is "marker" or "pattern" or "svg" or "symbol",
        "x" or "y" =>
            element is "feBlend" or "feComposite" or "feDropShadow" or "feFlood" or "feGaussianBlur"
                or "feMerge" or "feOffset" or "filter" or "image" or "mask" or "pattern" or "rect" or "svg"
                or "symbol" or "text" or "tspan" or "use",
        "x1" or "x2" or "y1" or "y2" => element is "line" or "linearGradient",
        "alignment-baseline" or "autofocus" or "baseline-shift" or "class" or "clip" or "clip-path"
            or "clip-rule" or "color" or "color-interpolation" or "color-interpolation-filters"
            or "color-profile" or "color-rendering" or "cursor" or "direction" or "display"
            or "dominant-baseline" or "enable-background" or "fill" or "fill-opacity" or "fill-rule" or "filter"
            or "flood-color" or "flood-opacity" or "font-family" or "font-size" or "font-size-adjust"
            or "font-stretch" or "font-style" or "font-variant" or "font-weight"
            or "glyph-orientation-horizontal" or "glyph-orientation-vertical" or "id" or "image-rendering"
            or "kerning" or "lang" or "letter-spacing" or "lighting-color" or "marker" or "marker-end"
            or "marker-mid" or "marker-start" or "mask" or "mask-type" or "opacity" or "overflow"
            or "paint-order" or "path-length" or "pointer-events" or "shape-rendering" or "stop-color"
            or "stop-opacity" or "stroke" or "stroke-dasharray" or "stroke-dashoffset" or "stroke-linecap"
            or "stroke-linejoin" or "stroke-miterlimit" or "stroke-opacity" or "stroke-width" or "style"
            or "tabindex" or "text-anchor" or "text-decoration" or "text-rendering" or "transform"
            or "transform-origin" or "unicode-bidi" or "vector-effect" or "visibility" or "word-spacing"
            or "writing-mode" => true,
        _ => false
    };
}
