namespace Lokad.Svg.Fonts;

internal static partial class OpenTypeFont
{
    private enum ContextActionKind { Substitution, Advance }

    private static void ValidateClassContext(ReadOnlySpan<byte> data, OpenTypeTableRange table, int lookups, int offset, int glyphCount, ref int records,
        Span<int> validatedReferences, ref int validatedReferenceCount, ContextActionKind kind, CancellationToken token)
    {
        RequireTableRange(table, offset, 12, "OpenType chained context");
        if (ReadUInt16(data, offset) != 2) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only format-2 class-chained canonical substitutions are supported.");
        int coverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 2), 4, "OpenType context coverage");
        ValidateGlyphCoverage(data, table, coverage, glyphCount, token);
        int sets = ReadUInt16(data, offset + 10);
        if (sets > MaximumContextRecords) throw new SvgException(SvgErrorCode.LimitExceeded, "The OpenType context class count exceeds the admitted limit.");
        RequireTableRange(table, offset, 12 + sets * 2, "OpenType class context sets");
        for (int field = 4; field <= 8; field += 2)
        {
            int classes = GetTableOffset(table, offset, ReadUInt16(data, offset + field), 4, "OpenType context classes");
            ValidateGposClasses(data, table, classes, glyphCount, field == 6 ? sets : 65536, token);
        }
        for (int setIndex = 0; setIndex < sets; setIndex++)
        {
            token.ThrowIfCancellationRequested();
            int relative = ReadUInt16(data, offset + 12 + setIndex * 2);
            if (relative == 0) continue;
            int set = GetTableOffset(table, offset, relative, 2, "OpenType class context set");
            int count = ReadUInt16(data, set);
            records += count;
            if (records > MaximumContextRecords) throw new SvgException(SvgErrorCode.LimitExceeded, "The OpenType context record count exceeds the admitted limit.");
            RequireTableRange(table, set, 2 + count * 2, "OpenType class context rules");
            for (int ruleIndex = 0; ruleIndex < count; ruleIndex++)
            {
                token.ThrowIfCancellationRequested();
                int rule = GetTableOffset(table, set, ReadUInt16(data, set + 2 + ruleIndex * 2), 2, "OpenType class context rule");
                int cursor = rule, inputCount = 0;
                for (int part = 0; part < 3; part++)
                {
                    RequireTableRange(table, cursor, 2, "OpenType context sequence count");
                    int units = ReadUInt16(data, cursor);
                    if (part == 1)
                    {
                        if (units == 0) throw InvalidFont("A OpenType context input cannot be empty.");
                        inputCount = units;
                    }
                    if (units > MaximumContextGlyphs) throw new SvgException(SvgErrorCode.LimitExceeded, "The OpenType context sequence exceeds the admitted glyph limit.");
                    int bytes = 2 + (units - (part == 1 ? 1 : 0)) * 2;
                    RequireTableRange(table, cursor, bytes, "OpenType context classes");
                    cursor += bytes;
                }
                RequireTableRange(table, cursor, 2, "OpenType context substitution count");
                int substitutions = ReadUInt16(data, cursor);
                if (substitutions > MaximumCompositionLookups) throw new SvgException(SvgErrorCode.LimitExceeded, "The OpenType context substitution count exceeds the admitted limit.");
                RequireTableRange(table, cursor, 2 + substitutions * 4, "OpenType context substitutions");
                for (int index = 0; index < substitutions; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (ReadUInt16(data, cursor + 2 + index * 4) >= inputCount) throw InvalidFont("A OpenType context sequence index is invalid.");
                    int selected = ReadUInt16(data, cursor + 4 + index * 4);
                    if (selected >= ReadUInt16(data, lookups)) throw InvalidFont("A OpenType context lookup index is invalid.");
                    int nested = GetTableOffset(table, lookups, ReadUInt16(data, lookups + 2 + selected * 2), 6, "OpenType context lookup");
                    if (validatedReferences[..validatedReferenceCount].Contains(nested)) continue;
                    if (validatedReferenceCount == validatedReferences.Length) throw new SvgException(SvgErrorCode.LimitExceeded, "The OpenType context lookup count exceeds the admitted limit.");
                    int nestedCount = kind == ContextActionKind.Substitution ? ValidateCompositionLookup(data, table, nested) : ValidateContextPositioningLookup(data, table, nested);
                    for (int nestedIndex = 0; nestedIndex < nestedCount; nestedIndex++)
                    {
                        token.ThrowIfCancellationRequested();
                        (ushort Type, int Offset) subtable = ReadAction(data, table, nested, nestedIndex, kind);
                        if (subtable.Type != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Canonical OpenType contexts may call only single substitutions.");
                        if (kind == ContextActionKind.Substitution) ValidateSingleSubstitution(data, table, subtable.Offset, glyphCount, token);
                        else ValidateSingleAdvance(data, table, subtable.Offset, glyphCount, token);
                    }
                    validatedReferences[validatedReferenceCount++] = nested;
                }
            }
        }
        static (ushort Type, int Offset) ReadAction(ReadOnlySpan<byte> data, OpenTypeTableRange table, int lookup, int index, ContextActionKind kind)
        {
            if (kind == ContextActionKind.Substitution)
            {
                GsubSubtable subtable = ReadGsubSubtable(data, table, lookup, index);
                return (subtable.Type, subtable.Offset);
            }
            GposSubtable positioning = ReadGposSubtable(data, table, lookup, index);
            return (positioning.Type, positioning.Offset);
        }

        static void ValidateSingleAdvance(ReadOnlySpan<byte> data, OpenTypeTableRange table, int offset, int glyphCount, CancellationToken token)
        {
            RequireTableRange(table, offset, 8, "GPOS SinglePos");
            int format = ReadUInt16(data, offset);
            if (format is not 1 and not 2 || ReadUInt16(data, offset + 4) != 4)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only GPOS single X-advance adjustments are supported in class contexts.");
            int coverage = GetTableOffset(table, offset, ReadUInt16(data, offset + 2), 4, "GPOS single coverage");
            int covered = ValidateGlyphCoverage(data, table, coverage, glyphCount, token);
            if (format == 1) return;
            int count = ReadUInt16(data, offset + 6);
            if (count != covered) throw InvalidFont("The GPOS single coverage and adjustment counts differ.");
            RequireTableRange(table, offset, 8 + count * 2, "GPOS single advances");
        }
    }

    private static bool TryMatchClassContext(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<SvgShapingGlyph> glyphs,
        int index, CancellationToken token, out int actions, out int inputCount)
    {
        actions = 0; inputCount = 0;
        int coverage = offset + ReadUInt16(data, offset + 2);
        if (GetGlyphCoverageIndex(data, coverage, glyphs[index].GlyphIndex, token) < 0) return false;
        int inputClasses = offset + ReadUInt16(data, offset + 6);
        int inputClass = GetGposClass(data, inputClasses, glyphs[index].GlyphIndex, token);
        int relative = ReadUInt16(data, offset + 12 + inputClass * 2);
        if (relative == 0) return false;
        int set = offset + relative, count = ReadUInt16(data, set);
        for (int ruleIndex = 0; ruleIndex < count; ruleIndex++)
        {
            token.ThrowIfCancellationRequested();
            int cursor = set + ReadUInt16(data, set + 2 + ruleIndex * 2);
            int back = ReadUInt16(data, cursor); cursor += 2;
            bool matches = back <= index;
            int classes = offset + ReadUInt16(data, offset + 4);
            for (int part = 0; part < back && matches; part++)
            {
                token.ThrowIfCancellationRequested();
                matches &= GetGposClass(data, classes, glyphs[index - part - 1].GlyphIndex, token) == ReadUInt16(data, cursor + part * 2);
            }
            cursor += back * 2;
            int inputs = ReadUInt16(data, cursor); cursor += 2;
            matches &= inputs <= glyphs.Length - index;
            for (int part = 1; part < inputs && matches; part++)
            {
                token.ThrowIfCancellationRequested();
                matches &= GetGposClass(data, inputClasses, glyphs[index + part].GlyphIndex, token) == ReadUInt16(data, cursor + (part - 1) * 2);
            }
            cursor += (inputs - 1) * 2;
            int ahead = ReadUInt16(data, cursor); cursor += 2;
            matches &= ahead <= glyphs.Length - index - inputs;
            classes = offset + ReadUInt16(data, offset + 8);
            for (int part = 0; part < ahead && matches; part++)
            {
                token.ThrowIfCancellationRequested();
                matches &= GetGposClass(data, classes, glyphs[index + inputs + part].GlyphIndex, token) == ReadUInt16(data, cursor + part * 2);
            }
            cursor += ahead * 2;
            if (!matches) continue;
            actions = cursor; inputCount = inputs;
            return true;
        }
        return false;
    }
}
