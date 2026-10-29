namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;

internal static partial class OpenTypeFont
{
    private const int MaximumKernLookups = 32;
    private const int MaximumKernSubtables = 32;

    /// <summary>
    /// Validates borrowed pair-list/class kerning without allocating lookup storage.
    /// Chained-context format-2 coverage is an admission guard, never a shaping
    /// implementation: covered glyphs remain unsupported even in a one-glyph run.
    /// Pair lookup filters admit bounded GDEF data; actual mark-positioning runs
    /// remain outside the supported text subset.
    /// Every traversal observes cancellation.
    /// </summary>
    private static OpenTypeGposPairTable? ParseGposPairTable(ReadOnlySpan<byte> data, OpenTypeTableRange table, OpenTypeTableRange? gdef, ushort glyphCount, CancellationToken cancellationToken)
    {
        RequireTableLength(table, 10, "GPOS");
        if (ReadUInt16(data, table.Offset) != 1 || ReadUInt16(data, table.Offset + 2) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0 GPOS tables are supported.");
        int scripts = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 4), 2, "GPOS ScriptList");
        int features = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 6), 2, "GPOS FeatureList");
        int lookups = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 8), 2, "GPOS LookupList");
        int feature = SelectLatinFeature(data, table, scripts, features, KernFeatureTag, "GPOS", cancellationToken);
        if (feature == 0) return null;
        if (ReadUInt16(data, feature) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "GPOS kern feature parameters are not supported.");
        int selectedCount = ReadUInt16(data, feature + 2);
        if (selectedCount > MaximumKernLookups) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS kern lookup count exceeds the admitted limit.");
        RequireTableRange(table, feature, 4 + selectedCount * 2, "GPOS kern feature");
        int lookupCount = ReadUInt16(data, lookups);
        RequireTableRange(table, lookups, 2 + lookupCount * 2, "GPOS LookupList");
        for (int index = 0; index < selectedCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int selected = ReadUInt16(data, feature + 4 + index * 2);
            if (selected >= lookupCount) throw InvalidFont("The GPOS kern feature references a lookup outside the LookupList.");
        }
        int firstPair = 0;
        int glyphClasses = 0;
        int markClasses = 0, markSets = 0, definitionMinor = 0;
        bool definitionsRead = false, filteredPairs = false;
        int previous = -1;
        var graph = new OpenTypeGposPairTable(table, feature, lookups, 0, 0);
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, cancellationToken))
        {
            int type = ReadUInt16(data, lookup);
            int flags = ReadUInt16(data, lookup + 2);
            int count = ReadUInt16(data, lookup + 4);
            if (count > MaximumKernSubtables) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS kern subtable count exceeds the admitted limit.");
            RequireTableRange(table, lookup, 6 + count * 2 + ((flags & 16) != 0 ? 2 : 0), "GPOS lookup");
            if ((flags & 0xE0) != 0) throw InvalidFont("The GPOS lookup flags contain reserved bits.");
            // RIGHT_TO_LEFT belongs to cursive attachment, not pair positioning
            // or the existing contextual admission guard.
            if ((flags & 0xFF1E) != 0)
            {
                if (type != 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filtered contextual GPOS positioning is not supported.");
                if (gdef is not OpenTypeTableRange definitions) throw InvalidFont("GPOS lookup filtering requires GDEF definitions.");
                if (!definitionsRead)
                {
                    RequireTableLength(definitions, 12, "GDEF");
                    definitionMinor = ReadUInt16(data, definitions.Offset + 2);
                    if (ReadUInt16(data, definitions.Offset) != 1 || definitionMinor is not 0 and not 2 and not 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0/1.2/1.3 GDEF definitions are supported.");
                    RequireTableLength(definitions, definitionMinor == 3 ? 18 : definitionMinor == 2 ? 14 : 12, "GDEF");
                    int classes = ReadUInt16(data, definitions.Offset + 4);
                    if (classes != 0)
                    {
                        glyphClasses = GetTableOffset(definitions, definitions.Offset, classes, 4, "GDEF glyph classes");
                        ValidateGposClasses(data, definitions, glyphClasses, glyphCount, 5, cancellationToken);
                    }
                    definitionsRead = true;
                }
                if ((flags & 14) != 0 && glyphClasses == 0) throw InvalidFont("GPOS ignore flags require GDEF glyph classes.");
                if ((flags & 0xFF00) != 0 && markClasses == 0)
                {
                    markClasses = GetTableOffset(definitions, definitions.Offset, ReadUInt16(data, definitions.Offset + 10), 4, "GDEF mark attachment classes");
                    ValidateGposClasses(data, definitions, markClasses, glyphCount, 256, cancellationToken);
                }
                if ((flags & 16) != 0)
                {
                    if (markSets == 0)
                    {
                        if (definitionMinor < 2) throw InvalidFont("GPOS mark filtering requires a GDEF mark glyph sets table.");
                        markSets = GetTableOffset(definitions, definitions.Offset, ReadUInt16(data, definitions.Offset + 12), 4, "GDEF mark glyph sets");
                        if (ReadUInt16(data, markSets) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-1 GDEF mark glyph sets are supported.");
                        RequireTableRange(definitions, markSets, 4 + ReadUInt16(data, markSets + 2) * 4, "GDEF mark glyph set offsets");
                    }
                    int selectedSet = ReadUInt16(data, lookup + 6 + count * 2);
                    if (selectedSet >= ReadUInt16(data, markSets + 2)) throw InvalidFont("The GPOS mark filtering set index exceeds GDEF's set count.");
                    uint relative = ReadUInt32(data, markSets + 4 + selectedSet * 4);
                    if (relative > int.MaxValue) throw InvalidFont("A GDEF mark coverage offset exceeds the supported range.");
                    int coverage = GetTableOffset(definitions, markSets, (int)relative, 4, "GDEF mark glyph set coverage");
                    ValidateGlyphCoverage(data, definitions, coverage, glyphCount, cancellationToken);
                }
                filteredPairs |= (flags & 6) != 0;
            }
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int subtable = GetTableOffset(table, lookup, ReadUInt16(data, lookup + 6 + index * 2), 4, "GPOS subtable");
                if (type == 2)
                {
                    ValidatePairSubtable(data, table, subtable, glyphCount, cancellationToken);
                    if (firstPair == 0) firstPair = subtable;
                }
                else if (type == 8 && ReadUInt16(data, subtable) == 2)
                {
                    RequireTableRange(table, subtable, 12, "GPOS chained-context admission header");
                    int coverage = GetTableOffset(table, subtable, ReadUInt16(data, subtable + 2), 4, "GPOS chained-context coverage");
                    ValidateGlyphCoverage(data, table, coverage, glyphCount, cancellationToken);
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "This GPOS kern lookup type or format is not supported.");
            }
        }
        return graph with { PairPosOffset = firstPair, GlyphClassOffset = glyphClasses, HasFilteredPairs = filteredPairs };

        static void ValidatePairSubtable(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, ushort glyphCount, CancellationToken token)
        {
            RequireTableRange(table, offset, 10, "GPOS PairPos");
            int format = ReadUInt16(data, offset);
            if (format is not 1 and not 2 || ReadUInt16(data, offset + 4) != 4 || ReadUInt16(data, offset + 6) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GPOS pair-list/class first-glyph X-advance adjustments are supported.");
            int coverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 2), 4, "GPOS PairPos coverage");
            int coveredCount = ValidateGlyphCoverage(data, table, coverage, glyphCount, token);
            if (format == 2)
            {
                RequireTableRange(table, offset, 16, "GPOS class pairs");
                int class1Count = ReadUInt16(data, offset + 12);
                int class2Count = ReadUInt16(data, offset + 14);
                if (class1Count == 0 || class2Count == 0) throw InvalidFont("GPOS pair classes must include class zero.");
                long length = 16L + 2L * class1Count * class2Count;
                if (length > int.MaxValue) throw InvalidFont("The GPOS class-pair matrix range is too large.");
                RequireTableRange(table, offset, (int)length, "GPOS class-pair matrix");
                token.ThrowIfCancellationRequested();
                ValidateGposClasses(data, table, GetTableOffset(table, offset, ReadUInt16(data, offset + 8), 4, "GPOS first classes"), glyphCount, class1Count, token);
                ValidateGposClasses(data, table, GetTableOffset(table, offset, ReadUInt16(data, offset + 10), 4, "GPOS second classes"), glyphCount, class2Count, token);
                return;
            }
            int count = ReadUInt16(data, offset + 8);
            if (count != coveredCount) throw InvalidFont("The GPOS coverage and pair-set counts differ.");
            RequireTableRange(table, offset, 10 + count * 2, "GPOS PairPos");
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int pairs = GetTableOffset(table, offset, ReadUInt16(data, offset + 10 + index * 2), 2, "GPOS PairSet");
                int pairCount = ReadUInt16(data, pairs);
                RequireTableRange(table, pairs, 2 + pairCount * 4, "GPOS PairSet");
                int previousGlyph = -1;
                for (int pair = 0; pair < pairCount; pair++)
                {
                    token.ThrowIfCancellationRequested();
                    int glyph = ReadUInt16(data, pairs + 2 + pair * 4);
                    if (glyph >= glyphCount || glyph <= previousGlyph) throw InvalidFont("The GPOS PairSet glyphs are invalid.");
                    previousGlyph = glyph;
                }
            }
        }
    }

    /// <summary>Checks each distinct mapped glyph before layout, including the last or sole glyph; borrows validated, unchanged font bytes.</summary>
    internal static void ValidateGlyphPositioning(ReadOnlyMemory<byte> bytes, ushort glyph, OpenTypeFontMetrics metrics, CancellationToken token)
    {
        if (glyph >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph));
        if (metrics.Gpos is not OpenTypeGposPairTable graph) return;
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> data = bytes.Span;
        if (graph.GlyphClassOffset != 0 && GetGposClass(data, graph.GlyphClassOffset, glyph, token) == 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "GPOS mark positioning is not supported in direct text.");
        int previous = -1;
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, token))
        {
            if (ReadUInt16(data, lookup) == 2) continue;
            int count = ReadUInt16(data, lookup + 4);
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                int subtable = GetTableOffset(graph.Range, lookup, ReadUInt16(data, lookup + 6 + index * 2), 4, "GPOS subtable");
                int coverage = GetTableOffset(graph.Range, subtable, ReadUInt16(data, subtable + 2), 4, "OpenType coverage");
                if (GetGlyphCoverageIndex(data, coverage, glyph, token) >= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The glyph requires unsupported contextual GPOS positioning.");
            }
        }
    }

    /// <summary>Accumulates ordered kerning lookups, stopping at the first matching subtable even when its adjustment is zero.</summary>
    internal static int GetPairAdjustment(ReadOnlyMemory<byte> bytes, ushort leftGlyphIndex, ushort rightGlyphIndex, OpenTypeFontMetrics metrics, CancellationToken cancellationToken)
    {
        if (metrics.Gpos is not OpenTypeGposPairTable graph) return GetKerningAdjustment(bytes, leftGlyphIndex, rightGlyphIndex, metrics, cancellationToken);
        ValidateGlyphPositioning(bytes, leftGlyphIndex, metrics, cancellationToken);
        ValidateGlyphPositioning(bytes, rightGlyphIndex, metrics, cancellationToken);
        ReadOnlySpan<byte> data = bytes.Span;
        int adjustment = 0;
        int previous = -1;
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, cancellationToken))
        {
            if (ReadUInt16(data, lookup) != 2) continue;
            int flags = ReadUInt16(data, lookup + 2);
            if (IsPairGlyphIgnored(data, graph, leftGlyphIndex, flags, cancellationToken)
                || IsPairGlyphIgnored(data, graph, rightGlyphIndex, flags, cancellationToken)) continue;
            adjustment += GetLookupPairAdjustment(data, graph, lookup, leftGlyphIndex, rightGlyphIndex, cancellationToken);
        }
        return adjustment;
    }

    /// <summary>Accumulates filtered pair adjustments within a borrowed, already shaped font/size/chunk run.</summary>
    /// <remarks>Retains no storage. Each visited glyph/table step observes cancellation;
    /// actual mark-positioning and contextual glyphs keep their existing unsupported outcomes.</remarks>
    internal static int GetPairAdjustment(ReadOnlyMemory<byte> bytes, ReadOnlySpan<SvgPositionedGlyph> glyphs, OpenTypeFontMetrics metrics, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (glyphs.Length < 2) return 0;
        ushort left = glyphs[0].GlyphIndex;
        if (metrics.Gpos is not OpenTypeGposPairTable graph) return GetKerningAdjustment(bytes, left, glyphs[1].GlyphIndex, metrics, token);
        ValidateGlyphPositioning(bytes, left, metrics, token);
        ReadOnlySpan<byte> data = bytes.Span;
        int adjustment = 0, previous = -1;
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, token))
        {
            if (ReadUInt16(data, lookup) != 2) continue;
            int flags = ReadUInt16(data, lookup + 2);
            if (IsPairGlyphIgnored(data, graph, left, flags, token)) continue;
            int next = 1;
            for (; next < glyphs.Length; next++)
            {
                token.ThrowIfCancellationRequested();
                ushort right = glyphs[next].GlyphIndex;
                ValidateGlyphPositioning(bytes, right, metrics, token);
                if (!IsPairGlyphIgnored(data, graph, right, flags, token)) break;
            }
            if (next < glyphs.Length) adjustment += GetLookupPairAdjustment(data, graph, lookup, left, glyphs[next].GlyphIndex, token);
        }
        return adjustment;
    }

    // Mark filtering is validated at admission; actual mark runs remain unsupported.
    // Admitted base/ligature filtering applies to both lookup starts and partners.
    private static bool IsPairGlyphIgnored(ReadOnlySpan<byte> data, OpenTypeGposPairTable graph, ushort glyph, int flags, CancellationToken token)
    {
        if ((flags & 6) == 0) return false;
        int glyphClass = GetGposClass(data, graph.GlyphClassOffset, glyph, token);
        return glyphClass == 1 && (flags & 2) != 0 || glyphClass == 2 && (flags & 4) != 0;
    }

    private static int GetLookupPairAdjustment(ReadOnlySpan<byte> data, OpenTypeGposPairTable graph, int lookup, ushort leftGlyphIndex, ushort rightGlyphIndex, CancellationToken cancellationToken)
    {
        int count = ReadUInt16(data, lookup + 4);
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int subtable = GetTableOffset(graph.Range, lookup, ReadUInt16(data, lookup + 6 + index * 2), 10, "GPOS PairPos");
            int coverage = GetTableOffset(graph.Range, subtable, ReadUInt16(data, subtable + 2), 4, "OpenType coverage");
            int covered = GetGlyphCoverageIndex(data, coverage, leftGlyphIndex, cancellationToken);
            if (covered < 0) continue;
            if (ReadUInt16(data, subtable) == 2)
            {
                int class1 = GetGposClass(data, subtable + ReadUInt16(data, subtable + 8), leftGlyphIndex, cancellationToken);
                int class2 = GetGposClass(data, subtable + ReadUInt16(data, subtable + 10), rightGlyphIndex, cancellationToken);
                int class2Count = ReadUInt16(data, subtable + 14);
                return ReadInt16(data, subtable + 16 + 2 * (class1 * class2Count + class2));
            }
            int pairs = GetTableOffset(graph.Range, subtable, ReadUInt16(data, subtable + 10 + covered * 2), 2, "GPOS PairSet");
            int pairCount = ReadUInt16(data, pairs);
            int low = 0;
            int high = pairCount;
            while (low < high)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int mid = low + (high - low) / 2;
                if (ReadUInt16(data, pairs + 2 + mid * 4) < rightGlyphIndex) low = mid + 1;
                else high = mid;
            }
            if (low == pairCount || ReadUInt16(data, pairs + 2 + low * 4) != rightGlyphIndex) continue;
            return ReadInt16(data, pairs + 4 + low * 4);
        }
        return 0;
    }

    // Feature lookup indices are a set; execution order comes from the LookupList.
    // At most 32 selections are scanned per step, with no heap-owned index array.
    private static bool TryGetNextKernLookup(ReadOnlySpan<byte> data, OpenTypeGposPairTable graph, ref int previous, out int offset, CancellationToken token)
    {
        int next = int.MaxValue;
        int count = ReadUInt16(data, graph.FeatureOffset + 2);
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            int candidate = ReadUInt16(data, graph.FeatureOffset + 4 + index * 2);
            if (candidate > previous && candidate < next) next = candidate;
        }
        offset = 0;
        if (next == int.MaxValue) return false;
        previous = next;
        offset = GetTableOffset(graph.Range, graph.LookupListOffset, ReadUInt16(data, graph.LookupListOffset + 2 + next * 2), 6, "GPOS lookup");
        return true;
    }

    private static int ValidateGlyphCoverage(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, int glyphCount, CancellationToken token)
    {
        int format = ReadUInt16(data, offset);
        int count = ReadUInt16(data, offset + 2);
        if (format is not 1 and not 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only OpenType coverage formats 1 and 2 are supported.");
        RequireTableRange(table, offset, 4 + count * (format == 1 ? 2 : 6), "OpenType coverage");
        int previous = -1;
        int covered = 0;
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            int record = offset + 4 + index * (format == 1 ? 2 : 6);
            int start = ReadUInt16(data, record);
            int end = format == 1 ? start : ReadUInt16(data, record + 2);
            if (start <= previous || end < start || end >= glyphCount || format == 2 && ReadUInt16(data, record + 4) != covered) throw InvalidFont("The OpenType coverage glyphs or indices are invalid.");
            previous = end;
            covered += end - start + 1;
        }
        return covered;
    }

    private static int GetGlyphCoverageIndex(ReadOnlySpan<byte> data, int offset, ushort glyph, CancellationToken token)
    {
        bool ranges = ReadUInt16(data, offset) == 2;
        int low = 0;
        int high = ReadUInt16(data, offset + 2);
        while (low < high)
        {
            token.ThrowIfCancellationRequested();
            int mid = low + (high - low) / 2;
            int record = offset + 4 + mid * (ranges ? 6 : 2);
            int start = ReadUInt16(data, record);
            int end = ranges ? ReadUInt16(data, record + 2) : start;
            if (glyph < start) high = mid;
            else if (glyph > end) low = mid + 1;
            else return ranges ? ReadUInt16(data, record + 4) + glyph - start : mid;
        }
        return -1;
    }

    private static void ValidateGposClasses(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, int glyphCount, int classCount, CancellationToken token)
    {
        int format = ReadUInt16(data, offset);
        if (format is not 1 and not 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GPOS/GDEF class definitions 1 and 2 are supported.");
        RequireTableRange(table, offset, format == 1 ? 6 : 4, "GPOS classes");
        int count = ReadUInt16(data, offset + (format == 1 ? 4 : 2));
        RequireTableRange(table, offset, (format == 1 ? 6 + count * 2 : 4 + count * 6), "GPOS classes");
        int startGlyph = format == 1 ? ReadUInt16(data, offset + 2) : 0;
        if (format == 1 && startGlyph + count > glyphCount) throw InvalidFont("The GPOS class glyph range is invalid.");
        int previous = -1;
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            int record = offset + (format == 1 ? 6 + index * 2 : 4 + index * 6);
            int value = ReadUInt16(data, record + (format == 1 ? 0 : 4));
            if (value >= classCount) throw InvalidFont("A GPOS glyph class exceeds its class count.");
            if (format == 1) continue;
            int start = ReadUInt16(data, record);
            int end = ReadUInt16(data, record + 2);
            if (start <= previous || end < start || end >= glyphCount) throw InvalidFont("The GPOS class glyph ranges are invalid.");
            previous = end;
        }
    }

    private static int GetGposClass(ReadOnlySpan<byte> data, int offset, ushort glyph, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ReadUInt16(data, offset) == 1)
        {
            int relative = glyph - ReadUInt16(data, offset + 2);
            return relative >= 0 && relative < ReadUInt16(data, offset + 4) ? ReadUInt16(data, offset + 6 + relative * 2) : 0;
        }
        int low = 0;
        int high = ReadUInt16(data, offset + 2);
        while (low < high)
        {
            token.ThrowIfCancellationRequested();
            int mid = low + (high - low) / 2;
            int record = offset + 4 + mid * 6;
            if (glyph < ReadUInt16(data, record)) high = mid;
            else if (glyph > ReadUInt16(data, record + 2)) low = mid + 1;
            else return ReadUInt16(data, record + 4);
        }
        return 0;
    }
}
