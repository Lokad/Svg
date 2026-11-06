namespace Lokad.Svg.Fonts;

using System.Text;
using Lokad.Svg.Parsing;

[Flags]
internal enum SvgTextLayoutRequirement { None = 0, Bidirectional = 512, ComplexScript = 1024 }

/// <summary>Pinned canonical Unicode primitives for font-aware shaping; no text or font ownership.</summary>
internal static partial class SvgCanonicalText
{
    internal const int MaximumDecomposedScalars = 4;
    private const int HangulStart = 0xAC00, HangulCount = 11172, TrailingCount = 28, VowelCount = 21;

    /// <summary>Identifies characters kept for shaping context but hidden after positioning.</summary>
    internal static bool IsDefaultIgnorable(Rune scalar)
    {
        if (scalar.Value < 0xAD) return false;
        ReadOnlySpan<int> rows = DefaultIgnorableRanges;
        int low = 0, high = rows.Length / 2;
        while (low < high)
        {
            int middle = low + (high - low) / 2, offset = middle * 2;
            if (scalar.Value < rows[offset]) high = middle;
            else if (scalar.Value > rows[offset + 1]) low = middle + 1;
            else return true;
        }
        return false;
    }

    /// <summary>Returns the canonical combining class and Unicode mark category, independently of GDEF glyph classes.</summary>
    internal static (byte CombiningClass, bool IsMark) GetProperties(Rune scalar)
    {
        int properties = GetPackedProperties(scalar);
        return ((byte)(properties & 255), (properties & 256) != 0);
    }

    /// <summary>Checks scalar work independently of a font's selected substitution/positioning features.</summary>
    /// <remarks>Uses pinned canonical data, including singleton decompositions; owns no storage.</remarks>
    internal static bool RequiresNormalization(Rune scalar)
    {
        if (scalar.IsAscii) return false;
        if (GetProperties(scalar).IsMark || IsDefaultIgnorable(scalar)) return true;
        Span<Rune> parts = stackalloc Rune[MaximumDecomposedScalars];
        int count = Decompose(scalar, parts);
        return count != 1 || parts[0] != scalar;
    }

