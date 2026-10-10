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
    /// IgnoreMarks is admitted only for runs containing no GDEF mark glyphs.
    /// Every traversal observes cancellation and the caller's parser work budget.
    /// </summary>
    private static OpenTypeGposPairTable? ParseGposPairTable(ReadOnlySpan<byte> data, OpenTypeTableRange table, OpenTypeTableRange? gdef, ushort glyphCount, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        RequireTableLength(table, 10, "GPOS");
        if (ReadUInt16(data, table.Offset) != 1 || ReadUInt16(data, table.Offset + 2) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0 GPOS tables are supported.");
        int scripts = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 4), 2, "GPOS ScriptList");
        int features = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 6), 2, "GPOS FeatureList");
        int lookups = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 8), 2, "GPOS LookupList");
        int featureCount = ReadUInt16(data, features);
        RequireTableRange(table, features, 2 + featureCount * 6, "GPOS FeatureList");
        int feature = 0;
        int featureIndex = -1;
        for (int index = 0; index < featureCount; index++)
        {
            Consume(workCounter, cancellationToken);
            int record = features + 2 + index * 6;
            if (ReadUInt32(data, record) != KernFeatureTag) continue;
            if (feature != 0) throw InvalidFont("The GPOS FeatureList contains duplicate kern features.");
            feature = GetTableOffset(table, features, ReadUInt16(data, record + 4), 4, "GPOS kern feature");
            featureIndex = index;
        }
        if (feature == 0) return null;
        ValidateDefaultGposKernFeature(data, table, scripts, featureCount, featureIndex, workCounter, cancellationToken);
        if (ReadUInt16(data, feature) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "GPOS kern feature parameters are not supported.");
        int selectedCount = ReadUInt16(data, feature + 2);
        if (selectedCount > MaximumKernLookups) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS kern lookup count exceeds the admitted limit.");
        RequireTableRange(table, feature, 4 + selectedCount * 2, "GPOS kern feature");
        int lookupCount = ReadUInt16(data, lookups);
        RequireTableRange(table, lookups, 2 + lookupCount * 2, "GPOS LookupList");
        for (int index = 0; index < selectedCount; index++)
        {
            Consume(workCounter, cancellationToken);
            int selected = ReadUInt16(data, feature + 4 + index * 2);
            if (selected >= lookupCount) throw InvalidFont("The GPOS kern feature references a lookup outside the LookupList.");
        }
        int firstPair = 0;
        int glyphClasses = 0;
        int previous = -1;
        var graph = new OpenTypeGposPairTable(table, feature, lookups, 0, null, 0);
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, workCounter, cancellationToken))
        {
            int type = ReadUInt16(data, lookup);
            int flags = ReadUInt16(data, lookup + 2);
            if (type == 2 && flags == 8)
            {
                if (gdef is not OpenTypeTableRange definitions) throw InvalidFont("GPOS IgnoreMarks requires GDEF glyph classes.");
                if (glyphClasses == 0)
                {
                    RequireTableLength(definitions, 12, "GDEF");
                    int minor = ReadUInt16(data, definitions.Offset + 2);
                    if (ReadUInt16(data, definitions.Offset) != 1 || minor is not 0 and not 2 and not 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0/1.2/1.3 GDEF glyph classes are supported.");
                    RequireTableLength(definitions, minor == 3 ? 18 : minor == 2 ? 14 : 12, "GDEF");
                    glyphClasses = GetTableOffset(definitions, definitions.Offset, ReadUInt16(data, definitions.Offset + 4), 4, "GDEF glyph classes");
                    ValidateGposClasses(data, definitions, glyphClasses, glyphCount, 5, workCounter, cancellationToken);
                }
            }
            else if (flags != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only unfiltered or mark-free IgnoreMarks GPOS kerning is supported.");
            int count = ReadUInt16(data, lookup + 4);
            if (count > MaximumKernSubtables) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS kern subtable count exceeds the admitted limit.");
            RequireTableRange(table, lookup, 6 + count * 2, "GPOS lookup");
            for (int index = 0; index < count; index++)
            {
                Consume(workCounter, cancellationToken);
                int subtable = GetTableOffset(table, lookup, ReadUInt16(data, lookup + 6 + index * 2), 4, "GPOS subtable");
                if (type == 2)
                {
                    ValidatePairSubtable(data, table, subtable, glyphCount, workCounter, cancellationToken);
                    if (firstPair == 0) firstPair = subtable;
                }
                else if (type == 8 && ReadUInt16(data, subtable) == 2)
                {
                    RequireTableRange(table, subtable, 12, "GPOS chained-context admission header");
                    int coverage = GetTableOffset(table, subtable, ReadUInt16(data, subtable + 2), 4, "GPOS chained-context coverage");
                    ValidateGposCoverage(data, table, coverage, glyphCount, workCounter, cancellationToken);
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "This GPOS kern lookup type or format is not supported.");
            }
        }
        return graph with { PairPosOffset = firstPair, Gdef = glyphClasses == 0 ? null : gdef, GlyphClassOffset = glyphClasses };

        static void ValidatePairSubtable(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, ushort glyphCount, ParserWorkCounter? workCounter, CancellationToken token)
        {
            RequireTableRange(table, offset, 10, "GPOS PairPos");
            int format = ReadUInt16(data, offset);
            if (format is not 1 and not 2 || ReadUInt16(data, offset + 4) != 4 || ReadUInt16(data, offset + 6) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GPOS pair-list/class first-glyph X-advance adjustments are supported.");
            int coverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 2), 4, "GPOS PairPos coverage");
            int coveredCount = ValidateGposCoverage(data, table, coverage, glyphCount, workCounter, token);
            if (format == 2)
            {
                RequireTableRange(table, offset, 16, "GPOS class pairs");
                int class1Count = ReadUInt16(data, offset + 12);
                int class2Count = ReadUInt16(data, offset + 14);
                if (class1Count == 0 || class2Count == 0) throw InvalidFont("GPOS pair classes must include class zero.");
                long length = 16L + 2L * class1Count * class2Count;
                if (length > int.MaxValue) throw InvalidFont("The GPOS class-pair matrix range is too large.");
                RequireTableRange(table, offset, (int)length, "GPOS class-pair matrix");
                workCounter?.ConsumeBytes((int)length, token);
                ValidateGposClasses(data, table, GetTableOffset(table, offset, ReadUInt16(data, offset + 8), 4, "GPOS first classes"), glyphCount, class1Count, workCounter, token);
                ValidateGposClasses(data, table, GetTableOffset(table, offset, ReadUInt16(data, offset + 10), 4, "GPOS second classes"), glyphCount, class2Count, workCounter, token);
                return;
            }
            int count = ReadUInt16(data, offset + 8);
            if (count != coveredCount) throw InvalidFont("The GPOS coverage and pair-set counts differ.");
            RequireTableRange(table, offset, 10 + count * 2, "GPOS PairPos");
            for (int index = 0; index < count; index++)
            {
                Consume(workCounter, token);
                int pairs = GetTableOffset(table, offset, ReadUInt16(data, offset + 10 + index * 2), 2, "GPOS PairSet");
                int pairCount = ReadUInt16(data, pairs);
                RequireTableRange(table, pairs, 2 + pairCount * 4, "GPOS PairSet");
                int previousGlyph = -1;
                for (int pair = 0; pair < pairCount; pair++)
                {
                    Consume(workCounter, token);
                    int glyph = ReadUInt16(data, pairs + 2 + pair * 4);
                    if (glyph >= glyphCount || glyph <= previousGlyph) throw InvalidFont("The GPOS PairSet glyphs are invalid.");
                    previousGlyph = glyph;
                }
            }
        }
    }

    /// <summary>Checks each distinct mapped glyph before layout, including the last or sole glyph; borrows validated, unchanged font bytes.</summary>
    internal static void ValidateGlyphPositioning(ReadOnlyMemory<byte> bytes, ushort glyph, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken token)
    {
        if (glyph >= metrics.GlyphCount) throw new ArgumentOutOfRangeException(nameof(glyph));
        if (metrics.Gpos is not OpenTypeGposPairTable graph) return;
        Consume(workCounter, token);
        ReadOnlySpan<byte> data = bytes.Span;
        if (graph.Gdef is OpenTypeTableRange && GetGposClass(data, graph.GlyphClassOffset, glyph, workCounter, token) == 3) throw new SvgException(SvgErrorCode.UnsupportedFeature, "GPOS mark positioning is not supported in direct text.");
        int previous = -1;
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, workCounter, token))
        {
            if (ReadUInt16(data, lookup) == 2) continue;
            int count = ReadUInt16(data, lookup + 4);
            for (int index = 0; index < count; index++)
            {
                Consume(workCounter, token);
                int subtable = GetTableOffset(graph.Range, lookup, ReadUInt16(data, lookup + 6 + index * 2), 4, "GPOS subtable");
                int coverage = GetTableOffset(graph.Range, subtable, ReadUInt16(data, subtable + 2), 4, "GPOS coverage");
                if (GetGposCoverageIndex(data, coverage, glyph, workCounter, token) >= 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The glyph requires unsupported contextual GPOS positioning.");
            }
        }
    }

    /// <summary>Accumulates ordered kerning lookups, stopping at the first matching subtable even when its adjustment is zero.</summary>
    internal static int GetPairAdjustment(ReadOnlyMemory<byte> bytes, ushort leftGlyphIndex, ushort rightGlyphIndex, OpenTypeFontMetrics metrics, ParserWorkCounter? workCounter, CancellationToken cancellationToken)
    {
        if (metrics.Gpos is not OpenTypeGposPairTable graph) return GetKerningAdjustment(bytes, leftGlyphIndex, rightGlyphIndex, metrics, workCounter, cancellationToken);
        ValidateGlyphPositioning(bytes, leftGlyphIndex, metrics, workCounter, cancellationToken);
        ValidateGlyphPositioning(bytes, rightGlyphIndex, metrics, workCounter, cancellationToken);
        ReadOnlySpan<byte> data = bytes.Span;
        int adjustment = 0;
        int previous = -1;
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, workCounter, cancellationToken))
        {
            if (ReadUInt16(data, lookup) != 2) continue;
            int count = ReadUInt16(data, lookup + 4);
            for (int index = 0; index < count; index++)
            {
                Consume(workCounter, cancellationToken);
                int subtable = GetTableOffset(graph.Range, lookup, ReadUInt16(data, lookup + 6 + index * 2), 10, "GPOS PairPos");
                int coverage = GetTableOffset(graph.Range, subtable, ReadUInt16(data, subtable + 2), 4, "GPOS coverage");
                int covered = GetGposCoverageIndex(data, coverage, leftGlyphIndex, workCounter, cancellationToken);
                if (covered < 0) continue;
                if (ReadUInt16(data, subtable) == 2)
                {
                    int class1 = GetGposClass(data, subtable + ReadUInt16(data, subtable + 8), leftGlyphIndex, workCounter, cancellationToken);
                    int class2 = GetGposClass(data, subtable + ReadUInt16(data, subtable + 10), rightGlyphIndex, workCounter, cancellationToken);
                    int class2Count = ReadUInt16(data, subtable + 14);
                    adjustment += ReadInt16(data, subtable + 16 + 2 * (class1 * class2Count + class2));
                    break;
                }
                int pairs = GetTableOffset(graph.Range, subtable, ReadUInt16(data, subtable + 10 + covered * 2), 2, "GPOS PairSet");
                int pairCount = ReadUInt16(data, pairs);
                int low = 0;
                int high = pairCount;
                while (low < high)
                {
                    Consume(workCounter, cancellationToken);
                    int mid = low + (high - low) / 2;
                    if (ReadUInt16(data, pairs + 2 + mid * 4) < rightGlyphIndex) low = mid + 1;
                    else high = mid;
                }
                if (low == pairCount || ReadUInt16(data, pairs + 2 + low * 4) != rightGlyphIndex) continue;
                adjustment += ReadInt16(data, pairs + 4 + low * 4);
                break;
            }
        }
        return adjustment;
    }

    // Feature lookup indices are a set; execution order comes from the LookupList.
    // At most 32 selections are scanned per step, with no heap-owned index array.
    private static bool TryGetNextKernLookup(ReadOnlySpan<byte> data, OpenTypeGposPairTable graph, ref int previous, out int offset, ParserWorkCounter? workCounter, CancellationToken token)
    {
        int next = int.MaxValue;
        int count = ReadUInt16(data, graph.FeatureOffset + 2);
        for (int index = 0; index < count; index++)
        {
            Consume(workCounter, token);
            int candidate = ReadUInt16(data, graph.FeatureOffset + 4 + index * 2);
            if (candidate > previous && candidate < next) next = candidate;
        }
        offset = 0;
        if (next == int.MaxValue) return false;
        previous = next;
        offset = GetTableOffset(graph.Range, graph.LookupListOffset, ReadUInt16(data, graph.LookupListOffset + 2 + next * 2), 6, "GPOS lookup");
        return true;
    }

    private static int ValidateGposCoverage(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, int glyphCount, ParserWorkCounter? workCounter, CancellationToken token)
    {
        int format = ReadUInt16(data, offset);
        int count = ReadUInt16(data, offset + 2);
        if (format is not 1 and not 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GPOS coverage formats 1 and 2 are supported.");
        RequireTableRange(table, offset, 4 + count * (format == 1 ? 2 : 6), "GPOS coverage");
        int previous = -1;
        int covered = 0;
        for (int index = 0; index < count; index++)
        {
            Consume(workCounter, token);
            int record = offset + 4 + index * (format == 1 ? 2 : 6);
            int start = ReadUInt16(data, record);
            int end = format == 1 ? start : ReadUInt16(data, record + 2);
            if (start <= previous || end < start || end >= glyphCount || format == 2 && ReadUInt16(data, record + 4) != covered) throw InvalidFont("The GPOS coverage glyphs or indices are invalid.");
            previous = end;
            covered += end - start + 1;
        }
        return covered;
    }

    private static int GetGposCoverageIndex(ReadOnlySpan<byte> data, int offset, ushort glyph, ParserWorkCounter? workCounter, CancellationToken token)
    {
        bool ranges = ReadUInt16(data, offset) == 2;
        int low = 0;
        int high = ReadUInt16(data, offset + 2);
        while (low < high)
        {
            Consume(workCounter, token);
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

    private static void ValidateGposClasses(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, int glyphCount, int classCount, ParserWorkCounter? workCounter, CancellationToken token)
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
            Consume(workCounter, token);
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

    private static int GetGposClass(ReadOnlySpan<byte> data, int offset, ushort glyph, ParserWorkCounter? workCounter, CancellationToken token)
    {
        Consume(workCounter, token);
        if (ReadUInt16(data, offset) == 1)
        {
            int relative = glyph - ReadUInt16(data, offset + 2);
            return relative >= 0 && relative < ReadUInt16(data, offset + 4) ? ReadUInt16(data, offset + 6 + relative * 2) : 0;
        }
        int low = 0;
        int high = ReadUInt16(data, offset + 2);
        while (low < high)
        {
            Consume(workCounter, token);
            int mid = low + (high - low) / 2;
            int record = offset + 4 + mid * 6;
            if (glyph < ReadUInt16(data, record)) high = mid;
            else if (glyph > ReadUInt16(data, record + 2)) low = mid + 1;
            else return ReadUInt16(data, record + 4);
        }
        return 0;
    }
}
