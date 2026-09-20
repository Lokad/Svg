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
        var seen = new HashSet<TextRequestKey>(TextRequestKeyComparer.Instance);
        var inherited = new Stack<TextStyle>();
        var workCounter = new ParserWorkCounter(limits.MaximumWorkUnits);
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        try
        {
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            bool hasRoot = false;
            int nodeCount = 0;
            while (reader.Read())
            {
                workCounter.Consume(cancellationToken);
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
                    if (isSvgElement) style = ApplyAttributes(reader, style, workCounter, cancellationToken);
                    bool textElement = isSvgElement && reader.LocalName is "text" or "tspan";
                    if (textElement && style.Family is string family)
                    {
                        var key = new TextRequestKey(family, style.Weight, style.Italic);
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

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        string? family = SvgTextFontSyntax.GetProperty(reader, "font-family", workCounter, cancellationToken) ?? inherited.Family;
        if (family is not null) family = SvgTextFontSyntax.ParseFamily(family);
        string? weightValue = SvgTextFontSyntax.GetProperty(reader, "font-weight", workCounter, cancellationToken);
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = SvgTextFontSyntax.GetProperty(reader, "font-style", workCounter, cancellationToken);
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        return new TextStyle(family, weight, italic);
    }

    private readonly record struct TextStyle(string? Family, int Weight, bool Italic);

    private readonly record struct TextRequestKey(string Family, int Weight, bool Italic);

    private sealed class TextRequestKeyComparer : IEqualityComparer<TextRequestKey>
    {
        internal static TextRequestKeyComparer Instance { get; } = new();

        public bool Equals(TextRequestKey left, TextRequestKey right)
            => left.Weight == right.Weight && left.Italic == right.Italic && left.Family.Equals(right.Family, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(TextRequestKey value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Family), value.Weight, value.Italic);
    }
}
