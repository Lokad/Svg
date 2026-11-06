namespace Lokad.Svg.Fonts;

internal static partial class OpenTypeFont
{
    private enum ContextValidation { WholeFeature, ActiveRun }

    /// <summary>Checks already admitted kern metadata for complete-run positioning work; allocates no storage.</summary>
    internal static bool HasContextualAdvances(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, CancellationToken token)
    {
        if (metrics.Gpos is not {} graph) return false;
        int previous = -1;
        while (TryGetNextKernLookup(bytes.Span, graph, ref previous, out int lookup, token))
            if (GetGposLookupType(bytes.Span, graph.Range, lookup) != 2) return true;
        return false;
    }

    /// <summary>Validates and applies selected unfiltered class-chained kern X-advance actions.</summary>
    /// <remarks>The caller lends a complete canonical font/style run before pair kerning or layout.
    /// GSUB and GPOS share the same bounded class-context matcher; positioning actions may call only
    /// format-1/2 SinglePos X-advance lookups and never recurse. Advances remain in font units. No
    /// material storage is allocated; validation and execution observe cancellation at every traversal.</remarks>
    internal static void ApplyContextualAdvances(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, Span<SvgShapingGlyph> glyphs, CancellationToken token)
        => ApplyContextualAdvancesCore(bytes, metrics, glyphs, ContextValidation.WholeFeature, token);

    /// <summary>Executes complete-run positioning while leaving uncovered context bodies dormant.</summary>
    /// <remarks>Selected coverage is already admitted by font parsing. Covered definitions retain full nested-action validation;
    /// raw scalar guards and strict whole-feature validation remain separate. Borrows mutable glyphs exclusively, owns no storage,
    /// observes cancellation and leaves cleanup of partial work to the caller's owner.</remarks>
    internal static void ApplyContextualAdvancesForRun(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, Span<SvgShapingGlyph> glyphs, CancellationToken token)
        => ApplyContextualAdvancesCore(bytes, metrics, glyphs, ContextValidation.ActiveRun, token);

    private static void ApplyContextualAdvancesCore(ReadOnlyMemory<byte> bytes, OpenTypeFontMetrics metrics, Span<SvgShapingGlyph> glyphs, ContextValidation validation, CancellationToken token)
    {
        if (metrics.Gpos is not {} graph) return;
        ReadOnlySpan<byte> data = bytes.Span;
        int previous = -1, records = 0, validatedCount = 0;
        Span<int> validated = stackalloc int[MaximumCompositionLookups];
        while (TryGetNextKernLookup(data, graph, ref previous, out int lookup, token))
        {
            if (GetGposLookupType(data, graph.Range, lookup) == 2) continue;
            ValidateContextPositioningLookup(data, graph.Range, lookup);
            int count = ReadUInt16(data, lookup + 4);
            for (int subtableIndex = 0; subtableIndex < count; subtableIndex++)
            {
                token.ThrowIfCancellationRequested();
                GposSubtable subtable = ReadGposSubtable(data, graph.Range, lookup, subtableIndex);
                if (subtable.Type != 8) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This contextual GPOS positioning type is not supported.");
                int coverage = GetTableOffset(graph.Range, subtable.Offset, ReadUInt16(data, subtable.Offset + 2), 4, "GPOS context input coverage");
                if (validation == ContextValidation.ActiveRun && !CoverageIntersectsRun(data, coverage, glyphs, token)) continue;
                ValidateClassContext(data, graph.Range, graph.LookupListOffset, subtable.Offset, metrics.GlyphCount,
                    ref records, validated, ref validatedCount, ContextActionKind.Advance, token);
            }
            for (int index = 0; index < glyphs.Length;)
            {
                token.ThrowIfCancellationRequested();
                int progress = 1;
                for (int subtableIndex = 0; subtableIndex < count; subtableIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    GposSubtable subtable = ReadGposSubtable(data, graph.Range, lookup, subtableIndex);
                    if (!TryMatchClassContext(data, subtable.Offset, glyphs, index, token, out int cursor, out int inputs)) continue;
                    int actions = ReadUInt16(data, cursor);
                    for (int action = 0; action < actions; action++)
                    {
                        token.ThrowIfCancellationRequested();
                        int sequence = ReadUInt16(data, cursor + 2 + action * 4), selected = ReadUInt16(data, cursor + 4 + action * 4);
                        int nested = graph.LookupListOffset + ReadUInt16(data, graph.LookupListOffset + 2 + selected * 2);
                        int nestedCount = ReadUInt16(data, nested + 4);
                        for (int nestedIndex = 0; nestedIndex < nestedCount; nestedIndex++)
                        {
                            token.ThrowIfCancellationRequested();
                            int offset = ReadGposSubtable(data, graph.Range, nested, nestedIndex).Offset;
                            int coverage = offset + ReadUInt16(data, offset + 2);
                            int covered = GetGlyphCoverageIndex(data, coverage, glyphs[index + sequence].GlyphIndex, token);
                            if (covered < 0) continue;
                            int adjustment = ReadInt16(data, offset + (ReadUInt16(data, offset) == 1 ? 6 : 8 + covered * 2));
                            SvgShapingGlyph glyph = glyphs[index + sequence];
                            glyphs[index + sequence] = glyph with { ContextAdvance = checked(glyph.ContextAdvance + adjustment) };
                            break;
                        }
                    }
                    progress = inputs;
                    break;
                }
                index += progress;
            }
        }
    }

    private static int ValidateContextPositioningLookup(ReadOnlySpan<byte> data, OpenTypeTableRange table, int lookup)
    {
        if ((ReadUInt16(data, lookup + 2) & ~1) != 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filtered contextual GPOS positioning is not supported.");
        int count = ReadUInt16(data, lookup + 4);
        if (count > MaximumKernSubtables) throw new SvgException(SvgErrorCode.LimitExceeded, "The GPOS kern subtable count exceeds the admitted limit.");
        RequireTableRange(table, lookup, 6 + count * 2, "GPOS context lookup");
        return count;
    }


}
