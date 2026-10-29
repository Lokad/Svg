using System.Buffers.Binary;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData("GposSkipBase")]
    [InlineData("GposSkipLig")]
    public async Task Gpos_filters_skip_partners_and_filter_starting_glyphs(string family)
    {
        byte[] bytes = ReadKerningFlagFont(family);
        byte[] unchanged = bytes.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        // Independent HarfBuzz vectors: V is ignored, A/W remain eligible.
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, "VAW AVW", 32, 16, 40, CancellationToken.None);
        Assert.Equal(new double[] { 16, 40, 60, 84, 92, 112, 136 }, glyphs.Select(glyph => glyph.X));
        Assert.Equal(0, OpenTypeFont.GetPairAdjustment(bytes, glyphs[0].GlyphIndex, glyphs[1].GlyphIndex, font.Metrics, CancellationToken.None));
        Assert.Equal(-128, OpenTypeFont.GetPairAdjustment(bytes, glyphs.AsSpan(4), font.Metrics, CancellationToken.None));
        Assert.Equal(unchanged, bytes);
    }

    [Theory]
    [InlineData("GposRtl")]
    [InlineData("GposMarks")]
    [InlineData("GposAttach")]
    [InlineData("GposSet")]
    [InlineData("GposCombined")]
    public async Task Gpos_pair_flags_admit_the_declared_nonmark_runs(string family)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont(family))], CancellationToken.None);
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(Assert.Single(snapshot.Fonts), "AV To W", 32, 16, 40, CancellationToken.None);
        Assert.Equal(new double[] { 16, 36, 60, 68, 90, 114, 122 }, glyphs.Select(glyph => glyph.X));
    }

    [Theory]
    [InlineData("GposMarks")]
    [InlineData("GposAttach")]
    [InlineData("GposSet")]
    [InlineData("GposCombined")]
    public async Task Gpos_mark_metadata_does_not_silently_enable_mark_positioning(string family)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont(family))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(16_384));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(font, "a", 32, 0, 40, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(3, SvgGlyphLayout.Layout(font, "AVW", 32, 0, 40, CancellationToken.None).Length);
    }

    [Theory]
    [InlineData("none", 36, 84)]
    [InlineData("font", 40, 88)]
    [InlineData("size", 40, 76)]
    [InlineData("absolute", 40, 96)]
    public async Task Gpos_filtered_lookahead_stops_at_later_font_size_and_absolute_boundaries(string boundary, double middleX, double lastX)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont("GposSkipBase"))], CancellationToken.None);
        using SvgFontSnapshot other = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont("GposRtl"))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        SvgPreparedFont otherFont = Assert.Single(other.Fonts);
        SvgTextResolvedFontSpan[] spans = boundary is "font" or "size"
            ? [new(0, 2, new(font, 32)), new(2, 3, new(boundary == "font" ? otherFont : font, boundary == "size" ? 16 : 32)), new(3, 4, new(font, 32))]
            : [];
        SvgTextPositionAdjustment[] adjustments = boundary == "absolute" ? [new(3, 96, null, 0, 0)] : [];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(16_384));
        using (SvgPositionedGlyphStorage storage = SvgGlyphLayout.CreateStorage(font, "AVTW", 32, 16, 40, 0, [], adjustments, [], spans, SvgTextAnchor.Start, null, budget, "label", CancellationToken.None))
        {
            Assert.Equal(middleX, storage.Glyphs[1].X);
            Assert.Equal(lastX, storage.Glyphs[3].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Gpos_each_lookup_filters_its_own_partner_and_selected_lookups_accumulate_once()
    {
        byte[] source = CreateKerningGraphFont([
            CreateKerningLookup(2, 2, CreatePairList(1, 0), CreateClassPairs(false, -300)),
            CreateKerningLookup(2, 0, CreatePairList(0, -25))], [1, 0, 1]);
        // .notdef is base class 1; A is ligature class 2. The first lookup skips
        // .notdef and matches zero on A, while the second pairs with .notdef.
        byte[] font = AppendTestFontTable(source, 0x47444546, [1, 0, 12, 0, 0, 0, 1, 0, 2, 1, 2]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        SvgPositionedGlyph[] glyphs = [new(1, 0, 0, 0), new(0, 0, 0, 0), new(1, 0, 0, 0)];
        Assert.Equal(-25, OpenTypeFont.GetPairAdjustment(font, glyphs, metrics, CancellationToken.None));
        int gpos = FindTestFontTable(font, 0x47504F53).Offset;
        int lookups = gpos + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(gpos + 8));
        int lookup = lookups + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(lookups + 2));
        int pairs = lookup + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(lookup + 6));
        WriteInt16(font, pairs + 16, -100);
        metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(-125, OpenTypeFont.GetPairAdjustment(font, glyphs, metrics, CancellationToken.None));
        Assert.Equal(-25, OpenTypeFont.GetPairAdjustment(font, 1, 0, metrics, CancellationToken.None));
    }

    [Theory]
    [InlineData(1, true, 2, 36, 84)]
    [InlineData(.75, false, 2, 36, 84)]
    [InlineData(.75, true, 2, 40, 88)]
    [InlineData(.75, true, 1, 40, 88)]
    [InlineData(1.5, true, 2, 40, 88)]
    public async Task Gpos_filtered_partners_observe_scaled_element_boundaries(double scale, bool changesElement, int offset, double middleX, double lastX)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(ReadKerningFlagFont("GposSkipBase"))], CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(16_384));
        var layout = SvgGlyphLayout.CreateStorageAndMetrics(Assert.Single(snapshot.Fonts), "AVTW", 32, 16, 40, 0, 0, false,
            [], [], [], [], [new(offset, changesElement)], SvgTextAnchor.Start,
            new(scale, SvgGlyphMetricTarget.Windows, false), budget, "label", CancellationToken.None);
        using (layout.Storage)
        {
            // Independent font vectors skip V/T while looking for W. Neither
            // the ignored glyphs nor a comment may hide an element boundary.
            Assert.Equal(middleX, layout.Storage.Glyphs[1].X);
            Assert.Equal(lastX, layout.Storage.Glyphs[3].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gpos_filtered_runs_retain_ligature_source_lengths_at_absolute_boundaries(bool absolute)
    {
        ushort[] pair = CreatePairList(2, -100);
        pair[^1] = 2; // Coverage is the ligature glyph, not its source A.
        byte[] pairs = CreateKerningGraphFont([CreateKerningLookup(2, 2, pair)], [0]);
        var table = FindTestFontTable(pairs, 0x47504F53);
        ushort[] words = Enumerable.Range(0, table.Length / 2).Select(index => BinaryPrimitives.ReadUInt16BigEndian(pairs.AsSpan(table.Offset + 2 * index))).ToArray();
        byte[] font = AppendTestFontTable(CreateLigatureFont(false), 0x47504F53, words);
        font = AppendTestFontTable(font, 0x47444546, [1, 0, 12, 0, 0, 0, 1, 0, 3, 1, 1, 2]);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        SvgTextPositionAdjustment[] adjustments = [new(3, 0, 0), absolute ? new(4, 100, null, 0, 0) : new(4, 0, 0)];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(16_384));
        using (SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAAAAAA", 20, 10, 40, 0, [], adjustments, SvgTextAnchor.Start, budget, string.Empty, CancellationToken.None))
        {
            // The selected GSUB graph prefers triples. Only the absolute reset
            // interrupts AAA; relative zero entries preserve both triples.
            Assert.Equal(absolute ? new ushort[] { 2, 1, 2 } : [2, 2, 1], positions.Glyphs.ToArray().Select(glyph => glyph.GlyphIndex));
            Assert.Equal(absolute ? 22 : 20, positions.Glyphs[1].X);
            Assert.Equal(absolute ? 100 : 32, positions.Glyphs[2].X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gpos_filtered_runs_count_supplementary_scalars_in_utf16_chunks(bool absolute)
    {
        ushort[] pair = CreatePairList(2, -128);
        pair[^1] = 2;
        byte[] source = CreateKerningGraphFont([CreateKerningLookup(2, 2, pair)], [0]);
        var table = FindTestFontTable(source, 0x47504F53);
        ushort[] words = Enumerable.Range(0, table.Length / 2).Select(index => BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(table.Offset + 2 * index))).ToArray();
        byte[] font = AppendTestFontTable(CreateFormat12Font(), 0x47504F53, words);
        font = AppendTestFontTable(font, 0x47444546, [1, 0, 12, 0, 0, 0, 1, 0, 3, 1, 1, 2]);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        SvgTextPositionAdjustment[] adjustments = absolute ? [new(2, 100, null, 0, 0)] : [];
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "😀😀😀", 32, 10, 40, 0, [], adjustments, SvgTextAnchor.Start, new RenderBudget(new HostMemoryBudget(16_384)), string.Empty, CancellationToken.None);
        Assert.Equal(3, positions.Glyphs.Length);
        Assert.Equal(absolute ? 100 : 24.75, positions.Glyphs[1].X);
        Assert.Equal(absolute ? 114.75 : 39.5, positions.Glyphs[2].X);
    }

    [Theory]
    [InlineData("GposSkipBase", "missing-gdef")]
    [InlineData("GposSkipLig", "missing-classes")]
    [InlineData("GposMarks", "bad-class")]
    [InlineData("GposAttach", "missing-attachment")]
    [InlineData("GposAttach", "bad-attachment")]
    [InlineData("GposSet", "old-version")]
    [InlineData("GposSet", "missing-sets")]
    [InlineData("GposSet", "bad-index")]
    [InlineData("GposSet", "bad-offset")]
    [InlineData("GposSet", "zero-offset")]
    [InlineData("GposSet", "bad-coverage")]
    [InlineData("GposSet", "truncated-field")]
    [InlineData("GposRtl", "reserved-flags")]
    public void Gpos_filters_reject_malformed_definitions_and_lookup_fields(string family, string defect)
    {
        byte[] font = ReadKerningFlagFont(family);
        var table = FindTestFontTable(font, 0x47504F53);
        int lookups = table.Offset + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(table.Offset + 8));
        int lookup = lookups + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(lookups + 2));
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(lookup + 4));
        var definitions = family == "GposRtl" ? default : FindTestFontTable(font, 0x47444546);
        int gdef = definitions.Offset;
        switch (defect)
        {
            case "missing-gdef": WriteUInt32(font, definitions.Record, 0x74657374); break;
            case "missing-classes": WriteUInt16(font, gdef + 4, 0); break;
            case "bad-class": CorruptClass(4, 5); break;
            case "missing-attachment": WriteUInt16(font, gdef + 10, 0); break;
            case "bad-attachment": CorruptClass(10, 256); break;
            case "old-version": WriteUInt16(font, gdef + 2, 0); break;
            case "missing-sets": WriteUInt16(font, gdef + 12, 0); break;
            case "bad-index": WriteUInt16(font, lookup + 6 + 2 * count, 1); break;
            case "bad-offset": WriteUInt32(font, MarkSets() + 4, uint.MaxValue); break;
            case "zero-offset": WriteUInt32(font, MarkSets() + 4, 0); break;
            case "bad-coverage":
                int sets = MarkSets();
                int coverage = sets + (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(sets + 4));
                Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(coverage)));
                WriteUInt16(font, coverage + 4, ushort.MaxValue);
                break;
            case "truncated-field": WriteUInt32(font, table.Record + 12, (uint)(lookup + 6 + count * 2 - table.Offset)); break;
            case "reserved-flags": WriteUInt16(font, lookup + 2, 0x20); break;
            default: throw new ArgumentException(defect, nameof(defect));
        }
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);

        void CorruptClass(int field, ushort value)
        {
            int classes = gdef + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(gdef + field));
            bool ranges = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(classes)) == 2;
            WriteUInt16(font, classes + (ranges ? 8 : 6), value);
        }
        int MarkSets() => gdef + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(gdef + 12));
    }

    [Theory]
    [InlineData(1, SvgErrorCode.UnsupportedFeature)]
    [InlineData(2, SvgErrorCode.UnsupportedFeature)]
    public void Filtered_contextual_lookups_keep_the_explicit_shaping_boundary(ushort flags, SvgErrorCode expected)
    {
        ushort[] context = [2, 12, 0, 0, 0, 0, 1, 1, 0];
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(8, flags, context)], [0]);
        if (flags == 1)
        {
            OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
            Assert.Equal(expected, Assert.Throws<SvgException>(() => OpenTypeFont.ValidateGlyphPositioning(font, 0, metrics, CancellationToken.None)).ErrorCode);
        }
        else Assert.Equal(expected, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Gpos_filtered_scans_preserve_tokens_and_release_layout_storage()
    {
        byte[] font = ReadKerningFlagFont("GposSkipBase");
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        SvgPreparedFont prepared = Assert.Single(snapshot.Fonts);
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(prepared, "AVVVW", 32, 0, 40, CancellationToken.None);
        Assert.Equal(-128, OpenTypeFont.GetPairAdjustment(font, glyphs, metrics, CancellationToken.None));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(16_384));
        using (SvgGlyphLayout.CreateStorage(prepared, "AVVVW", 32, 0, 40, SvgTextAnchor.Start, budget, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetPairAdjustment(font, glyphs, metrics, cancelled.Token)).CancellationToken);
        Assert.Equal(cancelled.Token, Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetPairAdjustment(font, ReadOnlySpan<SvgPositionedGlyph>.Empty, metrics, cancelled.Token)).CancellationToken);
        Assert.Equal(cancelled.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLayout.CreateStorage(prepared, "AVVVW", 32, 0, 40, budget, cancelled.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] ReadKerningFlagFont(string family)
        => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "kerning-flags", family + ".ttf"));

    private static (int Record, int Offset, int Length) FindTestFontTable(byte[] font, uint tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int index = 0; index < count; index++)
        {
            int record = 12 + index * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record)) == tag)
                return (record, (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12)));
        }
        throw new InvalidDataException("Missing test table.");
    }

    private static byte[] AppendTestFontTable(byte[] source, uint tag, ushort[] words)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(4));
        int directoryEnd = 12 + count * 16;
        int offset = source.Length + 16;
        byte[] result = new byte[offset + 2 * words.Length];
        source.AsSpan(0, directoryEnd).CopyTo(result);
        source.AsSpan(directoryEnd).CopyTo(result.AsSpan(directoryEnd + 16));
        for (int index = 0; index < count; index++)
        {
            int field = 12 + index * 16 + 8;
            WriteUInt32(result, field, BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(field)) + 16);
        }
        WriteUInt16(result, 4, (ushort)(count + 1));
        WriteTable(result, count, tag, offset, 2 * words.Length);
        for (int index = 0; index < words.Length; index++) WriteUInt16(result, offset + 2 * index, words[index]);
        return result;
    }
}
