using System.Text;
using System.Xml;

namespace Lokad.Svg.Parsing;

/// <summary>Names case conversion independently of original SVG character addresses.</summary>
internal enum SvgTextTransform { None, Uppercase, Lowercase, Capitalize }

/// <summary>Classifies original scalars for the Unicode case-context rules.</summary>
[Flags]
internal enum SvgTextCasingProperties { None = 0, Cased = 1, Ignorable = 2, SoftDotted = 4, Combining = 8, Above = 16 }

/// <summary>Original-text conditions shared by projection and streaming font discovery.</summary>
[Flags]
internal enum SvgTextCasingConditions
{ None = 0, WordStart = 1, FinalSigma = 2, AfterI = 4, AfterSoftDotted = 8, BeforeDot = 16, MoreAbove = 32, WordInitialUnit = 64 }

internal enum SvgTextCasingLookahead { None, Sigma, BeforeDot, MoreAbove }

/// <summary>UAX29 classes relevant to the start of a cased letter's word.</summary>
internal enum SvgTextCasingWordClass { Other, Letter, Numeric, Connector, Medial, Ignorable }

/// <summary>Retains source context across chunks and styles within one logical text element; parser-thread confined.</summary>
internal struct SvgTextCasingContext
{
    internal bool PrecedingCased { get; private set; }
    private SvgTextCasingWordClass _previousWord;
    private SvgTextCasingWordClass _beforePreviousWord;
    private bool _afterI;
    private bool _afterSoftDotted;
    private bool _wordInitialUnit;

    /// <summary>Advances original scalar context and returns whether this scalar starts a cased word.</summary>
    internal SvgTextCasingConditions Observe(Rune scalar, SvgTextCasingProperties properties)
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
        SvgTextCasingConditions conditions = start ? SvgTextCasingConditions.WordStart : SvgTextCasingConditions.None;
        if (_afterI) conditions |= SvgTextCasingConditions.AfterI;
        if (_afterSoftDotted) conditions |= SvgTextCasingConditions.AfterSoftDotted;
        bool combining = (properties & SvgTextCasingProperties.Combining) != 0;
        if (!combining) _wordInitialUnit = start;
        if (_wordInitialUnit) conditions |= SvgTextCasingConditions.WordInitialUnit;
        if (!combining || (properties & SvgTextCasingProperties.Above) != 0)
        {
            _afterI = scalar.Value == 'I';
            _afterSoftDotted = (properties & SvgTextCasingProperties.SoftDotted) != 0;
        }
        return conditions;
    }
}

