namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using System.Xml;

/// <summary>Discovers the explicit single-family text requests admitted by the initial preparation subset.</summary>
internal static class SvgFontRequestDiscovery
{
    private const int MaximumRequests = 256;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, CancellationToken cancellationToken)
    {
        SvgRenderLimits limits = SvgRenderLimits.Default;
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        var requests = new List<SvgFontFaceRequest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inherited = new Stack<TextStyle>();
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        try
        {
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            bool hasRoot = false;
            int nodeCount = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (!hasRoot)
                    {
                        if (reader.LocalName != "svg" || reader.NamespaceURI != SvgNamespace) throw new SvgException(SvgErrorCode.MalformedInput, "The document root must be an SVG element in the SVG namespace.");
                        hasRoot = true;
                    }
                    if (reader.Depth >= limits.MaximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
                    if (reader.AttributeCount > limits.MaximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
                    if (nodeCount >= limits.MaximumNodes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                    nodeCount++;
                    TextStyle style = inherited.Count == 0 ? new TextStyle(null, 400, false) : inherited.Peek();
                    bool isSvgElement = reader.NamespaceURI == SvgNamespace;
                    if (isSvgElement) style = ApplyAttributes(reader, style);
                    bool textElement = isSvgElement && reader.LocalName is "text" or "tspan";
                    if (textElement && style.Family is string family)
                    {
                        string key = family + "\n" + style.Weight + "\n" + style.Italic;
                        if (seen.Add(key))
                        {
                            if (requests.Count == MaximumRequests) throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG text font request count exceeds the preparation limit.");
                            requests.Add(new SvgFontFaceRequest(family, style.Weight, style.Italic));
                        }
                    }
                    if (!reader.IsEmptyElement) inherited.Push(style);
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (inherited.Count != 0) inherited.Pop();
                }
            }
            if (!hasRoot) throw new SvgException(SvgErrorCode.MalformedInput, "SVG input has no root element.");
        }
        catch (XmlException exception)
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message);
        }
        return requests.ToArray();
    }

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited)
    {
        string? family = GetProperty(reader, "font-family") ?? inherited.Family;
        if (family is not null) family = SvgTextFontSyntax.ParseFamily(family);
        string? weightValue = GetProperty(reader, "font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = GetProperty(reader, "font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        return new TextStyle(family, weight, italic);
    }

    private static string? GetProperty(XmlReader reader, string name)
    {
        string? inline = reader.GetAttribute("style");
        string? value = null;
        if (inline is not null)
        {
            foreach (string declaration in inline.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = declaration.IndexOf(':');
                if (separator < 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Malformed inline text style declarations are not implemented by preparation.");
                if (declaration[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) value = declaration[(separator + 1)..].Trim();
            }
        }
        return value ?? reader.GetAttribute(name);
    }

    private readonly record struct TextStyle(string? Family, int Weight, bool Italic);
}
