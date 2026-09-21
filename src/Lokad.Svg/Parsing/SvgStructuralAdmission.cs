using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Shares structural admission gates between font-request discovery and document lowering.</summary>
internal static class SvgStructuralAdmission
{
    /// <summary>Rejects elements that violate the configured nesting or attribute limits.</summary>
    internal static void ValidateElementStructure(XmlReader reader, SvgRenderLimits limits)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        if (reader.Depth >= limits.MaximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
        if (reader.AttributeCount > limits.MaximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
    }

    /// <summary>Rejects a document whose root is not an SVG element in the SVG namespace.</summary>
    internal static void ThrowIfNotSvgRoot(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.LocalName != "svg" || reader.NamespaceURI != "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
    }
}
