namespace Lokad.Svg.Fonts;

using Lokad.Svg.Parsing;

internal static partial class OpenTypeFont
{
    private const uint LatinScriptTag = 0x6C61746E;
    private const uint LigatureFeatureTag = 0x6C696761;
    private const int MaximumLigatureComponents = 8;
    private const int MaximumLigatureSubtables = 32;
    private const int MaximumLigatureRecords = 4_096;

    /// <summary>
    /// Borrows the default Latin (or DFLT) liga feature. The finite subset is one
    /// unfiltered type-4 lookup, with ordered format-1 subtables and 2–8 components.
    /// Validation allocates no storage and observes caller cancellation during every traversal.
    /// Other scripts and language-specific features are not selected.
    /// </summary>
    internal static OpenTypeGsubLigatureTable? GetLatinLigatures(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, CancellationToken token)
    {
        if (metrics.Gsub is not OpenTypeTableRange table) return null;
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> data = bytes.Span;
        RequireTableLength(table, 10, "GSUB");
        if (ReadUInt16(data, table.Offset) != 1 || ReadUInt16(data, table.Offset + 2) != 0)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0 GSUB ligature tables are supported.");
        int scripts = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 4), 2, "GSUB ScriptList");
        int features = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 6), 2, "GSUB FeatureList");
        int lookups = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 8), 2, "GSUB LookupList");
        int feature = SelectLatinFeature(data, table, scripts, features, LigatureFeatureTag, "GSUB", token);
        if (feature == 0) return null;
        if (ReadUInt16(data, feature) != 0 || ReadUInt16(data, feature + 2) != 1)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "The Latin liga feature requires one lookup without feature parameters.");
        RequireTableRange(table, feature, 6, "GSUB liga feature");
        int lookupCount = ReadUInt16(data, lookups);
        RequireTableRange(table, lookups, 2 + lookupCount * 2, "GSUB LookupList");
        int lookupIndex = ReadUInt16(data, feature + 4);
        if (lookupIndex >= lookupCount) throw InvalidFont("The GSUB liga lookup index is invalid.");
        int lookup = GetTableOffset(table, lookups, ReadUInt16(data, lookups + 2 + lookupIndex * 2), 6, "GSUB liga lookup");
        if (ReadUInt16(data, lookup) != 4 || ReadUInt16(data, lookup + 2) != 0)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only an unfiltered type-4 Latin ligature lookup is supported.");
        int subtableCount = ReadUInt16(data, lookup + 4);
        if (subtableCount > MaximumLigatureSubtables) throw new SvgException(SvgErrorCode.LimitExceeded, "The GSUB ligature subtable count exceeds the admitted limit.");
        RequireTableRange(table, lookup, 6 + subtableCount * 2, "GSUB liga lookup");
        int recordCount = 0;
        for (int index = 0; index < subtableCount; index++)
        {
            token.ThrowIfCancellationRequested();
            int subtable = GetTableOffset(table, lookup, ReadUInt16(data, lookup + 6 + index * 2), 6, "GSUB ligature subtable");
            if (ReadUInt16(data, subtable) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-1 GSUB ligature substitution is supported.");
            int coverage = GetTableOffset(table, subtable, ReadUInt16(data, subtable + 2), 4, "GSUB ligature coverage");
            int coveredCount = ValidateGlyphCoverage(data, table, coverage, metrics.GlyphCount, token);
            int setCount = ReadUInt16(data, subtable + 4);
            if (setCount != coveredCount) throw InvalidFont("The GSUB ligature coverage and set counts differ.");
            RequireTableRange(table, subtable, 6 + setCount * 2, "GSUB ligature sets");
            for (int setIndex = 0; setIndex < setCount; setIndex++)
            {
                token.ThrowIfCancellationRequested();
                int set = GetTableOffset(table, subtable, ReadUInt16(data, subtable + 6 + setIndex * 2), 2, "GSUB LigatureSet");
                int count = ReadUInt16(data, set);
                recordCount += count;
                if (recordCount > MaximumLigatureRecords) throw new SvgException(SvgErrorCode.LimitExceeded, "The GSUB ligature record count exceeds the admitted limit.");
                RequireTableRange(table, set, 2 + count * 2, "GSUB LigatureSet");
                for (int ligatureIndex = 0; ligatureIndex < count; ligatureIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    int ligature = GetTableOffset(table, set, ReadUInt16(data, set + 2 + ligatureIndex * 2), 4, "GSUB Ligature");
                    int components = ReadUInt16(data, ligature + 2);
                    if (components < 2) throw InvalidFont("A GSUB ligature needs at least two components.");
                    if (components > MaximumLigatureComponents) throw new SvgException(SvgErrorCode.LimitExceeded, "The GSUB ligature component count exceeds the admitted limit.");
                    RequireTableRange(table, ligature, 4 + (components - 1) * 2, "GSUB Ligature");
                    for (int component = 0; component < components; component++)
                    {
                        token.ThrowIfCancellationRequested();
                        int glyph = ReadUInt16(data, ligature + (component == 0 ? 0 : 2 + component * 2));
                        if (glyph == 0 || glyph >= metrics.GlyphCount) throw InvalidFont("A GSUB ligature references an invalid glyph.");
                    }
                }
            }
        }
        return new OpenTypeGsubLigatureTable(lookup);
    }

    /// <summary>Matches contiguous Basic Latin letters within one spacing/position interval, in font preference order. Borrows all inputs; bounded and cancellable.</summary>
    internal static bool TryGetLatinLigature(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, OpenTypeGsubLigatureTable graph, string text, int start, int end, ushort firstGlyph, CancellationToken token, out ushort ligatureGlyph, out int sourceLength)
    {
        ligatureGlyph = 0;
        sourceLength = 0;
        if (!IsBasicLatinLetter(text[start])) return false;
        ReadOnlySpan<byte> data = bytes.Span;
        Span<ushort> followingGlyphs = stackalloc ushort[MaximumLigatureComponents - 1];
        int mappedCount = 0;
        int subtableCount = ReadUInt16(data, graph.LookupOffset + 4);
        for (int index = 0; index < subtableCount; index++)
        {
            token.ThrowIfCancellationRequested();
            int subtable = graph.LookupOffset + ReadUInt16(data, graph.LookupOffset + 6 + index * 2);
            int coverage = subtable + ReadUInt16(data, subtable + 2);
            int covered = GetGlyphCoverageIndex(data, coverage, firstGlyph, token);
            if (covered < 0) continue;
            int set = subtable + ReadUInt16(data, subtable + 6 + covered * 2);
            int count = ReadUInt16(data, set);
            for (int ligatureIndex = 0; ligatureIndex < count; ligatureIndex++)
            {
                token.ThrowIfCancellationRequested();
                int ligature = set + ReadUInt16(data, set + 2 + ligatureIndex * 2);
                int components = ReadUInt16(data, ligature + 2);
                if (components > end - start) continue;
                int component = 1;
                for (; component < components; component++)
                {
                    token.ThrowIfCancellationRequested();
                    char next = text[start + component];
                    if (!IsBasicLatinLetter(next)) break;
                    while (mappedCount < component)
                    {
                        followingGlyphs[mappedCount] = GetGlyphIndex(bytes, text[start + mappedCount + 1], metrics, token);
                        mappedCount++;
                    }
                    if (followingGlyphs[component - 1] != ReadUInt16(data, ligature + 2 + component * 2)) break;
                }
                if (component != components) continue;
                ligatureGlyph = ReadUInt16(data, ligature);
                sourceLength = components;
                return true;
            }
        }
        return false;
    }

    private static bool IsBasicLatinLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

/// <summary>Borrowed offsets in validated immutable font bytes; retains no owner or mutable shared state.</summary>
internal readonly record struct OpenTypeGsubLigatureTable(int LookupOffset);
