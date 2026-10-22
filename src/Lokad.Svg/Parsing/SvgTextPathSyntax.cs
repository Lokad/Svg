using System.Xml;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Parsing;

/// <summary>Names one local path and its finite distance or percentage offset.</summary>
internal readonly record struct SvgTextPathReference(string PathId, double StartOffset, bool IsPercentage, SvgTextAnchor? Anchor);

/// <summary>Validates the first text-path subset before either font discovery or layout.</summary>
internal static class SvgTextPathSyntax
{
    internal static SvgTextPathReference Parse(XmlReader reader, ParserWorkCounter work, CancellationToken token)
    {
        while (reader.MoveToNextAttribute())
        {
            work.Consume(token);
            if (reader.Name == "xmlns" || reader.Prefix == "xmlns") continue;
            if (reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href") continue;
            if (reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace" && reader.LocalName == "space") continue;
            if (reader.NamespaceURI.Length == 0 && reader.LocalName is "href" or "startOffset" or "method" or "spacing" or "id" or "text-anchor")
            {
                if (reader.Value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "Text-path attributes exceed the configured syntax limit.");
                continue;
            }
            reader.MoveToElement();
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "This text-path attribute is not implemented.");
        }
        reader.MoveToElement();
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A text path requires a local path reference.");
        work.ConsumeText(href.Length, token);
        if (href.Length > SvgSyntaxLimits.MaximumIdentifierCharacters + 1) throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path reference exceeds the configured identifier limit.");
        if (!href.StartsWith('#')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local text-path references are implemented.");
        if (href.Length == 1 || href.AsSpan(1).IndexOfAny(" \t\r\n#".AsSpan()) >= 0) throw new SvgException(SvgErrorCode.MalformedInput, "The text-path reference requires one nonempty identifier.");
        if (reader.GetAttribute("method") is string method && SvgCssWhitespace.Trim(method) != "align") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only aligned text paths are implemented.");
        if (reader.GetAttribute("spacing") is string spacing && SvgCssWhitespace.Trim(spacing) != "exact") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only exact text-path spacing is implemented.");
        string offset = reader.GetAttribute("startOffset") ?? "0";
        work.ConsumeText(offset.Length, token);
        ReadOnlySpan<char> value = offset.AsSpan().Trim(SvgCssWhitespace.Characters);
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path offset exceeds the configured syntax limit.");
        bool percentage = value.EndsWith("%", StringComparison.Ordinal);
        if (percentage && value.Length > 1 && SvgCssWhitespace.Is(value[^2])) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text-path percentage units must follow their number without a gap.");
        double distance = SvgGeometrySyntax.ParseTextCoordinate(percentage ? value[..^1].ToString() : offset);
        string? anchor = reader.GetAttribute("text-anchor");
        if (anchor is not null) work.ConsumeText(anchor.Length, token);
        SvgTextAnchor? resolvedAnchor = anchor is null || SvgCssWhitespace.Trim(anchor).Equals("inherit", StringComparison.OrdinalIgnoreCase)
            ? null : SvgTextSyntax.ParseAnchor(anchor, SvgTextAnchor.Start);
        return new(href[1..], distance, percentage, resolvedAnchor);
    }
}