    /// <summary>Checks direct or complete canonical-component coverage against one borrowed physical font.</summary>
    /// <remarks>Acquisition and face selection share this predicate with normalization. No program or storage is acquired;
    /// all component cmap traversals observe cancellation. Partial decomposition coverage never selects a face.</remarks>
    internal static bool HasCoverage(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, Rune scalar, CancellationToken token)
    {
        if (OpenTypeFont.GetGlyphIndex(bytes, (uint)scalar.Value, metrics, token) != 0) return true;
        Span<Rune> parts = stackalloc Rune[MaximumDecomposedScalars];
        int count = Decompose(scalar, parts);
        if (count == 1 && parts[0] == scalar) return false;
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (OpenTypeFont.GetGlyphIndex(bytes, (uint)parts[index].Value, metrics, token) == 0) return false;
        }
        return true;
    }

    /// <summary>Reads pinned layout requirements independently of font coverage and canonical mark properties.</summary>
    internal static SvgTextLayoutRequirement GetLayoutRequirements(Rune scalar)
        => (SvgTextLayoutRequirement)(GetPackedProperties(scalar) & 1536);

    /// <summary>Enforces the finite text-layout subset before font demand or glyph layout.</summary>
    /// <remarks>No storage or font is acquired. Callers retain source context and observe cancellation while traversing scalars.</remarks>
    internal static void ValidateSimpleLayout(Rune scalar)
    {
        SvgTextLayoutRequirement required = GetLayoutRequirements(scalar);
        if ((required & SvgTextLayoutRequirement.Bidirectional) != 0)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bidirectional text layout is not implemented.");
        if ((required & SvgTextLayoutRequirement.ComplexScript) != 0)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "This script requires text shaping outside the supported subset.");
    }

    /// <summary>Validates borrowed discovery chunks, retaining only a split surrogate and attaching the active source on failure.</summary>
    /// <remarks>Input is XML-validated UTF-16; the caller starts with a zero pending character. No storage or font is acquired; cancellation is checked per scalar.</remarks>
    internal static void ValidateSimpleLayout(ReadOnlySpan<char> text, ref char pending, SvgSourceContext? source, CancellationToken token)
    {
        try
        {
            int first = 0, end = text.Length;
            if (pending != '\0' && end != 0)
            {
                token.ThrowIfCancellationRequested();
                ValidateSimpleLayout(new Rune(pending, text[0]));
                pending = '\0'; first = 1;
            }
            if (first < end && char.IsHighSurrogate(text[end - 1])) pending = text[--end];
            foreach (Rune scalar in text[first..end].EnumerateRunes())
            {
                token.ThrowIfCancellationRequested();
                ValidateSimpleLayout(scalar);
            }
        }
        catch (SvgException exception)
        {
            if (source is {} context) exception.AttachSource(context.Describe(string.Empty, string.Empty));
            throw;
        }
    }

    private static int GetPackedProperties(Rune scalar)
    {
        if (scalar.Value < 0x300) return 0;
        ReadOnlySpan<int> rows = CanonicalProperties;
        int low = 0, high = rows.Length / 3;
        while (low < high)
        {
            int middle = low + (high - low) / 2, offset = middle * 3;
            if (scalar.Value < rows[offset]) high = middle;
            else if (scalar.Value > rows[offset + 1]) low = middle + 1;
            else return rows[offset + 2];
        }
        return 0;
    }

    /// <summary>Writes the fully recursive canonical decomposition, retaining compatibility distinctions.</summary>
    /// <remarks>The caller lends space for four scalars. Lookup is bounded and allocates no storage;
    /// the caller observes cancellation while traversing source text.</remarks>
    internal static int Decompose(Rune scalar, Span<Rune> destination)
    {
        if (destination.Length < MaximumDecomposedScalars) throw new ArgumentException("Canonical decomposition requires four scalar slots.", nameof(destination));
        int syllable = scalar.Value - HangulStart;
        if ((uint)syllable < HangulCount)
        {
            destination[0] = new(0x1100 + syllable / (VowelCount * TrailingCount));
            destination[1] = new(0x1161 + syllable % (VowelCount * TrailingCount) / TrailingCount);
            int trailing = syllable % TrailingCount;
            if (trailing == 0) return 2;
            destination[2] = new(0x11A7 + trailing);
            return 3;
        }
        ReadOnlySpan<int> rows = CanonicalDecompositions;
        int low = 0, high = rows.Length / 6;
        while (low < high)
        {
            int middle = low + (high - low) / 2, offset = middle * 6;
            if (scalar.Value < rows[offset]) high = middle;
            else if (scalar.Value > rows[offset]) low = middle + 1;
            else
            {
                int count = rows[offset + 1];
                for (int index = 0; index < count; index++) destination[index] = new(rows[offset + 2 + index]);
                return count;
            }
        }
        destination[0] = scalar;
        return 1;
    }

    /// <summary>Finds a canonical pair composition, excluding Unicode composition exclusions.</summary>
    /// <remarks>Does not apply the sequence's combining-class blocking rule. The shaping caller
    /// owns that state and checks font coverage before replacing the pair. No allocation or shared mutation.</remarks>
    internal static bool TryCompose(Rune first, Rune second, out Rune composite)
    {
        int leading = first.Value - 0x1100, vowel = second.Value - 0x1161;
        if ((uint)leading < 19 && (uint)vowel < VowelCount)
        {
            composite = new(HangulStart + (leading * VowelCount + vowel) * TrailingCount);
            return true;
        }
        int syllable = first.Value - HangulStart, trailing = second.Value - 0x11A7;
        if ((uint)syllable < HangulCount && syllable % TrailingCount == 0 && trailing is > 0 and < TrailingCount)
        {
            composite = new(first.Value + trailing);
            return true;
        }
        ReadOnlySpan<int> rows = CanonicalCompositions;
        int low = 0, high = rows.Length / 3;
        while (low < high)
        {
            int middle = low + (high - low) / 2, offset = middle * 3;
            int comparison = first.Value.CompareTo(rows[offset]);
            if (comparison == 0) comparison = second.Value.CompareTo(rows[offset + 1]);
            if (comparison < 0) high = middle;
            else if (comparison > 0) low = middle + 1;
            else { composite = new(rows[offset + 2]); return true; }
        }
        composite = default;
        return false;
    }
}
