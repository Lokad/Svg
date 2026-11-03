using System.Xml;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Parsing;

/// <summary>Names one local path and its finite distance or percentage offset.</summary>
internal readonly record struct SvgTextPathReference(string PathId, double StartOffset, bool IsPercentage, SvgTextAnchor? Anchor);

/// <summary>Shares text-path reference validation and child-style resolution between discovery and layout.</summary>
internal static class SvgTextPathSyntax
{
    /// <summary>Identifies reference attributes independently of the shared text-child presentation.</summary>
    internal static bool IsReferenceAttribute(XmlReader reader)
        => reader.NamespaceURI.Length == 0 && reader.LocalName is "href" or "startOffset" or "method" or "spacing"
            || reader.NamespaceURI == "http://www.w3.org/1999/xlink" && reader.LocalName == "href";

    /// <summary>Resolves the source child style once and carries its computed anchor into placement; caller owns inherited state.</summary>
    internal static SvgTextPathReference Read(XmlReader reader, SvgTextSpanStyle inherited, InlineStyle styleSheet,
        CancellationToken token, out SvgTextSpanStyle style)
    {
        SvgTextPathReference reference = Parse(reader, token);
        if (!SvgTextSyntax.TryParseChildStyle(reader, inherited, styleSheet, token, out style))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "This text-path child style is not implemented.");
        return reference with { Anchor = style.Font.Anchor };
    }

    /// <summary>Reads geometry reference syntax without resolving presentation; the child-style reader supplies its anchor.</summary>
    internal static SvgTextPathReference Parse(XmlReader reader, CancellationToken token)
    {
        SvgElementAttributePolicy.ValidateTextPath(reader, token);
        string href = reader.GetAttribute("href") ?? reader.GetAttribute("href", "http://www.w3.org/1999/xlink") ?? throw new SvgException(SvgErrorCode.MalformedInput, "A text path requires a local path reference.");
        token.ThrowIfCancellationRequested();
        if (href.Length > SvgSyntaxLimits.MaximumIdentifierCharacters + 1) throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path reference exceeds the configured identifier limit.");
        if (!href.StartsWith('#')) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local text-path references are implemented.");
        if (href.Length == 1 || href.AsSpan(1).IndexOfAny(" \t\r\n#".AsSpan()) >= 0) throw new SvgException(SvgErrorCode.MalformedInput, "The text-path reference requires one nonempty identifier.");
        if (ReadBounded(reader, "method") is string method && SvgCssWhitespace.Trim(method) != "align") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only aligned text paths are implemented.");
        if (ReadBounded(reader, "spacing") is string spacing && SvgCssWhitespace.Trim(spacing) != "exact") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only exact text-path spacing is implemented.");
        string offset = ReadBounded(reader, "startOffset") ?? "0";
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<char> value = offset.AsSpan().Trim(SvgCssWhitespace.Characters);
        if (value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters) throw new SvgException(SvgErrorCode.LimitExceeded, "The text-path offset exceeds the configured syntax limit.");
        bool percentage = value.EndsWith("%", StringComparison.Ordinal);
        if (percentage && value.Length > 1 && SvgCssWhitespace.Is(value[^2])) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text-path percentage units must follow their number without a gap.");
        double distance = SvgGeometrySyntax.ParseTextCoordinate(percentage ? value[..^1].ToString() : offset);
        return new(SvgLocalUrl.ParseFragment(href.AsSpan(1)), distance, percentage, null);

        static string? ReadBounded(XmlReader reader, string name)
        {
            string? value = reader.GetAttribute(name);
            if (value is not null && value.Length > SvgSyntaxLimits.MaximumValueTokenCharacters)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Text-path attributes exceed the configured syntax limit.");
            return value;
        }
    }
}
