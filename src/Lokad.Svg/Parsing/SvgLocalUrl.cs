namespace Lokad.Svg.Parsing;

using System.Xml;

/// <summary>Parses one bounded local url reference shared by paint, clip, mask and pattern servers.</summary>
internal static class SvgLocalUrl
{
    /// <summary>Reads an effect reference, retaining the initial none value as absence before scopes or URL binding.</summary>
    internal static string? ReadReference(XmlReader reader, string attribute) => GetReference(reader.GetAttribute(attribute));

    /// <summary>Normalizes a borrowed live or retained effect value without allocating or resolving its identifier.</summary>
    internal static string? GetReference(string? value)
        => value is null || SvgCssWhitespace.Trim(value.AsSpan()).Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;

    /// <summary>Extracts the referenced identifier from url spellings with optional quotes and surrounding whitespace.</summary>
    internal static bool TryParseId(string value, out string id)
    {
        id = "";
        ReadOnlySpan<char> token = SvgCssWhitespace.Trim(value.AsSpan());
        if (token.Length < 7 || !token.StartsWith("url(".AsSpan(), StringComparison.OrdinalIgnoreCase) || !token.EndsWith(")".AsSpan(), StringComparison.Ordinal)) return false;
        ReadOnlySpan<char> inner = SvgCssWhitespace.Trim(token[4..^1]);
        if (inner.Length >= 2 && (inner[0] == (char)34 || inner[0] == (char)39) && inner[^1] == inner[0]) inner = SvgCssWhitespace.Trim(inner[1..^1]);
        if (inner.Length < 2 || inner[0] != (char)35) return false;
        ReadOnlySpan<char> reference = SvgCssWhitespace.Trim(inner[1..]);
        if (reference.IsEmpty || reference.IndexOfAny((char)34, (char)39) >= 0 || reference.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) return false;
        id = reference.ToString();
        return true;
    }
}
