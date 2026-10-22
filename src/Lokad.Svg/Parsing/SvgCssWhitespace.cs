namespace Lokad.Svg.Parsing;

/// <summary>Shares CSS grammar whitespace without altering Unicode identifier or string contents.</summary>
internal static class SvgCssWhitespace
{
    internal const string Characters = " \t\r\n\f";
    internal static bool Is(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f';
    internal static ReadOnlySpan<char> Trim(ReadOnlySpan<char> value) => value.Trim(Characters);
    internal static string Trim(string value)
    {
        ReadOnlySpan<char> token = Trim(value.AsSpan());
        return token.Length == value.Length ? value : token.ToString();
    }
}
