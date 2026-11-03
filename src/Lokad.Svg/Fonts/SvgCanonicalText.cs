namespace Lokad.Svg.Fonts;

using System.Text;

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
        if (scalar.Value < 0x300) return (0, false);
        ReadOnlySpan<int> rows = CanonicalProperties;
        int low = 0, high = rows.Length / 3;
        while (low < high)
        {
            int middle = low + (high - low) / 2, offset = middle * 3;
            if (scalar.Value < rows[offset]) high = middle;
            else if (scalar.Value > rows[offset + 1]) low = middle + 1;
            else return ((byte)(rows[offset + 2] & 255), (rows[offset + 2] & 256) != 0);
        }
        return (0, false);
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
