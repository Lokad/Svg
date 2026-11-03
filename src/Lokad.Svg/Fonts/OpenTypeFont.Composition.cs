namespace Lokad.Svg.Fonts;

internal static partial class OpenTypeFont
{
    private const uint CompositionFeatureTag = 0x63636D70;
    private const int MaximumCompositionLookups = 32, MaximumCompositionSubtables = 32, MaximumContextRecords = 4096;
    private const int MaximumContextGlyphs = 8;

    /// <summary>Validates default-Latin/DFLT ccmp single, ligature and class-chained substitutions.</summary>
    /// <remarks>Borrows immutable font bytes and retains no storage. Selected lookups are unfiltered;
    /// chained rules call only unfiltered single substitutions, so nested calls cannot recurse or
    /// change sequence indices. Every traversal is bounded and observes cancellation.</remarks>
    internal static OpenTypeGsubCompositionTable? GetCanonicalSubstitutions(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, CancellationToken token)
    {
        if (metrics.Gsub is not {} table) return null;
        ReadOnlySpan<byte> data = bytes.Span;
        token.ThrowIfCancellationRequested();
        RequireTableLength(table, 10, "GSUB");
        if (ReadUInt32(data, table.Offset) != 0x10000) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0 GSUB tables are supported.");
        int scripts = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 4), 2, "GSUB ScriptList");
        int features = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 6), 2, "GSUB FeatureList");
        int lookups = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 8), 2, "GSUB LookupList");
        int feature = SelectLatinFeature(data, table, scripts, features, CompositionFeatureTag, "GSUB", token);
        if (feature == 0) return null;
        if (ReadUInt16(data, feature) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "GSUB ccmp feature parameters are not supported.");
        int selectedCount = ReadUInt16(data, feature + 2), lookupCount = ReadUInt16(data, lookups);
        if (selectedCount > MaximumCompositionLookups) throw new SvgException(SvgErrorCode.LimitExceeded, "The GSUB ccmp lookup count exceeds the admitted limit.");
        RequireTableRange(table, feature, 4 + selectedCount * 2, "GSUB ccmp feature");
        RequireTableRange(table, lookups, 2 + lookupCount * 2, "GSUB LookupList");
        for (int index = 0; index < selectedCount; index++)
        {
            token.ThrowIfCancellationRequested();
            if (ReadUInt16(data, feature + 4 + index * 2) >= lookupCount) throw InvalidFont("A GSUB ccmp lookup index is invalid.");
        }
        var graph = new OpenTypeGsubCompositionTable(table, feature, lookups);
        int previous = -1, records = 0;
        Span<int> validatedReferences = stackalloc int[MaximumCompositionLookups];
        int validatedReferenceCount = 0;
        while (TryGetNextSelectedLookup(data, table, lookups, [feature], ref previous, out int lookup, token))
        {
            int count = ValidateCompositionLookup(data, table, lookup);
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                GsubSubtable subtable = ReadGsubSubtable(data, table, lookup, index);
                if (subtable.Type == 1) ValidateSingleSubstitution(data, table, subtable.Offset, metrics.GlyphCount, token);
                else if (subtable.Type == 4) ValidateLigatureSubtable(data, table, subtable.Offset, metrics.GlyphCount, ref records, token);
                else if (subtable.Type == 6) ValidateClassContext(data, table, lookups, subtable.Offset, metrics.GlyphCount, ref records, validatedReferences, ref validatedReferenceCount, ContextActionKind.Substitution, token);
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "This GSUB ccmp lookup type is not supported.");
                if (index != 0 && subtable.Type != ReadGsubSubtable(data, table, lookup, 0).Type)
                    throw InvalidFont("GSUB extension subtables must have the same lookup type.");
            }
        }
        return graph;
    }

    private static int ValidateCompositionLookup(ReadOnlySpan<byte> data, OpenTypeTableRange table, int lookup)
    {
        if (ReadUInt16(data, lookup + 2) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only unfiltered canonical GSUB lookups are supported.");
        int count = ReadUInt16(data, lookup + 4);
        if (count > MaximumCompositionSubtables) throw new SvgException(SvgErrorCode.LimitExceeded, "The GSUB ccmp subtable count exceeds the admitted limit.");
        RequireTableRange(table, lookup, 6 + count * 2, "GSUB ccmp lookup");
        return count;
    }

    private static void ValidateSingleSubstitution(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, int glyphCount, CancellationToken token)
    {
        RequireTableRange(table, offset, 6, "GSUB SingleSubst");
        int format = ReadUInt16(data, offset);
        if (format is not 1 and not 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GSUB single-substitution formats 1 and 2 are supported.");
        int coverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 2), 4, "GSUB single coverage");
        int covered = ValidateGlyphCoverage(data, table, coverage, glyphCount, token);
        if (format == 2)
        {
            if (ReadUInt16(data, offset + 4) != covered) throw InvalidFont("GSUB single coverage and substitution counts differ.");
            RequireTableRange(table, offset, 6 + covered * 2, "GSUB single substitutes");
        }
        // Walk coverage once, including format-1 wrapped deltas. Uncovered
        // glyphs do not multiply validation cost by the font's glyph count.
        bool ranges = ReadUInt16(data, coverage) == 2;
        int rows = ReadUInt16(data, coverage + 2), substitution = 0;
        for (int row = 0; row < rows; row++)
        {
            token.ThrowIfCancellationRequested();
            int record = coverage + 4 + row * (ranges ? 6 : 2);
            int first = ReadUInt16(data, record), last = ranges ? ReadUInt16(data, record + 2) : first;
            for (int glyph = first; glyph <= last; glyph++, substitution++)
            {
                token.ThrowIfCancellationRequested();
                int replacement = format == 1 ? unchecked((ushort)(glyph + ReadInt16(data, offset + 4))) : ReadUInt16(data, offset + 6 + substitution * 2);
                if (replacement == 0 || replacement >= glyphCount) throw InvalidFont("A GSUB single substitution references an invalid glyph.");
            }
        }
    }

    /// <summary>Applies validated ccmp lookups in LookupList order and returns the surviving prefix length.</summary>
    /// <remarks>The caller owns and exclusively lends the mutable span. Compaction changes glyphs
    /// and source clusters together; it creates no storage. Parent attachment is a later stage.
    /// Unfiltered contexts never recurse or change their matched sequence indices.</remarks>
    internal static int ApplyCanonicalSubstitutions(ReadOnlyMemory<byte> bytes, OpenTypeGsubCompositionTable graph, Span<SvgShapingGlyph> glyphs, CancellationToken token)
    {
        ReadOnlySpan<byte> data = bytes.Span;
        int previous = -1;
        while (TryGetNextSelectedLookup(data, graph.Range, graph.LookupList, [graph.Feature], ref previous, out int lookup, token))
        {
            int count = ReadUInt16(data, lookup + 4);
            for (int index = 0; index < glyphs.Length;)
            {
                token.ThrowIfCancellationRequested();
                int progress = 1;
                for (int subtableIndex = 0; subtableIndex < count; subtableIndex++)
                {
                    GsubSubtable subtable = ReadGsubSubtable(data, graph.Range, lookup, subtableIndex);
                    if (subtable.Type == 1 && ApplySingle(data, subtable.Offset, ref glyphs[index], token)) break;
                    if (subtable.Type == 4 && ApplyLigature(data, subtable.Offset, ref glyphs, index, glyphs.Length, token)) break;
                    if (subtable.Type == 6 && ApplyClassContext(data, graph, subtable.Offset, glyphs, index, token, out progress)) break;
                }
                index += progress;
            }
        }
        return glyphs.Length;

        static bool ApplyClassContext(ReadOnlySpan<byte> data, OpenTypeGsubCompositionTable graph, int offset,
            Span<SvgShapingGlyph> glyphs, int index, CancellationToken token, out int progress)
        {
            progress = 1;
            if (!TryMatchClassContext(data, offset, glyphs, index, token, out int cursor, out int inputs)) return false;
            int substitutions = ReadUInt16(data, cursor);
            for (int action = 0; action < substitutions; action++)
            {
                token.ThrowIfCancellationRequested();
                int sequence = ReadUInt16(data, cursor + 2 + action * 4), selected = ReadUInt16(data, cursor + 4 + action * 4);
                int lookup = graph.LookupList + ReadUInt16(data, graph.LookupList + 2 + selected * 2), subtableCount = ReadUInt16(data, lookup + 4);
                for (int subtableIndex = 0; subtableIndex < subtableCount; subtableIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    GsubSubtable subtable = ReadGsubSubtable(data, graph.Range, lookup, subtableIndex);
                    if (ApplySingle(data, subtable.Offset, ref glyphs[index + sequence], token)) break;
                }
            }
            progress = inputs;
            return true;
        }
    }

    private static bool ApplySingle(ReadOnlySpan<byte> data, int offset, ref SvgShapingGlyph glyph, CancellationToken token)
    {
        int coverage = offset + ReadUInt16(data, offset + 2);
        int covered = GetGlyphCoverageIndex(data, coverage, glyph.GlyphIndex, token);
        if (covered < 0) return false;
        ushort replacement = ReadUInt16(data, offset) == 1 ? unchecked((ushort)(glyph.GlyphIndex + ReadInt16(data, offset + 4))) : ReadUInt16(data, offset + 6 + covered * 2);
        glyph = glyph with { GlyphIndex = replacement };
        return true;
    }

    /// <summary>Applies the existing unfiltered Latin liga graph to a borrowed canonical run.</summary>
    /// <remarks>Retains its Basic Latin letter restriction and font preference order. Returns the
    /// surviving prefix length without allocating storage; the caller truncates its owned list.</remarks>
    internal static int ApplyLatinLigatures(ReadOnlyMemory<byte> bytes, OpenTypeGsubLigatureTable graph, Span<SvgShapingGlyph> glyphs, CancellationToken token)
    {
        ReadOnlySpan<byte> data = bytes.Span;
        int count = ReadUInt16(data, graph.LookupOffset + 4);
        for (int index = 0; index < glyphs.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            int end = index;
            while (end < glyphs.Length && end - index < MaximumLigatureComponents
                && glyphs[end].Scalar.Value <= 127 && IsBasicLatinLetter((char)glyphs[end].Scalar.Value)) end++;
            if (end - index < 2) continue;
            for (int subtable = 0; subtable < count; subtable++)
            {
                token.ThrowIfCancellationRequested();
                int offset = graph.LookupOffset + ReadUInt16(data, graph.LookupOffset + 6 + subtable * 2);
                if (ApplyLigature(data, offset, ref glyphs, index, end, token)) break;
            }
        }
        return glyphs.Length;
    }

    private static bool ApplyLigature(ReadOnlySpan<byte> data, int offset, ref Span<SvgShapingGlyph> glyphs, int index, int end, CancellationToken token)
    {
        int coverage = offset + ReadUInt16(data, offset + 2);
        int covered = GetGlyphCoverageIndex(data, coverage, glyphs[index].GlyphIndex, token);
        if (covered < 0) return false;
        int set = offset + ReadUInt16(data, offset + 6 + covered * 2), count = ReadUInt16(data, set);
        for (int candidate = 0; candidate < count; candidate++)
        {
            token.ThrowIfCancellationRequested();
            int ligature = set + ReadUInt16(data, set + 2 + candidate * 2), components = ReadUInt16(data, ligature + 2);
            if (components > end - index) continue;
            int component = 1;
            for (; component < components; component++)
            {
                token.ThrowIfCancellationRequested();
                if (glyphs[index + component].GlyphIndex != ReadUInt16(data, ligature + 2 + component * 2)) break;
            }
            if (component != components) continue;
            SvgShapingGlyph first = glyphs[index];
            int sourceEnd = first.ClusterEnd, componentCount = 0;
            for (component = 0; component < components; component++)
            {
                token.ThrowIfCancellationRequested();
                SvgShapingGlyph part = glyphs[index + component];
                sourceEnd = Math.Max(sourceEnd, part.ClusterEnd);
                componentCount += Math.Max(1, part.LigatureComponents);
            }
            glyphs[index] = first with { GlyphIndex = ReadUInt16(data, ligature), ClusterEnd = sourceEnd, LigatureComponents = componentCount };
            for (int following = index + components; following < glyphs.Length; following++)
            {
                token.ThrowIfCancellationRequested();
                glyphs[following - components + 1] = glyphs[following];
            }
            glyphs = glyphs[..(glyphs.Length - components + 1)];
            return true;
        }
        return false;
    }



    private readonly record struct GsubSubtable(ushort Type, int Offset);

    private static GsubSubtable ReadGsubSubtable(ReadOnlySpan<byte> data, OpenTypeTableRange table, int lookup, int index)
    {
        ushort type = ReadUInt16(data, lookup);
        int offset = GetTableOffset(table, lookup, ReadUInt16(data, lookup + 6 + index * 2), 4, "GSUB subtable");
        if (type != 7) return new(type, offset);
        RequireTableRange(table, offset, 8, "GSUB extension header");
        if (ReadUInt16(data, offset) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-1 GSUB extensions are supported.");
        type = ReadUInt16(data, offset + 2);
        if (type is 0 or 7 or > 8) throw InvalidFont("A GSUB extension must reference a non-extension substitution type.");
        uint relative = ReadUInt32(data, offset + 4);
        if (relative < 8 || relative > int.MaxValue - offset) throw InvalidFont("The GSUB extension offset is outside its payload range.");
        return new(type, GetTableOffset(table, offset, (int)relative, 4, "GSUB extension payload"));
    }
}

/// <summary>Validated borrowed canonical-substitution offsets, without owned data or mutable shared state.</summary>
internal readonly record struct OpenTypeGsubCompositionTable(OpenTypeTableRange Range, int Feature, int LookupList);