/// <summary>Shares pinned scalar conversion between font coverage and emitted text.</summary>
internal static partial class SvgTextCasing
{
    private enum Language { Default, Turkic, Lithuanian }
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
    internal static int Write(Rune scalar, SvgTextTransform transform, string language, SvgTextCasingConditions conditions, Span<char> destination)
    {
        if (transform == SvgTextTransform.None) return scalar.EncodeToUtf16(destination);
        if (transform is not (SvgTextTransform.Uppercase or SvgTextTransform.Lowercase or SvgTextTransform.Capitalize)) throw new ArgumentOutOfRangeException(nameof(transform));
        Language selected = ReadLanguage(language);
        if (selected == Language.Lithuanian && scalar.Value == 0x307
            && (conditions & SvgTextCasingConditions.AfterSoftDotted) != 0
            && (transform == SvgTextTransform.Uppercase || transform == SvgTextTransform.Capitalize
                && (conditions & SvgTextCasingConditions.WordInitialUnit) != 0)) return 0;
        if (transform == SvgTextTransform.Capitalize && (conditions & SvgTextCasingConditions.WordStart) == 0)
            return scalar.EncodeToUtf16(destination);
        if (selected == Language.Turkic)
        {
            if (transform == SvgTextTransform.Lowercase)
            {
                if (scalar.Value == 'I') return new Rune((conditions & SvgTextCasingConditions.BeforeDot) != 0 ? 'i' : 0x131).EncodeToUtf16(destination);
                if (scalar.Value == 0x130) return new Rune('i').EncodeToUtf16(destination);
                if (scalar.Value == 0x307 && (conditions & SvgTextCasingConditions.AfterI) != 0) return 0;
            }
            else if (scalar.Value == 'i') return new Rune(0x130).EncodeToUtf16(destination);
        }
        if (selected == Language.Lithuanian && transform == SvgTextTransform.Lowercase)
        {
            if ((conditions & SvgTextCasingConditions.MoreAbove) != 0 && scalar.Value is 'I' or 'J' or 0x12E)
            {
                destination[0] = scalar.Value switch { 'I' => 'i', 'J' => 'j', _ => '\u012F' };
                destination[1] = '\u0307';
                return 2;
            }
            ReadOnlySpan<char> accented = scalar.Value switch
            { 0xCC => "i\u0307\u0300", 0xCD => "i\u0307\u0301", 0x128 => "i\u0307\u0303", _ => [] };
            if (!accented.IsEmpty) { accented.CopyTo(destination); return accented.Length; }
        }
        if (transform == SvgTextTransform.Lowercase && scalar.Value == 0x3A3 && (conditions & SvgTextCasingConditions.FinalSigma) != 0)
            return new Rune(0x3C2).EncodeToUtf16(destination);
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

    /// <summary>Chooses the one forward decision needed by this original scalar; no text is retained.</summary>
    internal static SvgTextCasingLookahead GetLookahead(Rune scalar, SvgTextTransform transform, string language, bool precedingCased)
    {
        if (transform != SvgTextTransform.Lowercase) return SvgTextCasingLookahead.None;
        Language selected = ReadLanguage(language);
        if (scalar.Value == 0x3A3 && precedingCased) return SvgTextCasingLookahead.Sigma;
        if (selected == Language.Turkic && scalar.Value == 'I') return SvgTextCasingLookahead.BeforeDot;
        if (selected == Language.Lithuanian && scalar.Value is 'I' or 'J' or 0x12E) return SvgTextCasingLookahead.MoreAbove;
        return SvgTextCasingLookahead.None;
    }

    /// <summary>Resolves a pending scalar from the next original character, before any conversion or style reset.</summary>
    internal static bool TryResolve(SvgTextCasingLookahead kind, Rune following, SvgTextCasingProperties properties, out bool matched)
    {
        matched = kind switch
        {
            SvgTextCasingLookahead.Sigma => (properties & SvgTextCasingProperties.Cased) != 0,
            SvgTextCasingLookahead.BeforeDot => following.Value == 0x307,
            SvgTextCasingLookahead.MoreAbove => (properties & SvgTextCasingProperties.Above) != 0,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return kind == SvgTextCasingLookahead.Sigma ? (properties & SvgTextCasingProperties.Ignorable) == 0
            : matched || (properties & SvgTextCasingProperties.Combining) == 0 || (properties & SvgTextCasingProperties.Above) != 0;
    }

    internal static SvgTextCasingConditions Resolve(SvgTextCasingLookahead kind, bool matched)
        => kind switch
        {
            SvgTextCasingLookahead.Sigma => matched ? SvgTextCasingConditions.None : SvgTextCasingConditions.FinalSigma,
            SvgTextCasingLookahead.BeforeDot => matched ? SvgTextCasingConditions.BeforeDot : SvgTextCasingConditions.None,
            SvgTextCasingLookahead.MoreAbove => matched ? SvgTextCasingConditions.MoreAbove : SvgTextCasingConditions.None,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static Language ReadLanguage(string language)
    {
        ReadOnlySpan<char> primary = language.AsSpan();
        int separator = primary.IndexOf('-');
        if (separator >= 0) primary = primary[..separator];
        if (language.Length == 0 || primary.Equals("en", StringComparison.OrdinalIgnoreCase)) return Language.Default;
        if (primary.Equals("tr", StringComparison.OrdinalIgnoreCase) || primary.Equals("az", StringComparison.OrdinalIgnoreCase)) return Language.Turkic;
        if (primary.Equals("lt", StringComparison.OrdinalIgnoreCase)) return Language.Lithuanian;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Text case conversion for this content language is not implemented.");
    }

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
