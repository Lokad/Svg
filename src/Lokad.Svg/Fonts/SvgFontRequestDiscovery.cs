namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using System.Xml;

/// <summary>Discovers the explicit single-family text requests admitted by the initial preparation subset.</summary>
internal static class SvgFontRequestDiscovery
{
    private const int MaximumRequests = 256;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, CancellationToken cancellationToken)
        => Discover(svg, SvgRenderLimits.Default, cancellationToken);

    /// <summary>Discovers admitted text requests under the same caller-selected structural and work limits as rendering.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return Discover(svg, limits, new ParserWorkCounter(limits.MaximumWorkUnits), cancellationToken);
    }

    /// <summary>Discovers admitted text requests using one caller-owned preparation work counter.</summary>
    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, SvgRenderLimits limits, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (svg.Length > limits.MaximumInputBytes) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG input exceeds the configured input limit.");
        var requests = new List<SvgFontFaceRequest>();
        var seen = new HashSet<TextRequestKey>(TextRequestKeyComparer.Instance);
        var inherited = new Stack<TextStyle>();
        var directText = new Stack<TextRequestCandidate?>();
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = limits.MaximumInputBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        try
        {
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            bool hasRoot = false;
            int nodeCount = 0;
            int definitionsDepth = -1;
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
                    bool insideDirectText = directText.Any(ancestorCandidate => ancestorCandidate is not null);
                    foreach (TextRequestCandidate? ancestorCandidate in directText) if (ancestorCandidate is not null) ancestorCandidate.HasChildElement = true;
                    TextStyle style = inherited.Count == 0 ? new TextStyle(null, 400, false, true, true, AxisTransform.Identity, null) : inherited.Peek();
                    bool isSvgElement = reader.NamespaceURI == SvgNamespace;
                    if (isSvgElement && !insideDirectText) style = ApplyAttributes(reader, style, workCounter, cancellationToken);
                    if (isSvgElement && reader.LocalName == "defs" && !reader.IsEmptyElement) definitionsDepth = reader.Depth;
                    if (definitionsDepth < 0 && !insideDirectText && isSvgElement && reader.LocalName == "text" && reader.GetAttribute("transform") is null) SvgElementAttributePolicy.ValidateText(reader, workCounter, cancellationToken);
                    if (definitionsDepth < 0 && !insideDirectText && isSvgElement && reader.LocalName == "text" && style.FillRule is not null && !style.FillRule.Equals("nonzero", StringComparison.OrdinalIgnoreCase)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only the default nonzero fill rule is implemented for direct text.");
                    TextRequestCandidate? candidate = definitionsDepth < 0 && !insideDirectText && isSvgElement && reader.LocalName == "text" && reader.GetAttribute("transform") is null && style.Transform == AxisTransform.Identity && style.IsDisplayed && style.IsVisible && style.Family is string family
                        ? new TextRequestCandidate(new SvgFontFaceRequest(family, style.Weight, style.Italic))
                        : null;
                    if (reader.IsEmptyElement) AddCandidate(candidate, requests, seen);
                    else
                    {
                        inherited.Push(style);
                        directText.Push(candidate);
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Depth == definitionsDepth) definitionsDepth = -1;
                    if (inherited.Count != 0)
                    {
                        inherited.Pop();
                        AddCandidate(directText.Pop(), requests, seen);
                    }
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

    private static void AddCandidate(TextRequestCandidate? candidate, List<SvgFontFaceRequest> requests, HashSet<TextRequestKey> seen)
    {
        if (candidate is null || candidate.HasChildElement) return;
        SvgFontFaceRequest request = candidate.Request;
        if (!seen.Add(new TextRequestKey(request.Family, request.Weight, request.Italic))) return;
        if (requests.Count == MaximumRequests) throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG text font request count exceeds the preparation limit.");
        requests.Add(request);
    }

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        InlineStyle inlineStyle = SvgInlineStyleParser.Parse(reader.GetAttribute("style"), workCounter, cancellationToken, true);
        SvgTextInlineFontProperties inline = inlineStyle.TextFont;
        SvgComputedStyle computedStyle = SvgComputedStyle.Resolve(reader, inlineStyle);
        string? family = inline.Family ?? reader.GetAttribute("font-family") ?? inherited.Family;
        if (family is not null) family = SvgTextFontSyntax.ParseFamily(family);
        string? weightValue = inline.Weight ?? reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : SvgTextFontSyntax.ParseWeight(weightValue);
        string? styleValue = inline.Style ?? reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : SvgTextFontSyntax.ParseItalic(styleValue);
        bool isDisplayed = inherited.IsDisplayed && SvgPresentationResolver.ParseDisplay(computedStyle.Display);
        bool isVisible = SvgPresentationResolver.ParseVisibility(computedStyle.Visibility, inherited.IsVisible);
        AxisTransform transform = inherited.Transform.Compose(SvgGeometrySyntax.ParseTransform(reader.GetAttribute("transform")));
        return new TextStyle(family, weight, italic, isDisplayed, isVisible, transform, computedStyle.FillRule);
    }

    private readonly record struct TextStyle(string? Family, int Weight, bool Italic, bool IsDisplayed, bool IsVisible, AxisTransform Transform, string? FillRule);

    private readonly record struct TextRequestKey(string Family, int Weight, bool Italic);

    private sealed class TextRequestCandidate(SvgFontFaceRequest request)
    {
        internal SvgFontFaceRequest Request { get; } = request;

        internal bool HasChildElement { get; set; }
    }

    private sealed class TextRequestKeyComparer : IEqualityComparer<TextRequestKey>
    {
        internal static TextRequestKeyComparer Instance { get; } = new();

        public bool Equals(TextRequestKey left, TextRequestKey right)
            => left.Weight == right.Weight && left.Italic == right.Italic && left.Family.Equals(right.Family, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(TextRequestKey value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Family), value.Weight, value.Italic);
    }
}
