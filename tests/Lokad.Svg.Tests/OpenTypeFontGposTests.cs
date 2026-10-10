using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Buffers.Binary;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false, -300)]
    [InlineData(true, -300)]
    public void Gpos_class_pairs_use_coverage_lists_or_ranges(bool ranges, int expected)
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 0, CreateClassPairs(ranges, -300))], [0]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, null, CancellationToken.None));
        Assert.Equal(0, OpenTypeFont.GetPairAdjustment(font, 0, 1, metrics, null, CancellationToken.None));
        Assert.Equal(-50, OpenTypeFont.GetPairAdjustment(font, 1, 0, metrics, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-100, -100)]
    public void Gpos_first_matching_subtable_includes_zero_adjustment(short first, int expected)
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 0, CreatePairList(1, first), CreateClassPairs(false, -300))], [0]);
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, 1, 1, OpenTypeFont.ParseMetrics(font, CancellationToken.None), null, CancellationToken.None));
    }

    [Fact]
    public void Gpos_pair_list_missing_second_glyph_falls_through_to_class_pairs()
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 0, CreatePairList(0, -100), CreateClassPairs(true, -300))], [0]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(-300, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, null, CancellationToken.None));
        Assert.Equal(-100, OpenTypeFont.GetPairAdjustment(font, 1, 0, metrics, null, CancellationToken.None));
    }

    [Fact]
    public void Gpos_distinct_selected_lookups_accumulate_once_in_lookup_order()
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 0, CreatePairList(1, -300)), CreateKerningLookup(2, 0, CreateClassPairs(false, -50))], [1, 0, 1]);
        byte[] unchanged = font.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(-350, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, null, CancellationToken.None));
        Assert.Equal(unchanged, font);
    }

    [Fact]
    public async Task Gpos_dormant_context_does_not_poison_uncovered_glyphs_but_guards_a_single_glyph()
    {
        // Coverage alone is the conservative boundary; context bodies are never executed.
        ushort[] context = [2, 12, 0, 0, 0, 0, 1, 1, 0];
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(8, 0, context), CreateKerningLookup(2, 0, CreatePairList(1, -100))], [0, 1]);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        SvgPreparedFont prepared = Assert.Single(snapshot.Fonts);
        Assert.Single(SvgGlyphLayout.Layout(prepared, "A", 100, 0, 3, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ValidateGlyphPositioning(font, 0, prepared.Metrics, null, CancellationToken.None)).ErrorCode);

        byte[] covered = font.ToArray();
        OpenTypeGposPairTable graph = Assert.IsType<OpenTypeGposPairTable>(prepared.Metrics.Gpos);
        int lookup = graph.LookupListOffset + BinaryPrimitives.ReadUInt16BigEndian(covered.AsSpan(graph.LookupListOffset + 2));
        int subtable = lookup + BinaryPrimitives.ReadUInt16BigEndian(covered.AsSpan(lookup + 6));
        WriteUInt16(covered, subtable + 16, 1);
        using SvgFontSnapshot coveredSnapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(covered)], CancellationToken.None);
        RenderBudget budget = new(1_024);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(Assert.Single(coveredSnapshot.Fonts), "A", 100, 0, 3, SvgTextAnchor.Start, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Single(SvgGlyphLayout.Layout(prepared, "A", 100, 0, 3, CancellationToken.None));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Gpos_class_pairs_reject_invalid_ranges_indices_and_matrix_lengths(int defect)
    {
        ushort[] pairs = CreateClassPairs(true, -300);
        switch (defect)
        {
            case 1: pairs[20] = 2; break; // Class value outside class1Count.
            case 2: pairs[16] = 1; break; // Range coverage index does not begin at zero.
            case 3: pairs[7] = ushort.MaxValue; break; // Truncated matrix.
            case 4: pairs[15] = ushort.MaxValue; break; // Coverage glyph beyond maxp.
        }
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 0, pairs)], [0]);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(33, 1)]
    [InlineData(1, 33)]
    public void Gpos_lookup_and_subtable_limits_are_explicit(int lookupCount, int subtableCount)
    {
        ushort[][] subtables = Enumerable.Range(0, subtableCount).Select(_ => CreatePairList(1, 0)).ToArray();
        byte[] lookup = CreateKerningLookup(2, 0, subtables);
        byte[] font = CreateKerningGraphFont(Enumerable.Repeat(lookup, lookupCount).ToArray(), Enumerable.Range(0, lookupCount).Select(index => (ushort)index).ToArray());
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Gpos_parse_and_query_work_is_bounded_and_cancellable()
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 0, CreateClassPairs(true, -300))], [0]);
        ParserWorkCounter work = new(long.MaxValue);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, work, CancellationToken.None);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, new ParserWorkCounter(work.Used - 1), CancellationToken.None)).ErrorCode);
        ParserWorkCounter queryWork = new(long.MaxValue);
        Assert.Equal(-300, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, queryWork, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, new ParserWorkCounter(queryWork.Used - 1), CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.ValidateGlyphPositioning(font, 1, metrics, null, cancelled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, null, cancelled.Token));
    }

    [Fact]
    public void Gpos_IgnoreMarks_requires_definitions_and_rejects_actual_mark_runs()
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(2, 8, CreatePairList(1, -100))], [0]);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
        font = AddGlyphDefinitions(font, 1);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(-100, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, null, CancellationToken.None));
        WriteUInt16(font, font.Length - 2, 3);
        metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ValidateGlyphPositioning(font, 1, metrics, null, CancellationToken.None)).ErrorCode);

        static byte[] AddGlyphDefinitions(byte[] original, ushort glyphClass)
        {
            const int oldDirectory = 12 + 8 * 16;
            byte[] result = new byte[original.Length + 16 + 20];
            original.AsSpan(0, oldDirectory).CopyTo(result);
            original.AsSpan(oldDirectory).CopyTo(result.AsSpan(oldDirectory + 16));
            for (int index = 0; index < 8; index++)
            {
                int field = 12 + index * 16 + 8;
                WriteUInt32(result, field, BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(field)) + 16);
            }
            WriteUInt16(result, 4, 9);
            int gdef = original.Length + 16;
            WriteTable(result, 8, 0x47444546, gdef, 20);
            WriteUInt16(result, gdef, 1);
            WriteUInt16(result, gdef + 4, 12);
            WriteUInt16(result, gdef + 12, 1);
            WriteUInt16(result, gdef + 14, 1);
            WriteUInt16(result, gdef + 16, 1);
            WriteUInt16(result, gdef + 18, glyphClass);
            return result;
        }
    }

    private static ushort[] CreatePairList(ushort rightGlyph, short adjustment)
        => [1, 18, 4, 0, 1, 12, 1, rightGlyph, unchecked((ushort)adjustment), 1, 1, 1];

    private static ushort[] CreateClassPairs(bool rangeCoverage, short adjustment)
    {
        // Matrix rows: class0=(0,0), class1=(-50,adjustment).
        // ClassDef1 array assigns A to class1; ClassDef2 range does the same.
        ushort[] coverage = rangeCoverage ? [2, 1, 1, 1, 0] : [1, 1, 1];
        ushort[] result = new ushort[12 + coverage.Length + 4 + 5];
        ushort[] header = [2, 24, 4, 0, (ushort)(24 + coverage.Length * 2), (ushort)(32 + coverage.Length * 2), 2, 2, 0, 0, unchecked((ushort)-50), unchecked((ushort)adjustment)];
        header.CopyTo(result, 0);
        coverage.CopyTo(result, 12);
        ushort[] first = [1, 1, 1, 1];
        first.CopyTo(result, 12 + coverage.Length);
        ushort[] second = [2, 1, 1, 1, 1];
        second.CopyTo(result, 16 + coverage.Length);
        return result;
    }

    private static byte[] CreateKerningLookup(ushort type, ushort flags, params ushort[][] subtables)
    {
        byte[] result = new byte[6 + subtables.Length * 2 + subtables.Sum(subtable => subtable.Length * 2)];
        WriteUInt16(result, 0, type);
        WriteUInt16(result, 2, flags);
        WriteUInt16(result, 4, (ushort)subtables.Length);
        int cursor = 6 + subtables.Length * 2;
        for (int index = 0; index < subtables.Length; index++)
        {
            WriteUInt16(result, 6 + index * 2, (ushort)cursor);
            foreach (ushort word in subtables[index])
            {
                WriteUInt16(result, cursor, word);
                cursor += 2;
            }
        }
        return result;
    }

    private static byte[] CreateKerningGraphFont(byte[][] lookups, ushort[] selected)
    {
        byte[] font = CreateTriangleGposFont();
        OpenTypeGposPairTable old = Assert.IsType<OpenTypeGposPairTable>(OpenTypeFont.ParseMetrics(font, CancellationToken.None).Gpos);
        int lookupList = 42 + selected.Length * 2;
        int cursor = lookupList + 2 + lookups.Length * 2;
        int length = cursor + lookups.Sum(lookup => lookup.Length);
        Array.Resize(ref font, old.Range.Offset + length);
        font.AsSpan(old.Range.Offset + 38).Clear();
        WriteTable(font, 7, 0x47504F53, old.Range.Offset, length);
        WriteUInt16(font, old.Range.Offset + 8, (ushort)lookupList);
        WriteUInt16(font, old.Range.Offset + 40, (ushort)selected.Length);
        for (int index = 0; index < selected.Length; index++) WriteUInt16(font, old.Range.Offset + 42 + index * 2, selected[index]);
        WriteUInt16(font, old.Range.Offset + lookupList, (ushort)lookups.Length);
        for (int index = 0; index < lookups.Length; index++)
        {
            WriteUInt16(font, old.Range.Offset + lookupList + 2 + index * 2, (ushort)(cursor - lookupList));
            lookups[index].CopyTo(font, old.Range.Offset + cursor);
            cursor += lookups[index].Length;
        }
        return font;
    }
}
