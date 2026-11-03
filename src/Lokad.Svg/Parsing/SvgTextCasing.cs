using System.Text;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Names case conversion independently of original SVG character addresses.</summary>
internal enum SvgTextTransform { None, Uppercase, Lowercase }

/// <summary>Classifies original scalars for the Unicode case-context rules.</summary>
[Flags]
internal enum SvgTextCasingProperties { None = 0, Cased = 1, Ignorable = 2 }

/// <summary>Retains source context across chunks and styles within one logical text element; parser-thread confined.</summary>
internal struct SvgTextCasingContext
{
    internal bool PrecedingCased { get; private set; }

    internal void Observe(SvgTextCasingProperties properties)
    {
        // Ignorable takes precedence where the two properties overlap. Unicode
        // context matching consumes the ignorable sequence without backtracking.
        if ((properties & SvgTextCasingProperties.Ignorable) == 0)
            PrecedingCased = (properties & SvgTextCasingProperties.Cased) != 0;
    }
}

/// <summary>Shares pinned scalar conversion between font coverage and emitted text.</summary>
internal static partial class SvgTextCasing
{
    internal static SvgTextTransform Parse(ReadOnlySpan<char> value, SvgTextTransform inherited)
    {
        value = SvgCssWhitespace.Trim(value);
        if (value.IsEmpty || value.Equals("inherit", StringComparison.OrdinalIgnoreCase) || value.Equals("unset", StringComparison.OrdinalIgnoreCase)) return inherited;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("initial", StringComparison.OrdinalIgnoreCase)) return SvgTextTransform.None;
        if (value.Equals("uppercase", StringComparison.OrdinalIgnoreCase)) return SvgTextTransform.Uppercase;
        if (value.Equals("lowercase", StringComparison.OrdinalIgnoreCase)) return SvgTextTransform.Lowercase;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "This text-transform operation is not implemented.");
    }

    internal static string ReadLanguage(XmlReader reader, string inherited)
        => reader.GetAttribute("lang", "http://www.w3.org/XML/1998/namespace") ?? reader.GetAttribute("lang") ?? inherited;

    /// <summary>Writes one default-language mapping using context from original normalized text; caller owns output storage.</summary>
    internal static int Write(Rune scalar, SvgTextTransform transform, string language, bool finalSigma, Span<char> destination)
    {
        if (transform == SvgTextTransform.None) return scalar.EncodeToUtf16(destination);
        if (transform is not (SvgTextTransform.Uppercase or SvgTextTransform.Lowercase)) throw new ArgumentOutOfRangeException(nameof(transform));
        // Known content languages require their own qualified tailoring. Never
        // silently apply an invariant mapping to a language-sensitive request.
        if (language.Length != 0 && !language.Equals("en", StringComparison.OrdinalIgnoreCase)
            && !language.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text case conversion for this content language is not implemented.");
        if (transform == SvgTextTransform.Lowercase && scalar.Value == 0x3A3 && finalSigma)
            return new Rune(0x3C2).EncodeToUtf16(destination);
        ReadOnlySpan<int> expansions = transform == SvgTextTransform.Uppercase ? UppercaseExpansionRows : LowercaseExpansionRows;
        int expansion = Find(expansions, 3, scalar.Value);
        if (expansion >= 0)
        {
            ReadOnlySpan<char> text = transform == SvgTextTransform.Uppercase ? UppercaseExpansionText : LowercaseExpansionText;
            ReadOnlySpan<char> value = text.Slice(expansions[expansion + 1], expansions[expansion + 2]);
            value.CopyTo(destination);
            return value.Length;
        }
        ReadOnlySpan<int> pairs = transform == SvgTextTransform.Uppercase ? UppercasePairs : LowercasePairs;
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

    internal static SvgTextCasingProperties GetProperties(Rune scalar)
    {
        ReadOnlySpan<int> rows = CasingPropertyRanges;
        int low = 0, high = rows.Length / 3 - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2, offset = 3 * middle;
            if (scalar.Value < rows[offset]) high = middle - 1;
            else if (scalar.Value > rows[offset + 1]) low = middle + 1;
            else return (SvgTextCasingProperties)rows[offset + 2];
        }
        return SvgTextCasingProperties.None;
    }
}
