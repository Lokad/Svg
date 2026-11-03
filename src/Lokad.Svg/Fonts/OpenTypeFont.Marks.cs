namespace Lokad.Svg.Fonts;

internal static partial class OpenTypeFont
{
    private const uint MarkFeatureTag = 0x6D61726B, MarkToMarkFeatureTag = 0x6D6B6D6B;
    private const int MaximumMarkLookups = 32, MaximumMarkSubtables = 32, MaximumMarkClasses = 256;

    /// <summary>Validates selected default-Latin/DFLT mark features and their borrowed GDEF filters.</summary>
    /// <remarks>No font bytes or storage are owned or mutated. Only format-1 anchor coordinates
    /// and direct/extension mark-to-base, mark-to-ligature and mark-to-mark are admitted.
    /// Every bounded traversal observes the caller's cancellation token.</remarks>
    internal static OpenTypeGposMarkTable? GetMarkPositioning(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, CancellationToken token)
    {
        if (metrics.GposTable is not {} table) return null;
        token.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> data = bytes.Span;
        RequireTableLength(table, 10, "GPOS");
        if (ReadUInt32(data, table.Offset) != 0x10000) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0 GPOS tables are supported.");
        int scripts = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 4), 2, "GPOS ScriptList");
        int features = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 6), 2, "GPOS FeatureList");
        int lookups = GetTableOffset(table, table.Offset, ReadUInt16(data, table.Offset + 8), 2, "GPOS LookupList");
        int mark = SelectLatinFeature(data, table, scripts, features, MarkFeatureTag, "GPOS", token);
        int markToMark = SelectLatinFeature(data, table, scripts, features, MarkToMarkFeatureTag, "GPOS", token);
        if (mark == 0 && markToMark == 0) return null;
        int lookupCount = ReadUInt16(data, lookups);
        RequireTableRange(table, lookups, 2 + lookupCount * 2, "GPOS LookupList");
        ValidateFeature(data, table, mark, lookupCount, token);
        ValidateFeature(data, table, markToMark, lookupCount, token);
        if (metrics.GdefTable is not {} definitions) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Mark attachment requires GDEF glyph classes.");
        RequireTableLength(definitions, 12, "GDEF");
        int minor = ReadUInt16(data, definitions.Offset + 2);
        if (ReadUInt16(data, definitions.Offset) != 1 || minor is not 0 and not 2 and not 3)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only version-1.0/1.2/1.3 GDEF definitions are supported.");
        RequireTableLength(definitions, minor == 3 ? 18 : minor == 2 ? 14 : 12, "GDEF");
        int classes = ReadUInt16(data, definitions.Offset + 4);
        if (classes == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Mark attachment requires GDEF glyph classes.");
        classes = GetTableOffset(definitions, definitions.Offset, classes, 4, "GDEF glyph classes");
        ValidateGposClasses(data, definitions, classes, metrics.GlyphCount, 5, token);
        int markClasses = 0, markSets = 0;
        var graph = new OpenTypeGposMarkTable(table, mark, markToMark, lookups, classes, 0, 0);
        int previous = -1;
        while (TryGetNextMarkLookup(data, graph, ref previous, out int lookup, token))
        {
            int flags = ReadUInt16(data, lookup + 2), count = ReadUInt16(data, lookup + 4);
            if ((flags & 0xE0) != 0) throw InvalidFont("The GPOS lookup flags contain reserved bits.");
            if (count > MaximumMarkSubtables) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS mark subtable count exceeds the admitted limit.");
            RequireTableRange(table, lookup, 6 + count * 2 + ((flags & 16) != 0 ? 2 : 0), "GPOS mark lookup");
            if ((flags & 0xFF00) != 0 && markClasses == 0)
            {
                markClasses = GetTableOffset(definitions, definitions.Offset, ReadUInt16(data, definitions.Offset + 10), 4, "GDEF mark attachment classes");
                ValidateGposClasses(data, definitions, markClasses, metrics.GlyphCount, 256, token);
            }
            if ((flags & 16) != 0)
            {
                if (minor < 2) throw InvalidFont("GPOS mark filtering requires GDEF mark glyph sets.");
                if (markSets == 0)
                {
                    markSets = GetTableOffset(definitions, definitions.Offset, ReadUInt16(data, definitions.Offset + 12), 4, "GDEF mark glyph sets");
                    if (ReadUInt16(data, markSets) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-1 GDEF mark glyph sets are supported.");
                    RequireTableRange(definitions, markSets, 4 + ReadUInt16(data, markSets + 2) * 4, "GDEF mark glyph set offsets");
                }
                int selected = ReadUInt16(data, lookup + 6 + count * 2);
                if (selected >= ReadUInt16(data, markSets + 2)) throw InvalidFont("The GPOS mark filtering set index exceeds GDEF's set count.");
                uint relative = ReadUInt32(data, markSets + 4 + selected * 4);
                if (relative > int.MaxValue) throw InvalidFont("A GDEF mark coverage offset exceeds the supported range.");
                int coverage = GetTableOffset(definitions, markSets, (int)relative, 4, "GDEF mark glyph set coverage");
                ValidateGlyphCoverage(data, definitions, coverage, metrics.GlyphCount, token);
            }
            int type = GetGposLookupType(data, table, lookup);
            if (type is < 4 or > 6) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This GPOS mark lookup type is not supported.");
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                GposSubtable resolved = ReadGposSubtable(data, table, lookup, index);
                if (resolved.Type != type) throw InvalidFont("GPOS extension subtables must have the same lookup type.");
                ValidateMarkSubtable(data, table, resolved, metrics.GlyphCount, token);
            }
        }
        return graph with { MarkClasses = markClasses, MarkSets = markSets };

        static void ValidateFeature(ReadOnlySpan<byte> data, OpenTypeTableRange table, int feature, int lookupCount, CancellationToken token)
        {
            if (feature == 0) return;
            int count = ReadUInt16(data, feature + 2);
            if (ReadUInt16(data, feature) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "GPOS mark feature parameters are not supported.");
            if (count > MaximumMarkLookups) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS mark lookup count exceeds the admitted limit.");
            RequireTableRange(table, feature, 4 + count * 2, "GPOS mark feature");
            for (int index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                if (ReadUInt16(data, feature + 4 + index * 2) >= lookupCount) throw InvalidFont("A GPOS mark feature lookup index is invalid.");
            }
        }

        static void ValidateMarkSubtable(ReadOnlySpan<byte> data, OpenTypeTableRange table, GposSubtable subtable, int glyphCount, CancellationToken token)
        {
            int offset = subtable.Offset;
            RequireTableRange(table, offset, 12, "GPOS mark attachment");
            if (ReadUInt16(data, offset) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-1 GPOS mark attachment is supported.");
            int markCoverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 2), 4, "GPOS mark coverage");
            int parentCoverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 4), 4, "GPOS parent coverage");
            int marks = ValidateGlyphCoverage(data, table, markCoverage, glyphCount, token);
            int parents = ValidateGlyphCoverage(data, table, parentCoverage, glyphCount, token);
            int classCount = ReadUInt16(data, offset + 6);
            if (classCount == 0) throw InvalidFont("GPOS mark attachment needs at least one class.");
            if (classCount > MaximumMarkClasses) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS mark class count exceeds the admitted limit.");
            int markArray = GetTableOffset(table, offset, ReadUInt16(data, offset + 8), 2, "GPOS MarkArray");
            if (ReadUInt16(data, markArray) != marks) throw InvalidFont("The GPOS mark coverage and array counts differ.");
            RequireTableRange(table, markArray, 2 + marks * 4, "GPOS MarkArray");
            for (int index = 0; index < marks; index++)
            {
                token.ThrowIfCancellationRequested();
                if (ReadUInt16(data, markArray + 2 + index * 4) >= classCount) throw InvalidFont("A GPOS mark class is outside the attachment array.");
                int anchor = GetTableOffset(table, markArray, ReadUInt16(data, markArray + 4 + index * 4), 6, "GPOS mark anchor");
                ValidateAnchor(data, anchor);
            }
            int parentArray = GetTableOffset(table, offset, ReadUInt16(data, offset + 10), 2, "GPOS parent array");
            if (ReadUInt16(data, parentArray) != parents) throw InvalidFont("The GPOS parent coverage and array counts differ.");
            if (subtable.Type != 5)
            {
                ValidateMatrix(data, table, parentArray, parents, classCount, token);
                return;
            }
            RequireTableRange(table, parentArray, 2 + parents * 2, "GPOS LigatureArray");
            for (int index = 0; index < parents; index++)
            {
                token.ThrowIfCancellationRequested();
                int attachment = GetTableOffset(table, parentArray, ReadUInt16(data, parentArray + 2 + index * 2), 2, "GPOS LigatureAttach");
                int components = ReadUInt16(data, attachment);
                if (components == 0) throw InvalidFont("GPOS ligature attachment needs at least one component.");
                if (components > MaximumLigatureComponents) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS ligature component count exceeds the admitted limit.");
                ValidateMatrix(data, table, attachment, components, classCount, token);
            }

            static void ValidateMatrix(ReadOnlySpan<byte> data, OpenTypeTableRange table, int array, int rows, int columns, CancellationToken token)
            {
                RequireTableRange(table, array, checked(2 + rows * columns * 2), "GPOS parent anchors");
                for (int index = 0; index < rows * columns; index++)
                {
                    token.ThrowIfCancellationRequested();
                    int relative = ReadUInt16(data, array + 2 + index * 2);
                    if (relative == 0) continue;
                    int anchor = GetTableOffset(table, array, relative, 6, "GPOS parent anchor");
                    ValidateAnchor(data, anchor);
                }
            }

            static void ValidateAnchor(ReadOnlySpan<byte> data, int anchor)
            {
                if (ReadUInt16(data, anchor) != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-1 GPOS anchor coordinates are supported.");
            }
        }
    }



    /// <summary>Applies validated attachment relationships to one borrowed shaped run.</summary>
    /// <remarks>Parent indices refer to this span. Anchors stay in font units until layout resolves
    /// native metrics and fitting. Does not guess origins or change nominal advances. The caller owns
    /// the budgeted span and serializes mutation; every visited glyph/lookup observes cancellation.</remarks>
    internal static void AttachMarks(ReadOnlyMemory<byte> bytes, OpenTypeGposMarkTable graph, Span<SvgShapingGlyph> glyphs, CancellationToken token)
    {
        ReadOnlySpan<byte> data = bytes.Span;
        int previous = -1;
        while (TryGetNextMarkLookup(data, graph, ref previous, out int lookup, token))
        {
            int flags = ReadUInt16(data, lookup + 2), count = ReadUInt16(data, lookup + 4);
            int type = GetGposLookupType(data, graph.Range, lookup);
            for (int index = 0; index < glyphs.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                ushort mark = glyphs[index].GlyphIndex;
                if (IsMarkLookupIgnored(data, graph, lookup, mark, flags, token)) continue;
                for (int subtableIndex = 0; subtableIndex < count; subtableIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    int subtable = ReadGposSubtable(data, graph.Range, lookup, subtableIndex).Offset;
                    int coverage = subtable + ReadUInt16(data, subtable + 2);
                    int covered = GetGlyphCoverageIndex(data, coverage, mark, token);
                    if (covered < 0) continue;
                    if (GetGposClass(data, graph.GlyphClasses, mark, token) != 3)
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GDEF-classified marks may use mark attachment.");
                    int parent = index - 1;
                    while (parent >= 0)
                    {
                        token.ThrowIfCancellationRequested();
                        ushort glyph = glyphs[parent].GlyphIndex;
                        if (!IsMarkLookupIgnored(data, graph, lookup, glyph, flags, token)
                            && (type == 6 || GetGposClass(data, graph.GlyphClasses, glyph, token) != 3)) break;
                        parent--;
                    }
                    if (parent < 0) continue;
                    int parentClass = GetGposClass(data, graph.GlyphClasses, glyphs[parent].GlyphIndex, token);
                    if (type == 4 && parentClass != 1 || type == 5 && parentClass != 2 || type == 6 && parentClass != 3) continue;
                    int parentCoverage = subtable + ReadUInt16(data, subtable + 4);
                    int parentIndex = GetGlyphCoverageIndex(data, parentCoverage, glyphs[parent].GlyphIndex, token);
                    if (parentIndex < 0) continue;
                    int classCount = ReadUInt16(data, subtable + 6);
                    int marks = subtable + ReadUInt16(data, subtable + 8);
                    int markClass = ReadUInt16(data, marks + 2 + covered * 4);
                    int markAnchor = marks + ReadUInt16(data, marks + 4 + covered * 4);
                    int parents = subtable + ReadUInt16(data, subtable + 10);
                    int parentArray = parents, row = parentIndex;
                    if (type == 5)
                    {
                        parentArray = parents + ReadUInt16(data, parents + 2 + parentIndex * 2);
                        int components = ReadUInt16(data, parentArray);
                        // Unfiltered ligatures cannot consume a mark between
                        // components. A following mark belongs to the final one.
                        if (glyphs[parent].LigatureComponents != components) throw new SvgException(SvgErrorCode.UnsupportedFeature, "The mark's ligature component association is not supported.");
                        row = components - 1;
                    }
                    int relative = ReadUInt16(data, parentArray + 2 + (row * classCount + markClass) * 2);
                    if (relative == 0) continue;
                    int parentAnchor = parentArray + relative;
                    glyphs[index] = glyphs[index] with { Attachment = new(parent,
                        ReadInt16(data, parentAnchor + 2) - ReadInt16(data, markAnchor + 2),
                        ReadInt16(data, parentAnchor + 4) - ReadInt16(data, markAnchor + 4)) };
                    break;
                }
            }
        }
        for (int index = 0; index < glyphs.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (glyphs[index].Attachment.Parent < 0 && !glyphs[index].IsDefaultIgnorable && GetGposClass(data, graph.GlyphClasses, glyphs[index].GlyphIndex, token) == 3)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "GPOS mark positioning is not supported in direct text.");
        }

        static bool IsMarkLookupIgnored(ReadOnlySpan<byte> data, OpenTypeGposMarkTable graph, int lookup, ushort glyph, int flags, CancellationToken token)
            => IsLookupGlyphIgnored(data, new(graph.GlyphClasses, graph.MarkClasses, graph.MarkSets), lookup, glyph, flags, token);
    }



    private static bool IsLookupGlyphIgnored(ReadOnlySpan<byte> data, GposLookupFilters filters, int lookup, ushort glyph, int flags, CancellationToken token)
    {
        if (filters.GlyphClasses == 0) return false;
        int role = GetGposClass(data, filters.GlyphClasses, glyph, token);
        if (role == 1) return (flags & 2) != 0;
        if (role == 2) return (flags & 4) != 0;
        if (role != 3) return false;
        if ((flags & 8) != 0) return true;
        if ((flags & 16) != 0)
        {
            int selected = ReadUInt16(data, lookup + 6 + ReadUInt16(data, lookup + 4) * 2);
            int coverage = filters.MarkSets + (int)ReadUInt32(data, filters.MarkSets + 4 + selected * 4);
            return GetGlyphCoverageIndex(data, coverage, glyph, token) < 0;
        }
        return (flags & 0xFF00) != 0 && GetGposClass(data, filters.MarkClasses, glyph, token) != flags >> 8;
    }

    private readonly record struct GposLookupFilters(int GlyphClasses, int MarkClasses, int MarkSets);

    private static bool TryGetNextMarkLookup(ReadOnlySpan<byte> data, OpenTypeGposMarkTable graph, ref int previous, out int offset, CancellationToken token)
        => TryGetNextSelectedLookup(data, graph.Range, graph.LookupList, [graph.MarkFeature, graph.MarkToMarkFeature], ref previous, out offset, token);
}

/// <summary>Validated borrowed attachment features, glyph classes and filter offsets. Retains no owner or mutable cache.</summary>
internal readonly record struct OpenTypeGposMarkTable(OpenTypeTableRange Range, int MarkFeature, int MarkToMarkFeature,
    int LookupList, int GlyphClasses, int MarkClasses, int MarkSets);
