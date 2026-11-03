using System.Text;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Names case conversion independently of original SVG character addresses.</summary>
internal enum SvgTextTransform { None, Uppercase }

/// <summary>Shares pinned scalar conversion between font coverage and emitted text.</summary>
internal static partial class SvgTextCasing
{
    internal static SvgTextTransform Parse(ReadOnlySpan<char> value, SvgTextTransform inherited)
    {
        value = SvgCssWhitespace.Trim(value);
        if (value.IsEmpty || value.Equals("inherit", StringComparison.OrdinalIgnoreCase) || value.Equals("unset", StringComparison.OrdinalIgnoreCase)) return inherited;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("initial", StringComparison.OrdinalIgnoreCase)) return SvgTextTransform.None;
        if (value.Equals("uppercase", StringComparison.OrdinalIgnoreCase)) return SvgTextTransform.Uppercase;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "This text-transform operation is not implemented.");
    }

    internal static string ReadLanguage(XmlReader reader, string inherited)
        => reader.GetAttribute("lang", "http://www.w3.org/XML/1998/namespace") ?? reader.GetAttribute("lang") ?? inherited;

    /// <summary>Writes one default-language scalar mapping into caller storage; no allocation or source indexing occurs here.</summary>
    internal static int Write(Rune scalar, SvgTextTransform transform, string language, Span<char> destination)
    {
        if (transform == SvgTextTransform.None) return scalar.EncodeToUtf16(destination);
        if (transform != SvgTextTransform.Uppercase) throw new ArgumentOutOfRangeException(nameof(transform));
        // Known content languages require their own qualified tailoring. Never
        // silently apply an invariant mapping to a language-sensitive request.
        if (language.Length != 0 && !language.Equals("en", StringComparison.OrdinalIgnoreCase)
            && !language.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text case conversion for this content language is not implemented.");
        ReadOnlySpan<int> expansions = UppercaseExpansionRows;
        int expansion = Find(expansions, 3, scalar.Value);
        if (expansion >= 0)
        {
            ReadOnlySpan<char> value = UppercaseExpansionText.Slice(expansions[expansion + 1], expansions[expansion + 2]);
            value.CopyTo(destination);
            return value.Length;
        }
        ReadOnlySpan<int> pairs = UppercasePairs;
        int pair = Find(pairs, 2, scalar.Value);
        return (pair >= 0 ? new Rune(pairs[pair + 1]) : scalar).EncodeToUtf16(destination);

        static int Find(ReadOnlySpan<int> rows, int width, int scalar)
        {
            int low = 0, high = rows.Length / width - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2, offset = width * middle;
                if (rows[offset] == scalar) return offset;
                if (rows[offset] < scalar) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }
    }
}
