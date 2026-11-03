using System.Text;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Names case conversion independently of original SVG character addresses.</summary>
internal enum SvgTextTransform { None, Uppercase, Lowercase, Capitalize }

/// <summary>Classifies original scalars for the Unicode case-context rules.</summary>
[Flags]
internal enum SvgTextCasingProperties { None = 0, Cased = 1, Ignorable = 2 }

/// <summary>UAX29 classes relevant to the start of a cased letter's word.</summary>
internal enum SvgTextCasingWordClass { Other, Letter, Numeric, Connector, Medial, Ignorable }

/// <summary>Retains source context across chunks and styles within one logical text element; parser-thread confined.</summary>
internal struct SvgTextCasingContext
{
    internal bool PrecedingCased { get; private set; }
    private SvgTextCasingWordClass _previousWord;
    private SvgTextCasingWordClass _beforePreviousWord;

    /// <summary>Advances original scalar context and returns whether this scalar starts a cased word.</summary>
    internal bool Observe(Rune scalar, SvgTextCasingProperties properties)
    {
        // Ignorable takes precedence where the two properties overlap. Unicode
        // context matching consumes the ignorable sequence without backtracking.
        if ((properties & SvgTextCasingProperties.Ignorable) == 0)
            PrecedingCased = (properties & SvgTextCasingProperties.Cased) != 0;
        SvgTextCasingWordClass word = SvgTextCasing.GetWordClass(scalar);
        // All nonidentity capitalization scalars are ALetter except the
        // ignorable U+0345. WB5/7/10/13b decide their starts using two classes;
        // WB4 preserves those classes across combining/format characters.
        bool start = word == SvgTextCasingWordClass.Letter
            && _previousWord is not (SvgTextCasingWordClass.Letter or SvgTextCasingWordClass.Numeric or SvgTextCasingWordClass.Connector)
            && !(_previousWord == SvgTextCasingWordClass.Medial && _beforePreviousWord == SvgTextCasingWordClass.Letter);
        if (word != SvgTextCasingWordClass.Ignorable)
        {
            _beforePreviousWord = _previousWord;
            _previousWord = word;
        }
        return start;
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
        if (value.Equals("capitalize", StringComparison.OrdinalIgnoreCase)) return SvgTextTransform.Capitalize;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "This text-transform operation is not implemented.");
    }

    internal static string ReadLanguage(XmlReader reader, string inherited)
        => reader.GetAttribute("lang", "http://www.w3.org/XML/1998/namespace") ?? reader.GetAttribute("lang") ?? inherited;

    /// <summary>Writes one default-language mapping using context from original normalized text; caller owns output storage.</summary>
    internal static int Write(Rune scalar, SvgTextTransform transform, string language, bool finalSigma, bool wordStart, Span<char> destination)
    {
        if (transform == SvgTextTransform.None) return scalar.EncodeToUtf16(destination);
        if (transform is not (SvgTextTransform.Uppercase or SvgTextTransform.Lowercase or SvgTextTransform.Capitalize)) throw new ArgumentOutOfRangeException(nameof(transform));
        // Known content languages require their own qualified tailoring. Never
        // silently apply an invariant mapping to a language-sensitive request.
        if (language.Length != 0 && !language.Equals("en", StringComparison.OrdinalIgnoreCase)
            && !language.StartsWith("en-", StringComparison.OrdinalIgnoreCase))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text case conversion for this content language is not implemented.");
        if (transform == SvgTextTransform.Lowercase && scalar.Value == 0x3A3 && finalSigma)
            return new Rune(0x3C2).EncodeToUtf16(destination);
        if (transform == SvgTextTransform.Capitalize && !wordStart) return scalar.EncodeToUtf16(destination);
        ReadOnlySpan<int> expansions = transform == SvgTextTransform.Uppercase ? UppercaseExpansionRows
            : transform == SvgTextTransform.Lowercase ? LowercaseExpansionRows : CapitalizationExpansionRows;
        int expansion = Find(expansions, 3, scalar.Value);
        if (expansion >= 0)
        {
            ReadOnlySpan<char> text = transform == SvgTextTransform.Uppercase ? UppercaseExpansionText
                : transform == SvgTextTransform.Lowercase ? LowercaseExpansionText : CapitalizationExpansionText;
            ReadOnlySpan<char> value = text.Slice(expansions[expansion + 1], expansions[expansion + 2]);
            value.CopyTo(destination);
            return value.Length;
        }
        ReadOnlySpan<int> pairs = transform == SvgTextTransform.Uppercase ? UppercasePairs
            : transform == SvgTextTransform.Lowercase ? LowercasePairs : CapitalizationPairs;
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
        => (SvgTextCasingProperties)GetRangeValue(CasingPropertyRanges, scalar.Value);

    internal static SvgTextCasingWordClass GetWordClass(Rune scalar)
        => (SvgTextCasingWordClass)GetRangeValue(CasingWordRanges, scalar.Value);

    private static int GetRangeValue(ReadOnlySpan<int> rows, int scalar)
    {
        int low = 0, high = rows.Length / 3 - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2, offset = 3 * middle;
            if (scalar < rows[offset]) high = middle - 1;
            else if (scalar > rows[offset + 1]) low = middle + 1;
            else return rows[offset + 2];
        }
        return 0;
    }
}
