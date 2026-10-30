namespace Lokad.Svg.Parsing;

/// <summary>Separates a paint URL from its optional color or none fallback without changing effect-reference grammar.</summary>
/// <remarks>Spans borrow the input only for the current bounded parse. No resources are opened or
/// retained; decoded identifiers use the shared local-fragment policy.</remarks>
internal readonly ref struct SvgPaintReference(ReadOnlySpan<char> url, ReadOnlySpan<char> fallback)
{
    internal ReadOnlySpan<char> Url { get; } = url;
    internal ReadOnlySpan<char> Fallback { get; } = fallback;

    internal static bool IsReference(string? value)
        => value is not null && SvgCssWhitespace.Trim(value.AsSpan()).StartsWith("url(", StringComparison.OrdinalIgnoreCase);

    internal static bool TryParseId(string value, out string id)
    {
        id = "";
        return TryRead(value.AsSpan(), out SvgPaintReference reference) && SvgLocalUrl.TryParseId(reference.Url, out id);
    }

    internal static bool TryRead(ReadOnlySpan<char> value, out SvgPaintReference reference)
    {
        reference = default;
        ReadOnlySpan<char> token = SvgCssWhitespace.Trim(value);
        if (!token.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return false;
        int position = 4;
        while (position < token.Length && SvgCssWhitespace.Is(token[position])) position++;
        if (position == token.Length) return false;
        if (token[position] is '\'' or '"')
        {
            char quote = token[position++];
            int length = token[position..].IndexOf(quote);
            if (length < 0) return false;
            position += length + 1;
            while (position < token.Length && SvgCssWhitespace.Is(token[position])) position++;
            if (position == token.Length || token[position] != ')') return false;
        }
        else
        {
            int length = token[position..].IndexOf(')');
            if (length < 0 || token.Slice(position, length).IndexOf('(') >= 0) return false;
            position += length;
        }
        position++;
        reference = new(token[..position], SvgCssWhitespace.Trim(token[position..]));
        return true;
    }
}
