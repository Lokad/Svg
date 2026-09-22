namespace Lokad.Svg.Parsing;

/// <summary>Parses one bounded local url reference shared by paint, clip, mask and pattern servers.</summary>
internal static class SvgLocalUrl
{
    /// <summary>Extracts the referenced identifier from url spellings with optional quotes and surrounding whitespace.</summary>
    internal static bool TryParseId(string value, out string id)
    {
        id = "";
        ReadOnlySpan<char> token = value.AsSpan().Trim();
        if (token.Length < 7 || !token.StartsWith("url(".AsSpan(), StringComparison.Ordinal) || !token.EndsWith(")".AsSpan(), StringComparison.Ordinal)) return false;
        ReadOnlySpan<char> inner = token[4..^1].Trim();
        if (inner.Length >= 2 && (inner[0] == (char)34 || inner[0] == (char)39) && inner[^1] == inner[0]) inner = inner[1..^1].Trim();
        if (inner.Length < 2 || inner[0] != (char)35) return false;
        ReadOnlySpan<char> reference = inner[1..].Trim();
        if (reference.IsEmpty || reference.IndexOfAny((char)34, (char)39) >= 0 || reference.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) return false;
        id = reference.ToString();
        return true;
    }
}
