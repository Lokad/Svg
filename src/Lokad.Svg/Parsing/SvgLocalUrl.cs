namespace Lokad.Svg.Parsing;

using System.Xml;
using System.Text;
using System.Buffers;

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
        if (inner.Length >= 2 && (inner[0] == (char)34 || inner[0] == (char)39) && inner[^1] == inner[0]) inner = inner[1..^1].TrimEnd(SvgCssWhitespace.Characters);
        if (inner.Length < 2 || inner[0] != (char)35) return false;
        ReadOnlySpan<char> reference = inner[1..];
        if (reference.IsEmpty || reference.IndexOfAny((char)34, (char)39) >= 0 || reference.IndexOfAny(SvgCssWhitespace.Characters) >= 0 || reference.Length > SvgSyntaxLimits.MaximumIdentifierCharacters) return false;
        if (reference.IndexOf('%') < 0) { id = reference.ToString(); return true; }
        Span<char> decoded = stackalloc char[SvgSyntaxLimits.MaximumIdentifierCharacters];
        if (!TryDecodeFragment(reference, decoded, out int written)) return false;
        id = decoded[..written].ToString();
        return true;
    }

    /// <summary>Decodes one bounded local fragment exactly once, preserving literal Unicode and decoded percent signs.</summary>
    /// <remarks>Temporary storage is fixed-size stack memory, with no retained owner or external lookup.
    /// The caller meters its bounded reference visit and owns the returned parser string.</remarks>
    internal static string ParseFragment(ReadOnlySpan<char> fragment)
    {
        if (fragment.Length > SvgSyntaxLimits.MaximumIdentifierCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Local fragment identifiers exceed the syntax limit.");
        if (fragment.IndexOf('%') < 0) return fragment.ToString();
        Span<char> decoded = stackalloc char[SvgSyntaxLimits.MaximumIdentifierCharacters];
        if (!TryDecodeFragment(fragment, decoded, out int written))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Local fragments require valid UTF-8 escapes and non-whitespace identifiers.");
        return decoded[..written].ToString();
    }

    /// <summary>Reserves the decoded string before allocation and transfers its lifetime to the caller's store.</summary>
    internal static string CopyFragment(ReadOnlySpan<char> fragment, BudgetedParserStringStore strings)
    {
        if (fragment.Length > SvgSyntaxLimits.MaximumIdentifierCharacters)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Local fragment identifiers exceed the syntax limit.");
        if (fragment.IndexOf('%') < 0) return strings.Copy(fragment);
        Span<char> decoded = stackalloc char[SvgSyntaxLimits.MaximumIdentifierCharacters];
        if (!TryDecodeFragment(fragment, decoded, out int written))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Local fragments require valid UTF-8 escapes and non-whitespace identifiers.");
        return strings.Copy(decoded[..written]);
    }

    private static bool TryDecodeFragment(ReadOnlySpan<char> fragment, Span<char> decoded, out int written)
    {
        written = 0;
        Span<byte> bytes = stackalloc byte[SvgSyntaxLimits.MaximumIdentifierCharacters];
        int index = 0;
        while (index < fragment.Length)
        {
            if (fragment[index] != '%') { decoded[written++] = fragment[index++]; continue; }
            int count = 0;
            do
            {
                if (index + 2 >= fragment.Length) return false;
                int first = Hex(fragment[index + 1]), second = Hex(fragment[index + 2]);
                if (first < 0 || second < 0) return false;
                bytes[count++] = (byte)(first * 16 + second);
                index += 3;
            } while (index < fragment.Length && fragment[index] == '%');
            int offset = 0;
            while (offset < count)
            {
                if (Rune.DecodeFromUtf8(bytes.Slice(offset, count - offset), out Rune scalar, out int consumed) != OperationStatus.Done
                    || scalar.Value == 0) return false;
                written += scalar.EncodeToUtf16(decoded[written..]);
                offset += consumed;
            }
        }
        return decoded[..written].IndexOfAny(SvgCssWhitespace.Characters) < 0;

        static int Hex(char value) => value is >= '0' and <= '9' ? value - '0'
            : value is >= 'a' and <= 'f' ? value - 'a' + 10 : value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;
    }
}
