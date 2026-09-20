namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;
using System.Xml;

/// <summary>Discovers the explicit single-family text requests admitted by the initial preparation subset.</summary>
internal static class SvgFontRequestDiscovery
{
    private const int MaximumRequests = 256;

    internal static SvgFontFaceRequest[] Discover(ReadOnlyMemory<byte> svg, CancellationToken cancellationToken)
    {
        var requests = new List<SvgFontFaceRequest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inherited = new Stack<TextStyle>();
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = svg.Length, IgnoreComments = true, IgnoreProcessingInstructions = true };
        try
        {
            using ReadOnlyMemoryStream stream = new(svg);
            using XmlReader reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                {
                    TextStyle style = inherited.Count == 0 ? new TextStyle(null, 400, false) : inherited.Peek();
                    style = ApplyAttributes(reader, style);
                    bool textElement = reader.LocalName is "text" or "tspan";
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
        }
        catch (XmlException exception)
        {
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG XML is malformed: " + exception.Message);
        }
        return requests.ToArray();
    }

    private static TextStyle ApplyAttributes(XmlReader reader, TextStyle inherited)
    {
        string? family = reader.GetAttribute("font-family") ?? inherited.Family;
        if (family is not null)
        {
            family = family.Trim();
            if (family.Length == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty text font-family values are not implemented.");
            if (family.Contains(',')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Font-family lists are not implemented by text preparation yet.");
            if (family.Length >= 2 && ((family[0] == '\'' && family[^1] == '\'') || (family[0] == '"' && family[^1] == '"'))) family = family[1..^1];
        }
        string? weightValue = reader.GetAttribute("font-weight");
        int weight = weightValue is null ? inherited.Weight : ParseWeight(weightValue);
        string? styleValue = reader.GetAttribute("font-style");
        bool italic = styleValue is null ? inherited.Italic : styleValue.Trim() switch
        {
            "normal" => false,
            "italic" => true,
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only normal and italic text font-style values are implemented by preparation.")
        };
        return new TextStyle(family, weight, italic);
    }

    private static int ParseWeight(string value)
    {
        string token = value.Trim();
        if (token == "normal") return 400;
        if (token == "bold") return 700;
        if (!int.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out int weight) || weight <= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only positive numeric, normal, and bold text font-weight values are implemented by preparation.");
        return weight;
    }

    private readonly record struct TextStyle(string? Family, int Weight, bool Italic);
}
