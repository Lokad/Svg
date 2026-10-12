using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Fact]
    public void Validates_core_standalone_TrueType_metrics_without_copying_bytes()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);

        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal((ushort)1024, metrics.UnitsPerEm);
        Assert.Equal((ushort)3, metrics.GlyphCount);
        Assert.Equal((ushort)2, metrics.NumberOfHMetrics);
        Assert.Equal((short)1, metrics.IndexToLocFormat);
    }

    [Theory]
    [InlineData(0x74746366u, SvgErrorCode.UnsupportedFeature)]
    [InlineData(0x4F54544Fu, SvgErrorCode.UnsupportedFeature)]
    public void Rejects_unsupported_font_containers(uint scalerType, SvgErrorCode expectedCode)
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        WriteUInt32(font, 0, scalerType);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None));

        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_invalid_table_ranges_and_core_metrics()
    {
        byte[] range = CreateFont(1024, 3, 2, 0);
        WriteUInt32(range, 12 + 8, uint.MaxValue);
        byte[] metrics = CreateFont(8, 3, 2, 0);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(range, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(metrics, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Validates_optional_name_metadata_and_all_supported_OS2_versions()
    {
        foreach (ushort version in new ushort[] { 0, 1, 2, 3, 4, 5 })
        {
            OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(CreateFontWithMetadata(version), CancellationToken.None);

            Assert.Equal((ushort)3, metrics.GlyphCount);
        }
    }

    [Fact]
    public void Rejects_invalid_optional_name_and_OS2_metadata()
    {
        byte[] unsortedNames = CreateFontWithMetadata(5);
        int nameOffset = FindTableOffset(unsortedNames, 0x6E616D65);
        WriteUInt16(unsortedNames, nameOffset + 24, 0);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(unsortedNames, CancellationToken.None)).ErrorCode);

        byte[] invalidStorage = CreateFontWithMetadata(5);
        nameOffset = FindTableOffset(invalidStorage, 0x6E616D65);
        WriteUInt16(invalidStorage, nameOffset + 4, 35);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(invalidStorage, CancellationToken.None)).ErrorCode);

        byte[] missingLanguageTag = CreateFontWithMetadata(5);
        nameOffset = FindTableOffset(missingLanguageTag, 0x6E616D65);
        WriteUInt16(missingLanguageTag, nameOffset + 10, 0x8001);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(missingLanguageTag, CancellationToken.None)).ErrorCode);

        byte[] unsupportedOs2 = CreateFontWithMetadata(5);
        int os2Offset = FindTableOffset(unsupportedOs2, 0x4F532F32);
        WriteUInt16(unsupportedOs2, os2Offset, 6);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(unsupportedOs2, CancellationToken.None)).ErrorCode);

        byte[] truncatedOs2 = CreateFontWithMetadata(5);
        os2Offset = FindTableOffset(truncatedOs2, 0x4F532F32);
        WriteTable(truncatedOs2, 8, 0x4F532F32, os2Offset, 99);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(truncatedOs2, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Charges_name_records_language_tags_and_OS2_metadata_to_the_shared_work_limit()
    {
        byte[] font = CreateFontWithMetadata(5);

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, new ParserWorkCounter(13), CancellationToken.None)).ErrorCode);
        ParserWorkCounter successfulWork = new(14);
        OpenTypeFont.ParseMetrics(font, successfulWork, CancellationToken.None);

        Assert.Equal(14, successfulWork.Used);
    }

    [Fact]
    public void Checks_cancellation_before_font_parsing()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => OpenTypeFont.ParseMetrics(CreateFont(1024, 3, 2, 0), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void Observes_cancellation_between_SFNT_directory_records()
    {
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = FontMetricsProbe.Arm(cancellation.Cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.ParseMetrics(CreateFont(1024, 3, 2, 0), new ParserWorkCounter(100), cancellation.Token));
    }

    [Fact]
    public void Metrics_backed_font_readers_observe_cancellation_without_a_work_counter()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => OpenTypeFont.GetAdvanceWidth(font, 1, metrics, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => OpenTypeFont.GetGlyphDataRange(font, 1, metrics, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, cancellation.Token));
    }

    [Fact]
    public void Charges_glyph_location_lookups_to_the_shared_work_limit()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        SvgException rangeException = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphDataRange(font, 1, metrics, new ParserWorkCounter(0), CancellationToken.None));
        SvgException headerException = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphHeader(font, 1, metrics, new ParserWorkCounter(0), CancellationToken.None));
        OpenTypeGlyphHeader? header = OpenTypeFont.GetGlyphHeader(font, 1, metrics, new ParserWorkCounter(1), CancellationToken.None);

        Assert.Equal(SvgErrorCode.LimitExceeded, rangeException.ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, headerException.ErrorCode);
        Assert.True(header.HasValue);
    }

    [Fact]
    public void Active_composite_glyph_cycle_scans_consume_work_and_check_cancellation()
    {
        var workCounter = new ParserWorkCounter(1);
        using (var tracker = new OpenTypeGlyphRecursionTracker(3, null, workCounter, CancellationToken.None))
        {
            Assert.True(tracker.TryEnter(1));
            Assert.True(tracker.TryEnter(2));
            Assert.Equal(1, workCounter.Used);

            SvgException exception = Assert.Throws<SvgException>(() => tracker.TryEnter(3));

            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        }

        using CancellationTokenSource cancellation = new();
        using var cancelledTracker = new OpenTypeGlyphRecursionTracker(3, null, null, cancellation.Token);
        Assert.True(cancelledTracker.TryEnter(1));
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => cancelledTracker.TryEnter(2));
    }

    [Fact]
    public void Cmap_lookup_observes_cancellation_after_its_no_counter_entry_probe()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = CmapLookupProbe.Arm(cancellation.Cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetGlyphIndex(font, 'A', metrics, cancellation.Token));
    }

    [Fact]
    public void Maps_a_BMP_scalar_through_a_format_4_cmap()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);

        Assert.Equal((ushort)1, OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));
        Assert.Equal((ushort)0, OpenTypeFont.GetGlyphIndex(font, 'B', CancellationToken.None));
    }

    [Theory]
    [InlineData((uint)64, (ushort)0)]
    [InlineData((uint)65, (ushort)1)]
    [InlineData((uint)66, (ushort)0)]
    [InlineData((uint)0xFFFF, (ushort)0)]
    public void Maps_format_4_segment_boundary_scalars(uint scalar, ushort expectedGlyph)
    {
        Assert.Equal(expectedGlyph, OpenTypeFont.GetGlyphIndex(CreateFont(1024, 3, 2, 0), scalar, CancellationToken.None));
    }

    [Fact]
    public void Reuses_snapshot_validated_metrics_for_cmap_and_advance_queries()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        ushort glyph = OpenTypeFont.GetGlyphIndex(font, 'A', metrics, CancellationToken.None);

        Assert.Equal((ushort)1, glyph);
        Assert.Equal((ushort)600, OpenTypeFont.GetAdvanceWidth(font, glyph, metrics, CancellationToken.None));
    }

    [Fact]
    public async Task Text_layout_charges_cmap_and_advance_lookups_to_the_shared_work_counter()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 0))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        RenderBudget successfulBudget = new(1_024);
        var successfulWork = new ParserWorkCounter(9);

        using (SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(font, "A", 10, 0, 0, SvgTextAnchor.Start, successfulBudget, successfulWork, CancellationToken.None))
        {
            Assert.Single(glyphs.Glyphs.ToArray());
        }

        Assert.Equal(9, successfulWork.Used);
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(1_024);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(font, "A", 10, 0, 0, SvgTextAnchor.Start, deniedBudget, new ParserWorkCounter(8), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Text_layout_observes_cancellation_at_the_cmap_lookup_checkpoint()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 0))], CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = CmapLookupProbe.Arm(cancellation.Cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "A", 10, 0, 0, SvgTextAnchor.Start, new RenderBudget(1_024), new ParserWorkCounter(100), cancellation.Token));
    }

    [Fact]
    public async Task Text_layout_reuses_the_cmap_lookup_for_repeated_scalars_within_one_run()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 0))], CancellationToken.None);
        int lookupCount = 0;
        using IDisposable probe = CmapLookupProbe.Arm(() => lookupCount++);
        RenderBudget exactBudget = new(1_024);
        var exactWork = new ParserWorkCounter(18);

        using (SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAAA", 10, 0, 0, SvgTextAnchor.Start, exactBudget, exactWork, CancellationToken.None))
        {
            Assert.Equal(1, lookupCount);
            Assert.Equal(18, exactWork.Used);
            Assert.All(glyphs.Glyphs.ToArray(), glyph => Assert.Equal((ushort)1, glyph.GlyphIndex));
        }
        Assert.Equal(0, exactBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(1_024);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAAA", 10, 0, 0, SvgTextAnchor.Start, deniedBudget, new ParserWorkCounter(17), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Maps_a_supplementary_scalar_through_a_format_12_cmap()
    {
        byte[] font = CreateFormat12Font();

        Assert.Equal((ushort)2, OpenTypeFont.GetGlyphIndex(font, 0x1F600, CancellationToken.None));
        Assert.Equal((ushort)0, OpenTypeFont.GetGlyphIndex(font, 0x1F601, CancellationToken.None));
    }

    [Theory]
    [InlineData(0x1F5FFu, (ushort)0)]
    [InlineData(0x1F600u, (ushort)2)]
    [InlineData(0x1F601u, (ushort)0)]
    public void Maps_format_12_group_boundary_scalars(uint scalar, ushort expectedGlyph)
    {
        Assert.Equal(expectedGlyph, OpenTypeFont.GetGlyphIndex(CreateFormat12Font(), scalar, CancellationToken.None));
    }

    [Fact]
    public void Maps_a_BMP_scalar_through_a_format_12_cmap_when_format_4_is_absent()
    {
        byte[] font = CreateFormat12Font();
        WriteUInt32(font, 258, 'A');
        WriteUInt32(font, 262, 'A');
        WriteUInt32(font, 266, 1);

        Assert.Equal((ushort)1, OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));
    }

    [Fact]
    public void Accepts_a_unicode_platform_zero_format_4_cmap_but_not_a_windows_symbol_cmap()
    {
        byte[] unicode = CreateFont(1024, 3, 2, 0);
        WriteUInt16(unicode, 234, 0);
        byte[] symbol = CreateFont(1024, 3, 2, 0);
        WriteUInt16(symbol, 236, 0);

        Assert.Equal((ushort)1, OpenTypeFont.GetGlyphIndex(unicode, 'A', CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(symbol, 'A', CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Rejects_a_surrogate_code_point_as_a_non_scalar_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenTypeFont.GetGlyphIndex(CreateFont(1024, 3, 2, 0), 0xD800, CancellationToken.None));
    }

    [Fact]
    public void Rejects_a_format_12_cmap_with_a_length_smaller_than_its_header()
    {
        byte[] font = CreateFormat12Font();
        WriteUInt32(font, 246, 0);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 0x1F600, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    [InlineData((ushort)5)]
    [InlineData((ushort)6)]
    [InlineData((ushort)7)]
    [InlineData((ushort)8)]
    [InlineData((ushort)9)]
    [InlineData((ushort)10)]
    [InlineData((ushort)11)]
    [InlineData((ushort)12)]
    [InlineData((ushort)13)]
    [InlineData((ushort)14)]
    [InlineData((ushort)15)]
    public void Rejects_every_format_4_declared_length_smaller_than_its_header(ushort length)
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        const int Subtable = 242;
        WriteUInt16(font, Subtable + 2, length);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_a_format_4_declared_length_that_truncates_its_segment_arrays()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        const int Subtable = 242;
        WriteUInt16(font, Subtable + 2, 31);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_a_format_4_range_offset_that_starts_after_its_declared_payload()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        const int Subtable = 242;
        WriteUInt16(font, Subtable + 28, 4);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Categorizes_a_format_12_glyph_index_overflow_as_malformed_input()
    {
        byte[] font = CreateFormat12Font();
        WriteUInt32(font, 262, 0x1F601);
        WriteUInt32(font, 266, uint.MaxValue);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 0x1F601, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_a_later_malformed_format_12_group_even_when_an_earlier_group_matches()
    {
        byte[] font = CreateFormat12FontWithLaterMalformedGroup();

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_a_format_4_range_offset_that_points_before_its_glyph_index_array()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);
        const int Subtable = 242;
        WriteUInt16(font, Subtable + 28, 2);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public void Rejects_malformed_format_4_segments_even_when_the_requested_scalar_bypasses_them()
    {
        byte[] unordered = CreateFont(1024, 3, 2, 0);
        byte[] truncatedRange = CreateFont(1024, 3, 2, 0);
        const int Subtable = 242;
        WriteUInt16(unordered, Subtable + 20, 66);
        WriteUInt16(truncatedRange, Subtable + 28, ushort.MaxValue);

        SvgException unorderedException = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(unordered, 'B', CancellationToken.None));
        SvgException truncatedRangeException = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphIndex(truncatedRange, 'B', CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, unorderedException.ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, truncatedRangeException.ErrorCode);
    }

    [Fact]
    public void Reads_hmtx_advances_including_the_final_shared_advance()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);

        Assert.Equal((ushort)500, OpenTypeFont.GetAdvanceWidth(font, 0, CancellationToken.None));
        Assert.Equal((ushort)600, OpenTypeFont.GetAdvanceWidth(font, 1, CancellationToken.None));
        Assert.Equal((ushort)600, OpenTypeFont.GetAdvanceWidth(font, 2, CancellationToken.None));
    }

    [Fact]
    public void Validates_loca_ranges_before_exposing_borrowed_glyf_bytes()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);

        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);

        Assert.Equal(32, glyph.Length);
        font[glyph.Offset] = 42;
        Assert.Equal((byte)42, font[glyph.Offset]);
    }

    [Fact]
    public void Reads_the_fixed_header_of_a_nonempty_glyph()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset, 1);
        WriteInt16(font, glyph.Offset + 2, -2);
        WriteInt16(font, glyph.Offset + 4, -3);
        WriteInt16(font, glyph.Offset + 6, 4);
        WriteInt16(font, glyph.Offset + 8, 5);

        OpenTypeGlyphHeader? parsed = OpenTypeFont.GetGlyphHeader(font, 1, CancellationToken.None);
        Assert.True(parsed.HasValue);
        OpenTypeGlyphHeader header = parsed.Value;

        Assert.Equal((short)1, header.ContourCount);
        Assert.Equal((short)-2, header.XMinimum);
        Assert.Equal((short)5, header.YMaximum);
        Assert.False(header.IsComposite);
        Assert.Null(OpenTypeFont.GetGlyphHeader(font, 0, CancellationToken.None));
    }

    [Fact]
    public void Reads_simple_glyph_contours_without_retaining_hint_instructions()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset, 1);
        WriteUInt16(font, glyph.Offset + 10, 2);
        WriteUInt16(font, glyph.Offset + 12, 2);
        font[glyph.Offset + 14] = 1;
        font[glyph.Offset + 15] = 2;

        OpenTypeSimpleGlyphStructure structure = OpenTypeFont.GetSimpleGlyphStructure(font, 1, CancellationToken.None);

        Assert.Equal([2], structure.ContourEndpoints);
        Assert.Equal(16, structure.PointDataOffset);
    }

    [Fact]
    public void Decodes_simple_glyph_coordinate_deltas_and_on_curve_flags()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset, 1);
        WriteUInt16(font, glyph.Offset + 10, 1);
        WriteUInt16(font, glyph.Offset + 12, 0);
        font[glyph.Offset + 14] = 0x3F;
        font[glyph.Offset + 15] = 0x01;
        font[glyph.Offset + 16] = 10;
        font[glyph.Offset + 17] = 0;
        font[glyph.Offset + 18] = 3;
        font[glyph.Offset + 19] = 0;

        OpenTypeGlyphPoint[] points = OpenTypeFont.GetSimpleGlyphPoints(font, 1, CancellationToken.None);

        Assert.Equal([new OpenTypeGlyphPoint(10, 3, true), new OpenTypeGlyphPoint(10, 3, true)], points);
    }

    [Fact]
    public void Builds_quadratic_contours_with_implied_on_curve_midpoints()
    {
        OpenTypeGlyphPoint[] points = [new(0, 0, true), new(4, 4, false), new(8, 0, false), new(12, 0, true)];

        OpenTypeQuadraticSegment[] segments = OpenTypeFont.BuildQuadraticContours(points, [3], null, CancellationToken.None);

        Assert.Equal(new OpenTypeQuadraticSegment(new(0, 0), new(4, 4), new(6, 2)), segments[0]);
        Assert.Equal(new OpenTypeQuadraticSegment(new(6, 2), new(8, 0), new(12, 0)), segments[1]);
        Assert.Equal(new OpenTypeQuadraticSegment(new(12, 0), new(0, 0), new(0, 0)), segments[2]);
    }

    [Fact]
    public void Builds_quadratic_contours_for_degenerate_point_configurations()
    {
        Assert.Empty(OpenTypeFont.BuildQuadraticContours([new OpenTypeGlyphPoint(3, 7, true)], [0], null, CancellationToken.None));

        OpenTypeQuadraticSegment singleOffCurve = Assert.Single(OpenTypeFont.BuildQuadraticContours([new OpenTypeGlyphPoint(3, 7, false)], [0], null, CancellationToken.None));

        Assert.Equal(new OpenTypeQuadraticSegment(new(3, 7), new(3, 7), new(3, 7)), singleOffCurve);
        Assert.Equal([new OpenTypeQuadraticSegment(new(2, 0), new(0, 0), new(2, 0)), new OpenTypeQuadraticSegment(new(2, 0), new(4, 0), new(2, 0))], OpenTypeFont.BuildQuadraticContours([new OpenTypeGlyphPoint(0, 0, false), new OpenTypeGlyphPoint(4, 0, false)], [1], null, CancellationToken.None));
        Assert.Equal([new OpenTypeQuadraticSegment(new(2, 2), new(0, 0), new(2, 0)), new OpenTypeQuadraticSegment(new(2, 0), new(4, 0), new(4, 2)), new OpenTypeQuadraticSegment(new(4, 2), new(4, 4), new(2, 2))], OpenTypeFont.BuildQuadraticContours([new OpenTypeGlyphPoint(0, 0, false), new OpenTypeGlyphPoint(4, 0, false), new OpenTypeGlyphPoint(4, 4, false)], [2], null, CancellationToken.None));
    }

    [Fact]
    public void Segment_building_charges_contour_and_point_records()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset, 1);
        WriteUInt16(font, glyph.Offset + 10, 1);
        WriteUInt16(font, glyph.Offset + 12, 0);
        font[glyph.Offset + 14] = 0x37;
        font[glyph.Offset + 15] = 0x37;
        font[glyph.Offset + 16] = 10;
        font[glyph.Offset + 17] = 5;
        font[glyph.Offset + 18] = 3;
        font[glyph.Offset + 19] = 0;
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        ParserWorkCounter prefix = new(long.MaxValue);
        OpenTypeFont.GetSimpleGlyphPoints(font, 1, metrics, prefix, CancellationToken.None);
        ParserWorkCounter total = new(long.MaxValue);
        OpenTypeQuadraticSegment[] segments = OpenTypeFont.GetSimpleGlyphContours(font, 1, metrics, total, CancellationToken.None);

        Assert.True(total.Used > prefix.Used);
        ParserWorkCounter denied = new(prefix.Used);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetSimpleGlyphContours(font, 1, metrics, denied, CancellationToken.None)).ErrorCode);
        ParserWorkCounter exact = new(total.Used);
        Assert.Equal(segments, OpenTypeFont.GetSimpleGlyphContours(font, 1, metrics, exact, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetSimpleGlyphContours(font, 1, metrics, new ParserWorkCounter(total.Used - 1), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Combines_simple_glyph_data_into_quadratic_contours()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset, 1);
        WriteUInt16(font, glyph.Offset + 10, 1);
        WriteUInt16(font, glyph.Offset + 12, 0);
        font[glyph.Offset + 14] = 0x37;
        font[glyph.Offset + 15] = 0x37;
        font[glyph.Offset + 16] = 10;
        font[glyph.Offset + 17] = 5;
        font[glyph.Offset + 18] = 3;
        font[glyph.Offset + 19] = 0;

        OpenTypeQuadraticSegment[] segments = OpenTypeFont.GetSimpleGlyphContours(font, 1, CancellationToken.None);

        Assert.Equal([new OpenTypeQuadraticSegment(new(10, 3), new(15, 3), new(15, 3)), new OpenTypeQuadraticSegment(new(15, 3), new(10, 3), new(10, 3))], segments);
    }

    [Fact]
    public void Rejects_truncated_simple_glyph_flags_and_invalid_contour_endpoints()
    {
        byte[] truncated = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange truncatedGlyph = OpenTypeFont.GetGlyphDataRange(truncated, 1, CancellationToken.None);
        WriteInt16(truncated, truncatedGlyph.Offset, 1);
        WriteUInt16(truncated, truncatedGlyph.Offset + 10, 1);
        WriteUInt16(truncated, truncatedGlyph.Offset + 12, 15);
        byte[] invalid = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange invalidGlyph = OpenTypeFont.GetGlyphDataRange(invalid, 1, CancellationToken.None);
        WriteInt16(invalid, invalidGlyph.Offset, 2);
        WriteUInt16(invalid, invalidGlyph.Offset + 10, 2);
        WriteUInt16(invalid, invalidGlyph.Offset + 12, 1);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetSimpleGlyphPoints(truncated, 1, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetSimpleGlyphStructure(invalid, 1, CancellationToken.None)).ErrorCode);
        OpenTypeFontMetrics truncatedMetrics = OpenTypeFont.ParseMetrics(truncated, CancellationToken.None);
        OpenTypeFontMetrics invalidMetrics = OpenTypeFont.ParseMetrics(invalid, CancellationToken.None);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(truncated, 1, truncatedMetrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(truncated, 1, truncatedMetrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(invalid, 1, invalidMetrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(invalid, 1, invalidMetrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    public enum SimpleGlyphPointDefect
    {
        TruncatedFlags,
        FlagRepeatOverflow,
        TruncatedCoordinates,
        TruncatedRepetition,
    }

    [Theory]
    [InlineData(SimpleGlyphPointDefect.TruncatedFlags)]
    [InlineData(SimpleGlyphPointDefect.FlagRepeatOverflow)]
    [InlineData(SimpleGlyphPointDefect.TruncatedCoordinates)]
    [InlineData(SimpleGlyphPointDefect.TruncatedRepetition)]
    public void Rejects_malformed_simple_glyph_point_data_on_both_decode_paths(SimpleGlyphPointDefect defect)
    {
        byte[] font = BuildMalformedPointFont(defect);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);

        byte[] BuildMalformedPointFont(SimpleGlyphPointDefect kind)
        {
            byte[] malformed = CreateFont(1024, 3, 2, 1);
            OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(malformed, 1, CancellationToken.None);
            WriteInt16(malformed, range.Offset, 1);
            if (kind is SimpleGlyphPointDefect.TruncatedFlags)
            {
                WriteUInt16(malformed, range.Offset + 10, 1);
                WriteUInt16(malformed, range.Offset + 12, 17);
            }
            else if (kind is SimpleGlyphPointDefect.FlagRepeatOverflow)
            {
                WriteUInt16(malformed, range.Offset + 10, 0);
                WriteUInt16(malformed, range.Offset + 12, 0);
                malformed[range.Offset + 14] = 0x08;
                malformed[range.Offset + 15] = 5;
            }
            else if (kind is SimpleGlyphPointDefect.TruncatedCoordinates)
            {
                WriteUInt16(malformed, range.Offset + 10, 0);
                WriteUInt16(malformed, range.Offset + 12, 15);
            }
            else
            {
                WriteUInt16(malformed, range.Offset + 10, 0);
                WriteUInt16(malformed, range.Offset + 12, 17);
                malformed[range.Offset + 31] = 0x08;
            }
            return malformed;
        }
    }

    [Fact]
    public void Reads_composite_glyph_component_offsets_and_scale()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 11);
        WriteUInt16(font, glyph.Offset + 12, 1);
        WriteInt16(font, glyph.Offset + 14, 2);
        WriteInt16(font, glyph.Offset + 16, -3);
        WriteInt16(font, glyph.Offset + 18, 8192);

        OpenTypeCompositeGlyphComponent component = Assert.Single(OpenTypeFont.GetCompositeGlyphComponents(font, 2, CancellationToken.None));

        Assert.Equal((ushort)1, component.GlyphIndex);
        Assert.Equal(2, component.HorizontalOffset);
        Assert.Equal(-3, component.VerticalOffset);
        Assert.Equal(0.5, component.Transform.XX);
        Assert.Equal(0.5, component.Transform.YY);
    }

    [Theory]
    [InlineData((ushort)8192)]
    [InlineData((ushort)1024)]
    [InlineData((ushort)2048)]
    [InlineData((ushort)4096)]
    public void Rejects_composite_flags_outside_the_admitted_placement_and_transform_subset(ushort unsupportedFlag)
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 3 | unsupportedFlag);
        WriteUInt16(font, glyph.Offset + 12, 1);
        WriteInt16(font, glyph.Offset + 14, 0);
        WriteInt16(font, glyph.Offset + 16, 0);

        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Rejects_a_composite_component_with_conflicting_scale_encodings()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 3 | 8 | 64);
        WriteUInt16(font, glyph.Offset + 12, 1);
        WriteInt16(font, glyph.Offset + 14, 0);
        WriteInt16(font, glyph.Offset + 16, 0);

        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Rejects_point_matched_composite_glyph_components_on_all_decode_paths()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 1);
        WriteUInt16(font, glyph.Offset + 12, 1);
        WriteInt16(font, glyph.Offset + 14, 2);
        WriteInt16(font, glyph.Offset + 16, -3);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Rejects_truncated_composite_glyph_transforms_on_all_decode_paths()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        int locaOffset = FindTableOffset(font, 0x6C6F6361);
        WriteUInt32(font, locaOffset + 12, 48);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 10);
        WriteUInt16(font, glyph.Offset + 12, 1);
        font[glyph.Offset + 14] = 2;
        font[glyph.Offset + 15] = 253;
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Validates_composite_glyph_references_during_expansion_not_inspection()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 3);
        WriteUInt16(font, glyph.Offset + 12, 9);
        WriteInt16(font, glyph.Offset + 14, 0);
        WriteInt16(font, glyph.Offset + 16, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        OpenTypeCompositeGlyphComponent component = Assert.Single(OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None));

        Assert.Equal((ushort)9, component.GlyphIndex);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Validates_empty_composite_programs_and_preserves_unhinted_outline_inspection()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 3 | 256);
        WriteUInt16(font, glyph.Offset + 12, 1);
        WriteInt16(font, glyph.Offset + 14, 0);
        WriteInt16(font, glyph.Offset + 16, 0);
        WriteUInt16(font, glyph.Offset + 18, 0);

        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal((ushort)(3 | 256), Assert.Single(OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None)).Flags);
        Assert.Empty(OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None));
        RenderBudget budget = new(1_000_000);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetGlyphContours(font, 2, metrics, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None)) Assert.Empty(contours.Segments);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Rejects_truncated_composite_glyph_instructions_as_malformed_input()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, glyph.Offset, -1);
        WriteUInt16(font, glyph.Offset + 10, 3 | 256);
        WriteUInt16(font, glyph.Offset + 12, 1);
        WriteInt16(font, glyph.Offset + 14, 0);
        WriteInt16(font, glyph.Offset + 16, 0);
        WriteUInt16(font, glyph.Offset + 18, 1);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetCompositeGlyphComponents(font, 2, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    public enum CompositeGlyphComponentDefect
    {
        TruncatedComponent,
        TruncatedArguments,
        TruncatedInstructions,
    }

    [Theory]
    [InlineData(CompositeGlyphComponentDefect.TruncatedComponent)]
    [InlineData(CompositeGlyphComponentDefect.TruncatedArguments)]
    [InlineData(CompositeGlyphComponentDefect.TruncatedInstructions)]
    public void Rejects_malformed_composite_glyph_components_on_all_decode_paths(CompositeGlyphComponentDefect defect)
    {
        byte[] font = BuildMalformedComponentFont(defect);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetCompositeGlyphComponents(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(1_000_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None)).ErrorCode);

        byte[] BuildMalformedComponentFont(CompositeGlyphComponentDefect kind)
        {
            byte[] malformed = CreateFont(1024, 3, 2, 1);
            OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(malformed, 2, CancellationToken.None);
            WriteInt16(malformed, range.Offset, -1);
            if (kind is CompositeGlyphComponentDefect.TruncatedComponent)
            {
                WriteUInt16(malformed, range.Offset + 10, 35);
                WriteUInt16(malformed, range.Offset + 12, 1);
                WriteInt16(malformed, range.Offset + 14, 2);
                WriteInt16(malformed, range.Offset + 16, -3);
            }
            else if (kind is CompositeGlyphComponentDefect.TruncatedArguments)
            {
                WriteUInt16(malformed, range.Offset + 10, 34);
                WriteUInt16(malformed, range.Offset + 12, 1);
                malformed[range.Offset + 14] = 2;
                malformed[range.Offset + 15] = 253;
                WriteUInt16(malformed, range.Offset + 16, 3);
                WriteUInt16(malformed, range.Offset + 18, 1);
            }
            else
            {
                WriteUInt16(malformed, range.Offset + 10, 259);
                WriteUInt16(malformed, range.Offset + 12, 1);
                WriteInt16(malformed, range.Offset + 14, 0);
                WriteInt16(malformed, range.Offset + 16, 0);
                WriteUInt16(malformed, range.Offset + 18, 1);
            }
            return malformed;
        }
    }

    [Fact]
    public void Expands_composite_glyphs_with_their_affine_transform()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange simple = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, simple.Offset, 1);
        WriteUInt16(font, simple.Offset + 10, 1);
        WriteUInt16(font, simple.Offset + 12, 0);
        font[simple.Offset + 14] = 0x37;
        font[simple.Offset + 15] = 0x37;
        font[simple.Offset + 16] = 10;
        font[simple.Offset + 17] = 5;
        font[simple.Offset + 18] = 3;
        font[simple.Offset + 19] = 0;
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 11);
        WriteUInt16(font, composite.Offset + 12, 1);
        WriteInt16(font, composite.Offset + 14, 2);
        WriteInt16(font, composite.Offset + 16, -3);
        WriteInt16(font, composite.Offset + 18, 8192);

        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        OpenTypeQuadraticSegment[] segments = OpenTypeFont.GetGlyphContours(font, 2, metrics, CancellationToken.None);

        Assert.Equal([new OpenTypeQuadraticSegment(new(7, -1.5), new(9.5, -1.5), new(9.5, -1.5)), new OpenTypeQuadraticSegment(new(9.5, -1.5), new(7, -1.5), new(7, -1.5))], segments);
    }

    [Fact]
    public void Expands_composite_glyphs_with_the_OpenType_scale01_matrix_coefficient()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        Array.Resize(ref font, font.Length + 8);
        WriteUInt32(font, 120, 60);
        WriteUInt32(font, 286, 60);
        OpenTypeTableRange simple = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, simple.Offset, 1);
        WriteUInt16(font, simple.Offset + 10, 1);
        WriteUInt16(font, simple.Offset + 12, 0);
        font[simple.Offset + 14] = 0x37;
        font[simple.Offset + 15] = 0x37;
        font[simple.Offset + 16] = 10;
        font[simple.Offset + 17] = 5;
        font[simple.Offset + 18] = 3;
        font[simple.Offset + 19] = 0;
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 131);
        WriteUInt16(font, composite.Offset + 12, 1);
        WriteInt16(font, composite.Offset + 14, 0);
        WriteInt16(font, composite.Offset + 16, 0);
        WriteInt16(font, composite.Offset + 18, 16384);
        WriteInt16(font, composite.Offset + 20, 8192);
        WriteInt16(font, composite.Offset + 22, 0);
        WriteInt16(font, composite.Offset + 24, 16384);

        OpenTypeQuadraticSegment segment = OpenTypeFont.GetGlyphContours(font, 2, CancellationToken.None)[0];

        Assert.Equal(new OpenTypeOutlinePoint(10, 8), segment.Start);
        Assert.Equal(new OpenTypeOutlinePoint(15, 10.5), segment.Control);
    }

    [Fact]
    public void Expands_composite_glyphs_with_the_OpenType_scale10_matrix_coefficient()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        Array.Resize(ref font, font.Length + 8);
        WriteUInt32(font, 120, 60);
        WriteUInt32(font, 286, 60);
        OpenTypeTableRange simple = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, simple.Offset, 1);
        WriteUInt16(font, simple.Offset + 10, 1);
        WriteUInt16(font, simple.Offset + 12, 0);
        font[simple.Offset + 14] = 0x37;
        font[simple.Offset + 15] = 0x37;
        font[simple.Offset + 16] = 10;
        font[simple.Offset + 17] = 5;
        font[simple.Offset + 18] = 3;
        font[simple.Offset + 19] = 0;
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 131);
        WriteUInt16(font, composite.Offset + 12, 1);
        WriteInt16(font, composite.Offset + 14, 0);
        WriteInt16(font, composite.Offset + 16, 0);
        WriteInt16(font, composite.Offset + 18, 16384);
        WriteInt16(font, composite.Offset + 20, 0);
        WriteInt16(font, composite.Offset + 22, 8192);
        WriteInt16(font, composite.Offset + 24, 16384);

        OpenTypeQuadraticSegment segment = OpenTypeFont.GetGlyphContours(font, 2, CancellationToken.None)[0];

        Assert.Equal(new OpenTypeOutlinePoint(11.5, 3), segment.Start);
        Assert.Equal(new OpenTypeOutlinePoint(16.5, 3), segment.Control);
    }

    [Fact]
    public void Expands_nested_composite_glyph_transforms_in_parent_order()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeFontMetrics originalMetrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        OpenTypeTableRange loca = Assert.IsType<OpenTypeTableRange>(originalMetrics.Loca);
        OpenTypeTableRange glyf = Assert.IsType<OpenTypeTableRange>(originalMetrics.Glyf);
        Array.Resize(ref font, glyf.Offset + 72);
        WriteUInt32(font, 120, 72);
        WriteUInt32(font, loca.Offset, 0);
        WriteUInt32(font, loca.Offset + 4, 32);
        WriteUInt32(font, loca.Offset + 8, 52);
        WriteUInt32(font, loca.Offset + 12, 72);
        int leaf = glyf.Offset;
        int child = leaf + 32;
        int root = leaf + 52;
        WriteInt16(font, leaf, 1);
        WriteUInt16(font, leaf + 10, 1);
        WriteUInt16(font, leaf + 12, 0);
        font[leaf + 14] = 0x37;
        font[leaf + 15] = 0x37;
        font[leaf + 16] = 10;
        font[leaf + 17] = 5;
        font[leaf + 18] = 3;
        font[leaf + 19] = 0;
        WriteInt16(font, child, -1);
        WriteUInt16(font, child + 10, 3);
        WriteUInt16(font, child + 12, 0);
        WriteInt16(font, child + 14, 2);
        WriteInt16(font, child + 16, 1);
        WriteInt16(font, root, -1);
        WriteUInt16(font, root + 10, 11);
        WriteUInt16(font, root + 12, 1);
        WriteInt16(font, root + 14, 4);
        WriteInt16(font, root + 16, 6);
        WriteInt16(font, root + 18, 8192);

        OpenTypeQuadraticSegment segment = OpenTypeFont.GetGlyphContours(font, 2, CancellationToken.None)[0];

        Assert.Equal(new OpenTypeOutlinePoint(10, 8), segment.Start);
        Assert.Equal(new OpenTypeOutlinePoint(12.5, 8), segment.Control);
    }

    [Fact]
    public void Expands_composite_glyphs_with_a_two_by_two_rotation_matrix()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        Array.Resize(ref font, font.Length + 8);
        WriteUInt32(font, 120, 60);
        WriteUInt32(font, 286, 60);
        OpenTypeTableRange simple = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, simple.Offset, 1);
        WriteUInt16(font, simple.Offset + 10, 1);
        WriteUInt16(font, simple.Offset + 12, 0);
        font[simple.Offset + 14] = 0x37;
        font[simple.Offset + 15] = 0x37;
        font[simple.Offset + 16] = 10;
        font[simple.Offset + 17] = 5;
        font[simple.Offset + 18] = 3;
        font[simple.Offset + 19] = 0;
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 131);
        WriteUInt16(font, composite.Offset + 12, 1);
        WriteInt16(font, composite.Offset + 14, 0);
        WriteInt16(font, composite.Offset + 16, 0);
        WriteInt16(font, composite.Offset + 18, 0);
        WriteInt16(font, composite.Offset + 20, 16384);
        WriteInt16(font, composite.Offset + 22, -16384);
        WriteInt16(font, composite.Offset + 24, 0);

        OpenTypeQuadraticSegment segment = OpenTypeFont.GetGlyphContours(font, 2, CancellationToken.None)[0];

        Assert.Equal(new OpenTypeOutlinePoint(-3, 10), segment.Start);
        Assert.Equal(new OpenTypeOutlinePoint(-3, 15), segment.Control);
    }

    [Fact]
    public void Rejects_a_cycle_in_composite_glyph_references()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 3);
        WriteUInt16(font, composite.Offset + 12, 2);
        WriteInt16(font, composite.Offset + 14, 0);
        WriteInt16(font, composite.Offset + 16, 0);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public async Task Creates_a_bounded_snapshot_from_verified_caller_owned_font_bytes()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new SvgMemoryFontProgramSource("caller-font", bytes));

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);

        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        Assert.Equal((ushort)1024, font.UnitsPerEm);
        Assert.Equal((ushort)3, font.GlyphCount);
        Assert.True(font.Bytes.Span.SequenceEqual(bytes));
    }

    [Fact]
    public async Task Snapshot_observes_cancellation_between_integrity_hash_chunks()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        Array.Resize(ref bytes, 8_192);
        SvgFontProgramResolution resolution = CreateResolution(bytes);
        using CancellationTokenSource cancellation = new();
        bool completedChunk = false;
        using IDisposable probe = FontIntegrityProbe.Arm(() => { completedChunk = true; cancellation.Cancel(); });

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await SvgFontSnapshot.CreateAsync([resolution], cancellation.Token));

        Assert.True(completedChunk);
    }

    [Fact]
    public async Task Snapshot_hashes_a_manifest_bound_program_once()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        Array.Resize(ref bytes, 8_192);
        SvgFontProgramResolution resolution = CreateResolution(bytes);
        int hashChunks = 0;
        using IDisposable probe = FontIntegrityProbe.Arm(() => hashChunks++);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);

        Assert.Single(snapshot.Fonts);
        Assert.Equal(2, hashChunks);
    }

    [Fact]
    public async Task Manifest_bound_source_observes_cancellation_between_integrity_hash_chunks()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        Array.Resize(ref bytes, 8_192);
        SvgFontProgramResolution resolution = CreateResolution(bytes);
        using CancellationTokenSource cancellation = new();
        bool completedChunk = false;
        using IDisposable probe = FontIntegrityProbe.Arm(() => { completedChunk = true; cancellation.Cancel(); });

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await resolution.Source.GetBytesAsync(cancellation.Token));

        Assert.True(completedChunk);
    }

    [Fact]
    public async Task Snapshot_does_not_expose_its_prepared_face_array_for_mutation()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 1))], CancellationToken.None);

        Assert.False(snapshot.Fonts is SvgPreparedFont[]);
        Assert.Throws<NotSupportedException>(() => ((IList<SvgPreparedFont>)snapshot.Fonts)[0] = snapshot.Fonts[0]);
        Assert.Single(snapshot.Fonts);
    }

    [Fact]
    public async Task Snapshot_rejects_access_after_disposal()
    {
        var snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 1))], CancellationToken.None);
        snapshot.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = snapshot.Fonts);
        Assert.Throws<ObjectDisposedException>(() => snapshot.GetFont(new SvgFontFaceRequest("Example", 400, false)));
        snapshot.Dispose();
    }

    [Fact]
    public async Task Snapshot_invalidates_escaped_prepared_font_handles_on_disposal()
    {
        SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 1))], CancellationToken.None);
        SvgPreparedFont prepared = Assert.Single(snapshot.Fonts);
        snapshot.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = prepared.Resolution);
        Assert.Throws<ObjectDisposedException>(() => _ = prepared.Bytes);
        Assert.Throws<ObjectDisposedException>(() => _ = prepared.UnitsPerEm);
        Assert.Throws<ObjectDisposedException>(() => _ = prepared.GlyphCount);
    }

    [Fact]
    public async Task Snapshot_disposal_does_not_revoke_already_escaped_caller_memory()
    {
        var escapedViews = new List<ReadOnlyMemory<byte>>();
        for (int index = 0; index < 8; index++)
        {
            byte[] bytes = CreateFont(1024, 3, 2, 1);
            SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
            SvgPreparedFont prepared = Assert.Single(snapshot.Fonts);
            escapedViews.Add(prepared.Bytes);

            snapshot.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = prepared.Bytes);
        }

        Assert.All(escapedViews, view => Assert.Equal((byte)0, view.Span[0]));
    }

    [Fact]
    public async Task Snapshot_acquires_one_program_once_for_multiple_same_face_aliases()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\", \"faceIndex\": 0 }, { \"requestedFamily\": \"Example Alt\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\", \"faceIndex\": 0 }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgFontProgramResolution first = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), source);
        SvgFontProgramResolution second = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example Alt", 400, false)), source);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([first, second], CancellationToken.None);

        Assert.Equal(1, source.RequestCount);
        Assert.Equal(2, snapshot.Fonts.Count);
    }

    [Fact]
    public async Task Snapshot_rejects_a_nonzero_face_index_for_a_standalone_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\", \"faceIndex\": 1 }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new SvgMemoryFontProgramSource("caller-font", bytes));

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Format, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
    }

    [Fact]
    public async Task Request_based_snapshot_rejects_a_nonzero_face_index_before_provider_access()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\", \"faceIndex\": 1 }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource(bytes));

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("Example", 400, false)], provider, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Format, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Request_based_snapshot_does_not_reject_an_unused_unsupported_face()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }, { \"requestedFamily\": \"Unsupported\", \"resolvedFamily\": \"Unsupported\", \"relativeFontFile\": \"files/example.ttf\", \"faceIndex\": 1 }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource(bytes));

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("Example", 400, false)], provider, CancellationToken.None);

        Assert.Single(snapshot.Fonts);
        Assert.Equal(1, provider.RequestCount);
    }

    [Fact]
    public async Task Snapshot_attaches_format_context_to_an_unsupported_font_program()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        WriteUInt32(bytes, 0, 0x4F54544Fu);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Format, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
    }

    [Fact]
    public async Task Snapshot_attaches_integrity_context_when_bound_font_bytes_change()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        SvgFontProgramResolution resolution = CreateResolution(bytes);
        bytes[0] ^= 1;

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Integrity, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
    }

    [Fact]
    public async Task Snapshot_validates_every_selected_declaration_when_program_bytes_are_deduplicated()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/one.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }, { \"relativePath\": \"files/two.ttf\", \"byteSize\": " + (bytes.Length + 1) + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"One\", \"resolvedFamily\": \"One\", \"relativeFontFile\": \"files/one.ttf\" }, { \"requestedFamily\": \"Two\", \"resolvedFamily\": \"Two\", \"relativeFontFile\": \"files/two.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgFontProgramResolution first = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("One", 400, false)), source);
        SvgFontProgramResolution second = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Two", 400, false)), source);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([first, second], CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Integrity, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Snapshot_checks_its_face_limit_before_reading_any_font_source()
    {
        SvgFontProgramResolution[] resolutions = new SvgFontProgramResolution[257];

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(resolutions, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Request_based_snapshot_rejects_declared_programs_over_its_byte_limit_before_provider_access()
    {
        const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": 33554433, \"sha256\": \"" + Hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource([]));

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("Example", 400, false)], provider, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Request_based_snapshot_rejects_conflicting_same_hash_sizes_before_provider_access()
    {
        const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/one.ttf\", \"byteSize\": 1, \"sha256\": \"" + Hash + "\" }, { \"relativePath\": \"files/two.ttf\", \"byteSize\": 2, \"sha256\": \"" + Hash + "\" }], \"families\": [{ \"requestedFamily\": \"One\", \"resolvedFamily\": \"One\", \"relativeFontFile\": \"files/one.ttf\" }, { \"requestedFamily\": \"Two\", \"resolvedFamily\": \"Two\", \"relativeFontFile\": \"files/two.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource([]));

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("One", 400, false), new SvgFontFaceRequest("Two", 400, false)], provider, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Integrity, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Snapshot_resolves_requests_and_selectively_acquires_distinct_programs()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }, { \"requestedFamily\": \"Alias\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        var provider = new CountingFontProvider(source);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("Example", 400, false), new SvgFontFaceRequest("Alias", 400, false)], provider, CancellationToken.None);

        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
        Assert.Equal(2, snapshot.Fonts.Count);
    }

    [Fact]
    public void Bind_meters_the_provenance_scan_per_face()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
                string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }, { \"requestedFamily\": \"Alias\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgFontPackResolution first = catalog.Resolve(new SvgFontFaceRequest("Example", 400, false));
        SvgFontPackResolution second = catalog.Resolve(new SvgFontFaceRequest("Alias", 400, false));
        ParserWorkCounter firstCounter = new(100);
        ParserWorkCounter secondCounter = new(100);

        catalog.Bind(first, source, firstCounter, CancellationToken.None);
        catalog.Bind(second, source, secondCounter, CancellationToken.None);

        Assert.Equal(1, firstCounter.Used);
        Assert.Equal(2, secondCounter.Used);
    }

    [Fact]
    public void Bind_rejects_a_foreign_face_after_metering_the_full_scan()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
                string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }, { \"requestedFamily\": \"Alias\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        byte[] manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifest);
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifestBytes, "test-pack");
        SvgFontPackCatalog other = SvgFontPackCatalog.Parse(manifestBytes, "test-pack");
        SvgFontPackResolution foreign = other.Resolve(new SvgFontFaceRequest("Example", 400, false));
        ParserWorkCounter workCounter = new(100);

        Assert.Throws<ArgumentException>(() => catalog.Bind(foreign, new CountingFontSource(bytes), workCounter, CancellationToken.None));

        Assert.Equal(2, workCounter.Used);
    }

    [Fact]
    public void Bind_observes_cancellation_during_the_provenance_scan()
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgFontPackResolution selection = catalog.Resolve(new SvgFontFaceRequest("Example", 400, false));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => catalog.Bind(selection, source, new ParserWorkCounter(100), cancelled.Token));
    }

    [Fact]
    public async Task AcquireAsync_observes_cancellation_before_reporting_a_foreign_face()
    {
        byte[] bytes = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
                string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        byte[] manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifest);
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(manifestBytes, "test-pack");
        SvgFontPackCatalog other = SvgFontPackCatalog.Parse(manifestBytes, "test-pack");
        SvgFontPackResolution foreign = other.Resolve(new SvgFontFaceRequest("Example", 400, false));
        var source = new CountingFontSource(bytes);
        var provider = new CountingFontProvider(source);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await catalog.AcquireAsync(foreign, provider, cancelled.Token));
    }

    [Fact]
    public async Task Snapshot_rejects_a_selected_collection_face_before_acquisition()
    {
        byte[] font = CreateTriangleFont();
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/collection.ttc\", \"byteSize\": " + font.Length + ", \"sha256\": \"" + Convert.ToHexString(SHA256.HashData(font)) + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/collection.ttc\", \"weight\": 400, \"italic\": false, \"faceIndex\": 1 }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new SvgMemoryFontProgramSource("caller-font", font));

        Assert.Equal(1, resolution.Resolution.Face.FaceIndex);
        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }
    [Fact]
    public async Task Request_based_snapshot_reports_a_bounded_missing_source_diagnostic()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("Example", 400, false)], new MissingFontProvider(), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("Example", diagnostic.RequestedFamily);
    }

    [Theory]
    [InlineData(SvgErrorCode.UnresolvedResource, false)]
    [InlineData(SvgErrorCode.UnresolvedResource, true)]
    [InlineData(SvgErrorCode.LimitExceeded, false)]
    [InlineData(SvgErrorCode.LimitExceeded, true)]
    public async Task Snapshot_classifies_bound_source_failures_by_origin(SvgErrorCode errorCode, bool throwsSynchronously)
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new FailingFontSource(errorCode, throwsSynchronously));

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.DoesNotContain("private-source-detail", exception.Message, StringComparison.Ordinal);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("Example", diagnostic.RequestedFamily);
        Assert.Equal("Example", diagnostic.ResolvedFamily);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Empty(diagnostic.SourceElementId);
    }

    [Theory]
    [InlineData(SvgErrorCode.UnresolvedResource, false)]
    [InlineData(SvgErrorCode.UnresolvedResource, true)]
    [InlineData(SvgErrorCode.LimitExceeded, false)]
    [InlineData(SvgErrorCode.LimitExceeded, true)]
    public async Task Snapshot_classifies_provider_failures_by_origin(SvgErrorCode errorCode, bool throwsSynchronously)
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(catalog, [new SvgFontFaceRequest("Example", 400, false)], new FailingFontProvider(errorCode, throwsSynchronously), CancellationToken.None));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.DoesNotContain("private-provider-detail", exception.Message, StringComparison.Ordinal);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("Example", diagnostic.RequestedFamily);
        Assert.Equal("Example", diagnostic.ResolvedFamily);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Empty(diagnostic.SourceElementId);
    }

    [Fact]
    public async Task Svg_based_snapshot_discovers_and_prepares_a_selected_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Single(snapshot.Fonts);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_charges_metadata_before_provider_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource(bytes));
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 11);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, provider, limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Null(exception.FontDiagnostic);
        Assert.Equal(0, provider.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_an_invalid_root_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg' width='0'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_applies_caller_work_limits_to_metric_parsing()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 20);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Format, diagnostic.Stage);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_charges_manifest_bound_verification_before_metric_parsing()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 23);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Format, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_does_not_acquire_fonts_for_inert_definition_text()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><defs><text font-family='Unused'>x</text></defs><rect width='1' height='1'/></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_acquires_the_font_for_text_with_a_stylesheet_spaced_span()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><style>.wide { letter-spacing: 2; }</style><text font-family='Unused'><tspan class='wide' dx='2'>x</tspan></text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Single(snapshot.Fonts);
        Assert.Equal(1, source.RequestCount);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g font-family='Example' font-size='100'><text y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a font-family='Example' font-size='100'><text y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a font-family='Example' font-size='100'><text y='3'>A<tspan>A</tspan></text></a></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>g { font-family: Example; font-size: 100; }</style><g><text y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text display='none' font-family='Unused'>x</text></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g visibility='hidden'><text font-family='Unused'>x</text></g></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text fill='none' font-family='Unused'>x</text></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Unused'></text></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Unused'> \t\r\n </text></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve' style='white-space: normal'> </text></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'> </text></svg>", "Example", 1, 1, true, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan letter-spacing='2'><tspan>A</tspan></tspan></text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' x='0' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='1' height='1'/></defs><use href='#r'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs/><style>g { font-family: Example; font-size: 100; }</style><g><text y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><text font-family='Unused'>x</text></defs><rect width='5' height='5' fill='red'/></svg>", "", 0, 0, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan dx='2'>A</tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan x='0'>A</tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100'><tspan y='3px'>A</tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100'><tspan x='1' y='2'><tspan dx='-1' dy='1'>A</tspan></tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan x='200'>  </tspan>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><path id='p' d='M0 3H64' fill='none'> <!-- empty --> </path><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><path id='p' d='M0 3H64'> </path></defs><text font-family='Example' font-size='100'><textPath href='#p'>A</textPath></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan dy='1'>A</tspan></text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan xml:space='preserve'> </tspan></text></svg>", "Example", 1, 1, true, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'><tspan xml:space='default'> </tspan></text></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' opacity='0.5'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' letter-spacing='2'>AA</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g opacity='0.5'><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.t { font-family: Example; font-size: 100; }</style><text class='t' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' font-family='Example' font-size='100'><text y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-weight='700' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-style='italic' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-weight='bold' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g visibility='hidden'><text visibility='visible' font-family='Example' font-size='100' y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' opacity='0'>A</text></svg>", "Example", 1, 1, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g display='none'><text display='inline' font-family='Example' font-size='100' y='3'>A</text></g></svg>", "", 0, 0, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.a { font-family: Unused; } .b { font-family: Example; font-size: 100; }</style><text class='a b' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.t { font-family: Example; } .t { font-size: 100; }</style><text class='t' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a opacity='0.5'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a visibility='hidden'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "", 0, 0, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a display='none'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "", 0, 0, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a visibility='hidden'><text visibility='visible' font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g visibility='hidden'><a><text visibility='visible' font-family='Example' font-size='100' y='3'>A</text></a></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a display='none'><text display='inline' font-family='Example' font-size='100' y='3'>A</text></a></svg>", "", 0, 0, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' font-family='Example' font-size='100' font-weight='bold'><text y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' font-family='Example' font-size='100' font-style='italic'><text y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' font-family='Example' font-size='100' letter-spacing='2'><text y='3'>AA</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><title>t</title><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text><title>t</title></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><desc>d</desc><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g font-family='Example' font-size='100' font-weight='bold'><text y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g font-family='Example' font-size='100' font-style='italic'><text y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g font-family='Example' font-size='100' letter-spacing='2'><text y='3'>AA</text></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a font-family='Example' font-size='100' font-weight='bold'><text y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a font-family='Example' font-size='100' font-style='italic'><text y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a font-family='Example' font-size='100' letter-spacing='2'><text y='3'>AA</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan letter-spacing='2'><tspan dx='1'><tspan>A</tspan></tspan></tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan dx='1'><tspan dy='1'>A</tspan></tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan letter-spacing='2'><tspan letter-spacing='3'>A</tspan></tspan></text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' fill-opacity='0.5'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' stroke='none'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' fill='red'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text color='blue' font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text color='blue' fill='red' font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text color='blue' fill='none' font-family='Example' font-size='100' y='3'>A</text></svg>", "", 0, 0, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='5' height='5'><a xlink:href='#x'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='5' height='5'><defs><rect id='r' width='1' height='1'/></defs><use xlink:href='#r'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Missing' font-size='100' y='3'>A</text></svg>", "Missing", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Missing1' font-size='100' y='3'>A</text><text font-family='Missing2' font-size='100' y='4'>A</text></svg>", "Missing1,Missing2", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Missing' font-weight='bold' font-size='100' y='3'>A</text></svg>", "Missing", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Missing' font-style='italic' font-size='100' y='3'>A</text></svg>", "Missing", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Missing' font-size='100' y='3'>A</text><text font-family='Missing' font-weight='bold' font-size='100' y='4'>A</text></svg>", "Missing,Missing", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.l { opacity: 0.5; }</style><a class='l'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.gg { letter-spacing: 2; }</style><g class='gg'><text font-family='Example' font-size='100' y='3'>AA</text></g></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='1' height='1'/></defs><use href='#r' opacity='0.5'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='0.5' y='3'>A</text></svg>", "Example", 1, 1, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='10000' y='3'>A</text></svg>", "Example", 1, 1, false, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><title>t</title></defs><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.s { letter-spacing: 50; }</style><text font-family='Example' font-size='100' y='3'>A<tspan class='s'>A</tspan>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='2' height='2' fill='red'/></defs><use href='#r' x='1' y='1'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><line id='l' x1='0' y1='0' x2='2' y2='0' stroke='red' stroke-width='2'/></defs><use href='#l'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><symbol id='s'><rect width='1' height='1' fill='red'/></symbol></defs><use href='#s'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text><text font-family='Example' font-size='100' y='4'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan>A</tspan></text><text font-family='Example' font-size='100' y='4'>A</text></svg>", "Example", 1, 1, true, false)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='2' height='2' fill='red'/></defs><g><use href='#r'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><defs><rect id='r' width='2' height='2' fill='red'/></defs></g><use href='#r'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><switch><text font-family='Example' font-size='100' y='3'>A</text></switch></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><switch/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text><switch/></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' x1='1' y1='1' x2='1' y2='1'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='2' height='2' fill='red'/></defs><a href='#x'><use href='#r'/></a><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a href='#x' target='_blank'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a href='#x' rel='noopener'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a href='#x' hreflang='en'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='2' height='2' fill='red'/></defs><a href='#x'><g><use href='#r'/></g></a><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3'><rect width='5' height='5' fill='red'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3' viewBox='0 0 3 3'><rect width='3' height='3' fill='red'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3'><g><rect width='5' height='5' fill='red'/></g></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='4' height='4'><svg x='1' y='1' width='2' height='2'><rect width='5' height='5' fill='red'/></svg></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><symbol id='s' viewBox='0 0 4 4'><rect width='4' height='4' fill='red'/></symbol></defs><use href='#s' x='1' y='1' width='2' height='2'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><symbol id='s' viewBox='0 0 4 4'><rect width='4' height='4' fill='red'/></symbol></defs><use href='#s' x='1' y='1'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1'><rect width='2' height='2' fill='red'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><g clip-path='url(#c)'><rect width='5' height='5' fill='red'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='a'><rect x='1' y='1' width='4' height='4'/></clipPath><clipPath id='b'><rect x='2' y='2' width='2' height='2'/></clipPath></defs><g clip-path='url(#a)'><g clip-path='url(#b)'><rect width='5' height='5' fill='red'/></g></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='0' y='0' width='5' height='5' fill='#ffffff'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m' mask-type='alpha'><rect x='0' y='0' width='5' height='5' fill='#00ff00'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='1' y='1' width='3' height='3'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/><rect x='1' y='1' width='1' height='1' fill='blue'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><circle cx='1' cy='1' r='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' href='#base'/><pattern id='base' width='2.5' height='2.25' patternUnits='userSpaceOnUse'><circle cx='1' cy='1' r='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='0.5' stop-color='#00ff00'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#g)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><path d='M0,0 H5 V5 H0 Z' fill='url(#g)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><path d='M0,0 H5 V5 H0 Z' fill='red' clip-path='url(#c)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g transform='translate(5,0) scale(-1,1)'><rect x='0' y='0' width='2' height='2' fill='red'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><path d='M1,1 H6 V6 H1 Z M3,0 H5 V8 H3 Z' fill='red'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='2' height='2' transform='translate(1,1)'/></clipPath></defs><rect width='5' height='5' fill='red' clip-path='url(#c)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAADklEQVR4nGP4z8DwH4QBEfcD/ePF9e8AAAAASUVORK5CYII=' x='0' y='0' width='4' height='2'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3' opacity='0.5'><rect width='5' height='5' fill='red'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><line x1='0' y1='1' x2='4' y2='1' stroke='red' stroke-width='2' opacity='0.5'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='5' y2='0'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='1' x2='4' y2='1' stroke='url(#g)' stroke-width='2'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse' patternTransform='translate(1,1)'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><path d='M0,0 H5 V5 H0 Z' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='1' y='1' width='3' height='3' transform='translate(1)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='0' y='0' width='2' height='2' transform='translate(2,0)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><symbol id='s'><rect width='1' height='1' fill='red'/></symbol><use href='#s'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g display='none'><symbol id='s'><rect width='1' height='1' fill='red'/></symbol></g><use href='#s'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><symbol/><defs><symbol/></defs><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><symbol><rect id='leaf' width='1' height='1' fill='red'/></symbol><use href='#leaf'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><symbol id='s'><title>Shape</title><metadata><note xmlns='urn:sample'>ignored</note></metadata><rect width='1' height='1' fill='red'/></symbol><use href='#s'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><title>t</title><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a><desc>d</desc><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><clipPath id='c'><path d='M0,0 Q1,1 2,0 Z'/></clipPath><rect width='5' height='5' fill='red' clip-path='url(#c)'/></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text class='label' font-size='100' y='3'>A</text><style>.label { font-family: Example; }</style></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text class='label' font-size='100' y='3'>A</text><defs><style>.label { font-family: Example; }</style></defs></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text class='hidden' font-family='Unused'>A</text><style>.hidden { fill: none; }</style></svg>", "", 0, 0, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>text { font-family: Unused; } .t { font-family: Example; font-size: 100; }</style><text class='t' y='3'>A</text></svg>", "Example", 1, 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>tspan { letter-spacing: 50; }</style><text font-family='Example' font-size='100' y='3'>A<tspan>A</tspan>A</text></svg>", "Example", 1, 1, true, false)]
    public async Task Discovery_and_lowering_agree_on_supported_text(string svg, string expectedRequests, int expectedProviderCalls, int expectedSourceCalls, bool painted, bool spaceFont)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        string[] expected = expectedRequests.Length == 0 ? [] : expectedRequests.Split(',');
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);

        Assert.Equal(expected, requests.Select(static request => request.Family).ToArray());
        byte[] font = spaceFont ? CreateTriangleSpaceFont() : CreateTriangleFont();
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(font);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);

        Assert.Equal(expected.Length, snapshot.Fonts.Count);
        Assert.Equal(expectedProviderCalls, provider.RequestCount);
        Assert.Equal(expectedSourceCalls, source.RequestCount);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        if (painted)
        {
            Assert.True(DecodeAlpha(png, 1, 1) > 0);
        }
        else
        {
            Assert.Equal(0, DecodeAlpha(png, 1, 1));
        }
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect id='r' width='1' height='1'/></defs><style>g { font-family: Example; }</style><g><text font-family='Example'>A</text></g></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s'><line x1='0' y1='0' x2='1' y2='0' stroke='red' stroke-width='2'/></symbol></defs><style>g { font-family: Example; }</style><g><text font-family='Example'>A</text></g></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='1' height='1'/></defs><style>.u { opacity: 0.5; }</style><use href='#r' class='u'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><style>rect { fill: red; }</style><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><style>rect { fill: red; }</style><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><style>rect { fill: red; }</style><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    public async Task Late_stylesheets_after_definitions_preserve_discovery_and_lowering(string svg)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        Assert.Equal("Example", Assert.Single(SvgFontRequestDiscovery.Discover(input, CancellationToken.None)).Family);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgConverter.ConvertToPng(input, new SvgRenderOptions(new SvgViewport(5, 5), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' font-size='0'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='-5' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>A</text></svg> trailing", SvgErrorCode.MalformedInput)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' textLength='50'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' stroke='red'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' fill-rule='positive'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' style='white-space: pre-line'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-variant='small-caps' font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text text-decoration='underline' font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text word-spacing='5' font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text style='text-decoration: underline' font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.l { font-variant: small-caps; }</style><a class='l'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.l { text-decoration: underline; }</style><a class='l'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.gg { font-variant: small-caps; }</style><g class='gg'><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.gg { text-decoration: underline; }</style><g class='gg'><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' font-variant='small-caps'><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' class='rt'><style>.rt { opacity: 0.5; }</style><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='1' height='1'/></defs><style>.u { font-variant: small-caps; }</style><use href='#r' class='u'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><svg><text font-family='Example'>A</text></svg></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><svg x='1' y='1'><rect width='2' height='2' fill='red'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs><style>x</style></defs><text font-family='Unused'>A</text></svg>", SvgErrorCode.MalformedInput)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg width='5' height='5'><rect width='1' height='1'/></svg><text font-family='Example' font-size='100' y='3'>A</text><style>svg>svg{fill:red}</style></svg>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='1' height='1'/></defs><text font-family='Example' font-size='100' y='3'>A</text><style>svg>defs{fill:red}</style></svg>", SvgErrorCode.UnsupportedFeature)]
    public async Task Discovery_rejects_unsupported_text_before_lowering(string svg, SvgErrorCode code)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        SvgException exception = Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, CancellationToken.None));

        Assert.Equal(code, exception.ErrorCode);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        SvgException snapshot = await Assert.ThrowsAsync<SvgException>(() => SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None).AsTask());

        Assert.Equal(code, snapshot.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }

    [Theory]
    [InlineData(0, 61)]
    [InlineData(1, 62)]
    [InlineData(2, 63)]
    public async Task Letter_spacing_shifts_second_glyph_advance_by_exact_pixels(int spacing, int expectedRightmostX)
    {
        string spacingAttribute = spacing == 0 ? string.Empty : " letter-spacing='" + spacing + "'";
        string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='8'><text font-family='Example' font-size='100' y='7'" + spacingAttribute + ">AA</text></svg>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(DecodeAlpha(png, 1, 5) > 0);
        int rightmost = -1;
        for (int x = 0; x < 64; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                if (DecodeAlpha(png, x, y) > 0)
                {
                    rightmost = x;
                }
            }
        }

        Assert.Equal(expectedRightmostX, rightmost);
    }

    [Theory]
    [InlineData("g.outer text>tspan.target", "<tspan class='target'{0}>AA</tspan>")]
    [InlineData(".outer text tspan.target", "<tspan><tspan class='target'{0}>AA</tspan></tspan>")]
    public async Task Text_subtree_selectors_preserve_absolute_ancestor_depth(string selector, string span)
    {
        string document = "<svg xmlns='http://www.w3.org/2000/svg' width='256' height='8'><g class='outer'><text font-family='Example' font-size='100' y='7'>A{0}A</text></g>{1}</svg>";
        byte[] expected = await RenderAsync(string.Format(document, string.Format(span, " letter-spacing='5'"), string.Empty));
        byte[] actual = await RenderAsync(string.Format(document, string.Format(span, string.Empty), "<style>" + selector + "{letter-spacing:5}</style>"));
        byte[] unspaced = await RenderAsync(string.Format(document, string.Format(span, string.Empty), string.Empty));

        Assert.Equal(expected, actual);
        Assert.False(actual.AsSpan().SequenceEqual(unspaced));

        static async Task<byte[]> RenderAsync(string svg)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
            (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            Assert.Equal(1, provider.RequestCount);
            Assert.Equal(1, source.RequestCount);
            return SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        }
    }

    [Fact]
    public async Task Text_opacity_scales_glyph_alpha_with_native_rounding()
    {
        async Task<byte> RenderAlphaAsync(string svg)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
            (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
            using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
            ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);
            return DecodeAlpha(png, 1, 1);
        }

        byte full = await RenderAlphaAsync("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>");
        byte half = await RenderAlphaAsync("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' opacity='0.5'>A</text></svg>");

        Assert.True(full > 0);
        // Linux uses native mask multiplication after font preblend; the
        // existing Windows sampler remains pending its native font correction.
        int expected = OperatingSystem.IsLinux() ? (full + 1) * 128 >> 8 : full / 2;
        Assert.Equal(expected, (int)half);
    }

    [Fact]
    public void Nested_use_applies_enclosing_group_transform()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><defs><rect id='r' width='2' height='2' fill='red'/></defs><g transform='translate(1)'><use href='#r'/></g></svg>");
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null), CancellationToken.None);

        Assert.Equal(0, DecodeAlpha(png, 0, 0));
        Assert.Equal(0, DecodeAlpha(png, 0, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodePixel(png, 1, 1));
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodePixel(png, 2, 1));
        Assert.Equal(0, DecodeAlpha(png, 3, 1));
        Assert.Equal(0, DecodeAlpha(png, 1, 2));
    }

    [Fact]
    public void Nested_viewport_clips_rendered_content_to_its_box()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><svg x='8' y='8' width='32' height='32'><rect width='64' height='64' fill='red'/></svg></svg>");
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, null), CancellationToken.None);

        Assert.Equal(0, DecodeAlpha(png, 7, 8));
        Assert.Equal(0, DecodeAlpha(png, 8, 7));
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodePixel(png, 8, 8));
        Assert.Equal(new SvgColor(255, 0, 0, 255), DecodePixel(png, 39, 39));
        Assert.Equal(0, DecodeAlpha(png, 40, 39));
        Assert.Equal(0, DecodeAlpha(png, 39, 40));
    }


    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>A<tspan font-style='italic'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>A<text font-family='Example'>A</text></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' transform='scale(2)'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g transform='scale(2)'><text font-family='Unused'>x</text></g></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><a transform='scale(2)'><text font-family='Unused'>x</text></a></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><circle cx='1' cy='1' r='1' fill='url(#g)'/><text font-family='Unused'>x</text></svg>", SvgErrorCode.UnsupportedFeature, "Unused", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><symbol id='s'><text font-family='Unused'>x</text></symbol></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused'>A</text><circle cx='1' cy='1' r='1' fill='url(#g)'/></svg>", SvgErrorCode.UnsupportedFeature, "Unused", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs xmlns='urn:x'><rect width='1' height='1' fill='red'/></defs><text font-family='Unused'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g><use href='#x'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><g><foo/></g><text font-family='Example'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><symbol id='s'><text font-family='Unused'>x</text></symbol><use href='#s'/></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><mask id='m'><text font-family='Unused'>x</text></mask><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example'><tspan rotate='5'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example'>A<tspan rotate='1'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><path id='p' d='M0 3H64'><text font-family='Example'>A</text></path><text font-family='Example'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><path id='p' d='M0 3H64'><unknown/></path></defs><text font-family='Example'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><path id='p' d='M0 3H64'/></defs><text font-family='Example'><textPath href='#p'><tspan x='1'>A</tspan></textPath></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan transform='translate(1)'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan style='font-size: 13'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><foreignObject width='5' height='5'><text font-family='Unused'>x</text></foreignObject><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><pattern id='p'><text font-family='Unused'>x</text></pattern><rect width='5' height='5' fill='url(#p)'/></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><rect id='r' width='5' height='5' fill='red'/></defs><use id='u' href='#r'/><use href='#u'/></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' x='1 2' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A😀</text></svg>", SvgErrorCode.UnresolvedResource, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><a href='#x'>A</a></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><path id='p' d='M0,0 H5'/></defs><text font-family='Example' font-size='100' y='3'><textPath href='#p' method='stretch'>A</textPath></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><text id='t' font-family='Example' font-size='100' y='3'>A</text></defs><use href='#t'/></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan font-size='50'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan font-size='13'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan opacity='0.5'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan font-family='Example'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan><a href='#x'>A</a></tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><a href='#x'><tspan>A</tspan></a></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><style>g { font-family: Example; font-size: 100; }</style><text y='3'>A</text></g></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g transform='scale(2)'><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a transform='scale(2)'><text font-family='Example' font-size='100' y='3'>A</text></a></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' transform='scale(2)'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><a><defs><rect id='r' width='1' height='1'/></defs></a><use href='#r'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
[InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>.s { font-family: Example; font-size: 100; }</style><text y='3'>A<tspan class='s'>A</tspan></text></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3'><text font-family='Example'>A</text></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><svg x='1' y='1' width='3' height='3'><rect width='5' height='5' fill='red' stroke='blue' stroke-width='2'/></svg><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><symbol id='s' viewBox='0 0 0 0'><rect width='1' height='1' fill='red'/></symbol></defs><use href='#s' x='1' y='1' width='2' height='2'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.MalformedInput, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><symbol id='s' preserveAspectRatio='bogus'><rect width='1' height='1' fill='red'/></symbol></defs><use href='#s' x='1' y='1' width='2' height='2'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><defs><symbol id='s' viewBox='0 0 4 4'><rect width='4' height='4' fill='red'/></symbol></defs><use href='#s'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
        [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><g clip-path='url(#c)'><text font-family='Example'>A</text></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='3' height='3'/></clipPath></defs><g clip-path='url(#missing)'><rect width='5' height='5' fill='red'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' clip-path='url(#c)'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><clipPath id='c'><rect x='1' y='1' width='2' height='2'/><rect x='3' y='3' width='1' height='1'/></clipPath></defs><g clip-path='url(#c)'><rect width='5' height='5' fill='red'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='1' y='1' width='3' height='3' fill='#ffffff'/></mask></defs><text font-family='Example' font-size='100' y='3' mask='url(#m)'>A</text><text font-family='Example' font-size='100' y='4'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5' fill='red' mask='url(#missing)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='1' y='1' width='3' height='3' fill='#ff0000'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAADUlEQVR4nGP4zwAE/wEHAAH/4iOeWQAAAABJRU5ErkJggg==' x='1' y='1' width='3' height='3'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><image href='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' x='1' y='1'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><image href='data:image/png;base64,!!!' x='1' y='1' width='3' height='3'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.MalformedInput, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#missing)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2'><rect width='1' height='1' fill='red'/></pattern></defs><rect width='5' height='5' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='5' height='5' fill='url(#missing)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='1' x2='4' y2='1' stroke='url(#g)' stroke-width='2'/><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g transform='scale(-1,1)'><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", SvgErrorCode.UnsupportedFeature, "", 0, 0)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='1' y='1' width='3' height='3' fill='#ffffff'/></mask></defs><style>rect { fill: red; }</style><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect width='5' height='5' fill='white'/></mask></defs><g mask='url(#m)'><text font-family='Unused'>x</text></g><text font-family='Example' font-size='100' y='3'>A</text></svg>", SvgErrorCode.UnsupportedFeature, "Example", 1, 1)]
    public async Task Discovery_and_lowering_agree_on_rejected_text(string svg, SvgErrorCode code, string expectedRequests, int expectedProviderCalls, int expectedSourceCalls)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        string[] expected = expectedRequests.Length == 0 ? [] : expectedRequests.Split(',');
        SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, CancellationToken.None);

        Assert.Equal(expected, requests.Select(static request => request.Family).ToArray());
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);

        Assert.Equal(expectedProviderCalls, provider.RequestCount);
        Assert.Equal(expectedSourceCalls, source.RequestCount);
        SvgException lowering = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None));

        Assert.Equal(code, lowering.ErrorCode);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><mask id='m'><rect width='1' height='1' fill='white'/></mask><rect width='5' height='5' fill='red' mask='url(#m)'/></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='1' y='1' width='3' height='3' fill='#ffffff'/></mask></defs><g mask='url(#m)'><rect width='5' height='5' fill='red'/></g><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='0' y='0' width='2' height='2' fill='#ffffff'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><mask id='m'><rect x='0' y='0' width='5' height='5' fill='#ffffff' transform='translate(1,1)'/></mask></defs><rect width='5' height='5' fill='red' mask='url(#m)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    public async Task Partial_masks_preserve_following_font_requests(string svg)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        bool hasText = svg.Contains("<text", StringComparison.Ordinal);
        Assert.Equal(hasText ? new[] { "Example" } : [], SvgFontRequestDiscovery.Discover(input, CancellationToken.None).Select(request => request.Family));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(hasText ? 1 : 0, provider.RequestCount);
        Assert.Equal(hasText ? 1 : 0, source.RequestCount);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><pattern id='p' width='2' height='2' patternUnits='userSpaceOnUse'><rect width='1' height='1' fill='red'/></pattern></defs><line x1='0' y1='0' x2='4' y2='0' stroke='red' stroke-width='2' fill='url(#p)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><line x1='0' y1='0' x2='4' y2='0' stroke='red' stroke-width='2' fill='url(#g)'/><text font-family='Example' font-size='100' y='3'>A</text></svg>")]
    public async Task Line_fill_servers_are_inert_and_preserve_following_font_requests(string svg)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        Assert.Equal(new[] { "Example" }, SvgFontRequestDiscovery.Discover(input, CancellationToken.None).Select(request => request.Family));
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(1, provider.RequestCount);
        Assert.Equal(1, source.RequestCount);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>", 1, true, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5' fill='red'/></svg>", 2, true, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><rect width='5' height='5' fill='red'/></svg>", 1, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><title>t</title><rect width='5' height='5' fill='red'/></svg>", 3, true, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><title>t</title><rect width='5' height='5' fill='red'/></svg>", 2, false, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>rect { fill: red; }</style><rect width='5' height='5'/></svg>", 3, true, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>rect { fill: red; }</style><rect width='5' height='5'/></svg>", 2, false, false)]
    public async Task Discovery_and_lowering_agree_on_root_node_boundaries(string svg, int maximumNodes, bool succeeds, bool painted)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        SvgRenderLimits limits = new(1_048_576, 1_024, 16, 256, 4_096, maximumNodes, 16, 100_000, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        if (succeeds)
        {
            SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, limits, CancellationToken.None);

            Assert.Empty(requests);
            ReadOnlyMemory<byte> raw = SvgConverter.ConvertToPng(input, options, CancellationToken.None);

            Assert.True(raw.Length > 0);
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
            ReadOnlyMemory<byte> prepared = document.ConvertToPng(CancellationToken.None);

            Assert.True(prepared.Length > 0);
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal(0, source.RequestCount);
            if (painted)
            {
                Assert.True(DecodeAlpha(prepared, 1, 1) > 0);
            }
        }
        else
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, limits, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            SvgException preparedFailure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None));

            Assert.Equal(SvgErrorCode.LimitExceeded, preparedFailure.ErrorCode);
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal(0, source.RequestCount);
        }
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan>A</tspan></text></svg>", 3, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<tspan>A</tspan></text></svg>", 2, false)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", 3, true)]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><text font-family='Example' font-size='100' y='3'>A</text></g></svg>", 2, false)]
    public async Task Discovery_and_lowering_agree_on_text_node_boundaries(string svg, int maximumNodes, bool succeeds)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        SvgRenderLimits limits = new(1_048_576, 1_024, 16, 256, 4_096, maximumNodes, 16, 100_000, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        if (succeeds)
        {
            SvgFontFaceRequest[] requests = SvgFontRequestDiscovery.Discover(input, limits, CancellationToken.None);

            Assert.Single(requests);
            // Raw conversion has no font snapshot: node admission passes and only the missing font rejects the text.
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None);
            ReadOnlyMemory<byte> prepared = document.ConvertToPng(CancellationToken.None);

            Assert.True(DecodeAlpha(prepared, 1, 1) > 0);
            Assert.Equal(1, provider.RequestCount);
            Assert.Equal(1, source.RequestCount);
        }
        else
        {
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, limits, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            SvgException preparedFailure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None));

            Assert.Equal(SvgErrorCode.LimitExceeded, preparedFailure.ErrorCode);
            Assert.Equal(0, provider.RequestCount);
            Assert.Equal(0, source.RequestCount);
        }
    }
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>")]
    public async Task Discovery_and_lowering_agree_on_reader_workspace_denial(string svg)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(svg);
        SvgRenderLimits limits = new(1, 1_024, 16, 256, 4_096, 16, 16, 100_000, 16, 16);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, limits);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgFontRequestDiscovery.Discover(input, limits, CancellationToken.None)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
        SvgException preparedFailure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(input, catalog, provider, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, preparedFailure.ErrorCode);
        Assert.Equal(0, provider.RequestCount);
        Assert.Equal(0, source.RequestCount);
    }
    [Fact]
    public async Task Painted_text_pixels_carry_the_declared_fill_color()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' fill='red'>A</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        SvgColor painted = DecodePixel(png, 1, 1);
        SvgColor background = DecodePixel(png, 0, 0);

        Assert.Equal(255, painted.Red);
        Assert.Equal(0, painted.Green);
        Assert.Equal(0, painted.Blue);
        Assert.True(painted.Alpha > 0);
        Assert.Equal(new SvgColor(0, 0, 0, 0), background);
    }

    [Fact]
    public async Task Text_opacity_composes_multiplicatively_over_paint_and_coverage()
    {
        byte[] fillInput = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' fill='red' fill-opacity='0.5'>A</text></svg>");
        byte[] layeredInput = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' fill='red' fill-opacity='0.5' opacity='0.5'>A</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot fillSnapshot = await SvgFontSnapshot.CreateAsync(fillInput, catalog, provider, CancellationToken.None);
        using SvgFontSnapshot layeredSnapshot = await SvgFontSnapshot.CreateAsync(layeredInput, catalog, provider, CancellationToken.None);
        SvgColor fill = DecodePixel(SvgConverter.ConvertToPng(fillInput, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fillSnapshot), CancellationToken.None), 1, 1);
        SvgColor layered = DecodePixel(SvgConverter.ConvertToPng(layeredInput, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, layeredSnapshot), CancellationToken.None), 1, 1);

        // The same triangle has native Linux mask coverage 130 after red
        // preblend. Alpha 128/64 applies afterward using (coverage + 1) / 256.
        Assert.Equal(new SvgColor(255, 0, 0, OperatingSystem.IsLinux() ? (byte)65 : (byte)48), fill);
        Assert.Equal(new SvgColor(255, 0, 0, OperatingSystem.IsLinux() ? (byte)32 : (byte)24), layered);
    }

    [Fact]
    public void Admitted_fill_rules_render_identically_on_simple_geometry()
    {
        ReadOnlyMemory<byte> nonzero = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='2' height='2' fill='red' fill-rule='nonzero'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);
        ReadOnlyMemory<byte> evenodd = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'><rect width='2' height='2' fill='red' fill-rule='evenodd'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        Assert.Equal(nonzero.ToArray(), evenodd.ToArray());
    }

    [Fact]
    public void Clamps_stacked_translucent_rectangle_composites_to_opaque_channels()
    {
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><rect x='14' y='8' width='12' height='48' fill='#ff0000' fill-opacity='0.5'/><rect x='24' y='24' width='16' height='16' fill='#ffcc00' fill-opacity='0.5'/></svg>"u8.ToArray(), SvgRenderOptions.Default, CancellationToken.None);

        // Pinned premultiplied backend (SkAlphaMulQ premultiply, SkPMSrcOver composite,
        // round-half-up un-premultiply) moves the overlap fringe to (255,135,0,192).
        Assert.Equal(new SvgColor(255, 135, 0, 192), DecodePixel(png, 24, 24));
        Assert.Equal(new SvgColor(255, 135, 0, 192), DecodePixel(png, 25, 30));
        // Single-layer mid-tone over transparent un-premultiplies (102 * 255 + 64) over 128 to 203.
        Assert.Equal(new SvgColor(255, 203, 0, 128), DecodePixel(png, 30, 30));
        Assert.Equal(new SvgColor(255, 0, 0, 128), DecodePixel(png, 16, 10));
    }

    [Fact]
    public async Task Discovery_and_preparation_observe_cancellation_together()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>");
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => SvgFontRequestDiscovery.Discover(input, cancelled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await SvgFontSnapshot.CreateAsync(input, catalog, provider, cancelled.Token));
        Assert.Equal(0, provider.RequestCount);
    }

    private static (SvgFontPackCatalog Catalog, CountingFontProvider Provider, CountingFontSource Source) CreateExamplePack(byte[] font)
    {
        string hash = Convert.ToHexString(SHA256.HashData(font));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + font.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(font);
        return (catalog, new CountingFontProvider(source), source);
    }

    [Fact]
    public async Task Empty_direct_text_does_not_acquire_or_require_a_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><text font-family='Unused'></text><rect width='1' height='1'/></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(png.Length > 0);
        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_does_not_acquire_fonts_for_unsupported_scaled_text()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><g transform='scale(2)'><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Stylesheet_group_font_properties_are_shared_by_snapshot_preparation_and_rendering()
    {
        byte[] bytes = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>g { font-family: Example; font-size: 100; }</style><g><text y='3'>A</text></g></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.Single(snapshot.Fonts);
        Assert.Equal(1, source.RequestCount);
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Direct_inline_font_properties_override_a_stylesheet_group_font_family()
    {
        byte[] bytes = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>g { font-family: Missing; font-size: 100; }</style><g><text style='font-family: Example' y='3'>A</text></g></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.Equal("Example", Assert.Single(snapshot.Fonts).Resolution.Resolution.Request.Family);
        Assert.Equal(1, source.RequestCount);
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Stylesheet_font_properties_follow_selector_specificity_per_property()
    {
        byte[] bytes = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>g { font-family: Missing; font-size: 100; } .preferred { font-family: Example; }</style><g class='preferred'><text y='3'>A</text></g></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.Equal("Example", Assert.Single(snapshot.Fonts).Resolution.Resolution.Request.Family);
        Assert.Equal(1, source.RequestCount);
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Stylesheet_font_weight_and_style_select_the_matching_prepared_face()
    {
        byte[] bytes = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\", \"weight\": 700, \"italic\": true }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>g { font-family: Example; font-size: 100; font-weight: 700; font-style: italic; }</style><g><text y='3'>A</text></g></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        SvgFontFaceRequest request = Assert.Single(snapshot.Fonts).Resolution.Resolution.Request;
        Assert.Equal(700, request.Weight);
        Assert.True(request.Italic);
        Assert.Equal(1, source.RequestCount);
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Svg_based_snapshot_does_not_acquire_fonts_for_stylesheet_hidden_text()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><style>.hidden { display: none; }</style><g class='hidden'><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Hidden_text_with_an_empty_prepared_snapshot_does_not_require_a_font_at_conversion()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><text display='none' font-family='Unused'>x</text></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(png.Length > 0);
        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Nonpainting_text_with_an_empty_prepared_snapshot_retains_validation_without_requiring_a_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><text fill='none' font-family='Unused'>x</text></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(png.Length > 0);
        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Inherited_nonpainting_text_with_an_empty_prepared_snapshot_does_not_require_a_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><g fill='none'><text font-family='Unused'>x</text></g></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(png.Length > 0);
        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Root_inherited_nonpainting_text_with_an_empty_prepared_snapshot_does_not_require_a_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1' fill='none'><text font-family='Unused'>x</text></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(png.Length > 0);
        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Stylesheet_inherited_nonpainting_text_with_an_empty_prepared_snapshot_does_not_require_a_font()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><style>g { fill: none; }</style><g><text font-family='Unused'>x</text></g></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None);

        Assert.True(png.Length > 0);
        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_does_not_acquire_fonts_after_unsupported_sibling_content()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><foreignObject/><text font-family='Unused'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Theory]
    [InlineData("<rect><animate/></rect>")]
    [InlineData("<g><style>rect { fill: red; }</style></g>")]
    public async Task Svg_based_snapshot_does_not_acquire_fonts_after_known_render_unsupported_content(string content)
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + content + "<text font-family='Unused'>x</text></svg>");

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_definition_container_attributes_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><defs fill='red'/><text font-family='Unused'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_definition_child_attributes_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><defs><rect vector-effect='non-scaling-stroke'/></defs><text font-family='Unused'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_unsupported_text_attributes_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused' filter='url(#missing)'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_invalid_text_fill_rules_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused' fill-rule='positive'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_unsupported_group_presentation_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><g style='fill-rule:positive'><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Theory]
    [InlineData("<g stroke='red'><text font-family='Unused'>x</text></g>")]
    [InlineData("<g opacity='0.5'><g opacity='0.5'><text font-family='Unused'>x</text></g></g>")]
    [InlineData("<text font-family='Unused' stroke='red'>x</text>")]
    [InlineData("<text font-family='Unused' fill='not-a-color'>x</text>")]
    public async Task Svg_based_snapshot_matches_paint_and_opacity_admission_before_font_acquisition(string content)
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'>" + content + "</svg>"), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_stylesheet_group_presentation_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><style>g { fill-rule: positive; }</style><g><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_an_unsupported_late_stylesheet_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1'/><style>rect:hover { fill: red; }</style><text font-family='Unused'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Lays_out_verified_glyph_advances_and_reports_missing_glyphs()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPositionedGlyph glyph = Assert.Single(SvgGlyphLayout.Layout(snapshot.Fonts[0], "A", 10, 2, 3, CancellationToken.None));
        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphLayout.Layout(snapshot.Fonts[0], "B", 10, 2, 3, CancellationToken.None));

        Assert.Equal((ushort)1, glyph.GlyphIndex);
        Assert.Equal(2, glyph.X);
        Assert.Equal(3, glyph.Y);
        Assert.Equal(6000d / 1024, glyph.Advance);
        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Glyph, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
    }

    [Fact]
    public async Task Text_run_glyph_diagnostics_retain_the_bounded_source_element_id()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 1))], CancellationToken.None);
        var style = new SvgTextStyle(new SvgFontFaceRequest("Example", 400, false), 10, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "caption");

        SvgException exception = Assert.Throws<SvgException>(() => SvgTextRunBuilder.Create(style, "B", snapshot, new RenderBudget(1_024), CancellationToken.None));

        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Glyph, diagnostic.Stage);
        Assert.Equal("caption", diagnostic.SourceElementId);
    }

    [Fact]
    public async Task Direct_text_missing_prepared_face_diagnostics_retain_the_bounded_source_element_id()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><text id='caption' font-family='Missing'>x</text></svg>"u8.ToArray(), options, CancellationToken.None));

        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Source, diagnostic.Stage);
        Assert.Equal("caption", diagnostic.SourceElementId);
    }

    [Fact]
    public async Task Lays_out_supplementary_scalars_with_utf16_span_bookkeeping()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFormat12Font())], CancellationToken.None);
        SvgPositionedGlyph glyph = Assert.Single(SvgGlyphLayout.Layout(snapshot.Fonts[0], "😀", 10, 2, 3, CancellationToken.None));
        SvgTextLetterSpacingSpan[] spans = [new(0, 2, 10), new(2, 4, 20)];
        using SvgPositionedGlyphStorage pair = SvgGlyphLayout.CreateStorage(snapshot.Fonts[0], "😀😀", 10, 2, 3, 2, spans, SvgTextAnchor.Start, new RenderBudget(1_000_000), string.Empty, null, CancellationToken.None);

        Assert.Equal((ushort)2, glyph.GlyphIndex);
        Assert.Equal(2, glyph.X);
        Assert.Equal(3, glyph.Y);
        Assert.Equal(6000d / 1024, glyph.Advance);
        Assert.Equal(2, pair.Glyphs[0].X);
        Assert.Equal(2 + 6000d / 1024 + 10, pair.Glyphs[1].X);
    }

    [Fact]
    public async Task Kerned_pair_adjustments_shift_rendered_pixels_relative_to_unadjusted_text()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'>AA</text></svg>");
        (SvgFontPackCatalog catalog1, CountingFontProvider provider1, CountingFontSource source1) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot1 = await SvgFontSnapshot.CreateAsync(input, catalog1, provider1, CancellationToken.None);
        ReadOnlyMemory<byte> png1 = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot1), CancellationToken.None);
        (SvgFontPackCatalog catalog2, CountingFontProvider provider2, CountingFontSource source2) = CreateExamplePack(CreateTriangleKernFont());
        using SvgFontSnapshot snapshot2 = await SvgFontSnapshot.CreateAsync(input, catalog2, provider2, CancellationToken.None);
        ReadOnlyMemory<byte> png2 = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot2), CancellationToken.None);

        Assert.False(png1.Span.SequenceEqual(png2.Span));
        Assert.Equal(0, DecodeAlpha(png1, 31, 1));
        Assert.True(DecodeAlpha(png1, 61, 1) > 0);
        Assert.True(DecodeAlpha(png2, 31, 1) > 0);
        Assert.Equal(0, DecodeAlpha(png2, 61, 1));
    }

    [Fact]
    public async Task Gpos_pair_adjustments_shift_rendered_pixels_relative_to_unadjusted_text()
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'>AA</text></svg>");
        (SvgFontPackCatalog catalog1, CountingFontProvider provider1, CountingFontSource source1) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot1 = await SvgFontSnapshot.CreateAsync(input, catalog1, provider1, CancellationToken.None);
        ReadOnlyMemory<byte> png1 = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot1), CancellationToken.None);
        (SvgFontPackCatalog catalog2, CountingFontProvider provider2, CountingFontSource source2) = CreateExamplePack(CreateTriangleGposFont());
        using SvgFontSnapshot snapshot2 = await SvgFontSnapshot.CreateAsync(input, catalog2, provider2, CancellationToken.None);
        ReadOnlyMemory<byte> png2 = SvgConverter.ConvertToPng(input, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot2), CancellationToken.None);

        Assert.False(png1.Span.SequenceEqual(png2.Span));
        Assert.Equal(0, DecodeAlpha(png1, 31, 1));
        Assert.True(DecodeAlpha(png1, 61, 1) > 0);
        Assert.True(DecodeAlpha(png2, 31, 1) > 0);
        Assert.Equal(0, DecodeAlpha(png2, 61, 1));
    }


    [Fact]
    public async Task Letter_spacing_shifts_rendered_glyphs_relative_to_unspaced_text()
    {
        byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'>AA</text></svg>");
        byte[] spaced = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3' letter-spacing='10'>AA</text></svg>");
        (SvgFontPackCatalog catalog1, CountingFontProvider provider1, CountingFontSource source1) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot1 = await SvgFontSnapshot.CreateAsync(plain, catalog1, provider1, CancellationToken.None);
        ReadOnlyMemory<byte> png1 = SvgConverter.ConvertToPng(plain, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot1), CancellationToken.None);
        (SvgFontPackCatalog catalog2, CountingFontProvider provider2, CountingFontSource source2) = CreateExamplePack(CreateTriangleFont());
        using SvgFontSnapshot snapshot2 = await SvgFontSnapshot.CreateAsync(spaced, catalog2, provider2, CancellationToken.None);
        ReadOnlyMemory<byte> png2 = SvgConverter.ConvertToPng(spaced, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot2), CancellationToken.None);

        Assert.False(png1.Span.SequenceEqual(png2.Span));
        Assert.True(DecodeAlpha(png1, 61, 1) > 0);
        Assert.Equal(0, DecodeAlpha(png2, 61, 1));
        Assert.True(DecodeAlpha(png2, 71, 1) > 0);
    }

    [Fact]
    public async Task Positions_direct_glyph_runs_for_admitted_text_anchors()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1024, 3, 2, 1))], CancellationToken.None);

        SvgPositionedGlyph[] middle = SvgGlyphLayout.Layout(snapshot.Fonts[0], "AA", 10, 20, 3, SvgTextAnchor.Middle, CancellationToken.None);
        SvgPositionedGlyph[] end = SvgGlyphLayout.Layout(snapshot.Fonts[0], "AA", 10, 20, 3, SvgTextAnchor.End, CancellationToken.None);

        Assert.Equal(20 - 6000d / 1024, middle[0].X);
        Assert.Equal(20, middle[1].X);
        Assert.Equal(20 - 12000d / 1024, end[0].X);
        Assert.Equal(20 - 6000d / 1024, end[1].X);
    }

    [Fact]
    public async Task Snapshot_finds_a_prepared_face_by_case_insensitive_request()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPreparedFont font = snapshot.GetFont(new SvgFontFaceRequest("example", 400, false));
        SvgException exception = Assert.Throws<SvgException>(() => snapshot.GetFont(new SvgFontFaceRequest("missing", 400, false)));
        SvgException longFamily = Assert.Throws<SvgException>(() => snapshot.GetFont(new SvgFontFaceRequest(new string('x', 257), 400, false)));

        Assert.Equal((ushort)3, font.GlyphCount);
        Assert.Equal(SvgErrorCode.UnresolvedResource, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Source, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
        Assert.Equal(SvgErrorCode.UnresolvedResource, longFamily.ErrorCode);
        Assert.Equal(256, Assert.IsType<SvgFontDiagnostic>(longFamily.FontDiagnostic).RequestedFamily.Length);
    }

    [Fact]
    public async Task Snapshot_font_lookup_charges_each_scanned_prepared_face()
    {
        byte[] regular = CreateFont(1024, 3, 2, 1);
        byte[] bold = CreateFont(1024, 3, 2, 1);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(regular), CreateResolution(bold, 700)], CancellationToken.None);

        SvgException exception = Assert.Throws<SvgException>(() => snapshot.GetFont(new SvgFontFaceRequest("Example", 700, false), string.Empty, new ParserWorkCounter(1), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => snapshot.GetFont(new SvgFontFaceRequest("Example", 700, false), string.Empty, new ParserWorkCounter(2), cancellation.Token));
    }

    [Fact]
    public async Task Render_options_retain_the_caller_owned_font_snapshot()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        Assert.Same(snapshot, options.FontSnapshot);
    }

    [Fact]
    public async Task Prepared_document_retains_a_lowered_direct_text_scene()
    {
        byte[] font = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(font));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + font.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource(font));
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);

        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        Array.Fill(svg, (byte)'x');
        ReadOnlyMemory<byte> png = document.ConvertToPng(CancellationToken.None);

        Assert.Equal(1, provider.RequestCount);
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Prepared_scaled_circle_matches_direct_conversion()
    {
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128' viewBox='0 0 8 8'><circle cx='4' cy='4' r='3' fill='red'/></svg>"u8.ToArray();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> direct = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(svg, catalog, provider, options, CancellationToken.None);
        ReadOnlyMemory<byte> prepared = document.ConvertToPng(CancellationToken.None);

        Assert.True(direct.Span.SequenceEqual(prepared.Span));
    }

    [Fact]
    public async Task Disposed_document_detaches_its_retained_scene()
    {
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(CreateSceneSvg());

        Assert.NotNull(document.RetainedSceneGeometries);
        document.Dispose();

        Assert.Null(document.RetainedSceneGeometries);
        document.Dispose();

        Assert.Null(document.RetainedSceneGeometries);
        Assert.Throws<ObjectDisposedException>(() => document.ConvertToPng(CancellationToken.None));
    }

    [Fact]
    public async Task Successful_outputs_detach_the_retained_scene()
    {
        byte[] svg = CreateSceneSvg();
        using SvgPreparedDocument owned = await CreateSceneDocumentAsync(svg);
        ReadOnlyMemory<byte> ownedPng = owned.ConvertToPng(CancellationToken.None);

        Assert.True(ownedPng.Length > 0);
        Assert.Null(owned.RetainedSceneGeometries);
        using SvgPreparedDocument callerMemory = await CreateSceneDocumentAsync(svg);
        byte[] destination = new byte[1_000_000];
        int written = callerMemory.ConvertToPng(destination, CancellationToken.None);

        Assert.True(written > 0);
        Assert.Null(callerMemory.RetainedSceneGeometries);
        using SvgPreparedDocument stream = await CreateSceneDocumentAsync(svg);
        using MemoryStream streamDestination = new();
        int streamed = stream.ConvertToPng(streamDestination, CancellationToken.None);

        Assert.True(streamed > 0);
        Assert.Null(stream.RetainedSceneGeometries);
        using SvgPreparedDocument ownedAsync = await CreateSceneDocumentAsync(svg);
        ReadOnlyMemory<byte> ownedAsyncPng = await ownedAsync.ConvertToPngAsync(CancellationToken.None);

        Assert.True(ownedAsyncPng.Length > 0);
        Assert.Null(ownedAsync.RetainedSceneGeometries);
        using SvgPreparedDocument callerMemoryAsync = await CreateSceneDocumentAsync(svg);
        byte[] asyncDestination = new byte[1_000_000];
        int asyncWritten = await callerMemoryAsync.ConvertToPngAsync(asyncDestination, CancellationToken.None);

        Assert.True(asyncWritten > 0);
        Assert.Null(callerMemoryAsync.RetainedSceneGeometries);
        using SvgPreparedDocument streamAsync = await CreateSceneDocumentAsync(svg);
        using MemoryStream asyncStreamDestination = new();
        int asyncStreamed = await streamAsync.ConvertToPngAsync(asyncStreamDestination, CancellationToken.None);

        Assert.True(asyncStreamed > 0);
        Assert.Null(streamAsync.RetainedSceneGeometries);
    }

    [Fact]
    public async Task Failed_outputs_detach_the_retained_scene()
    {
        byte[] svg = CreateSceneSvg();
        using SvgPreparedDocument callerMemory = await CreateSceneDocumentAsync(svg);

        Assert.Throws<SvgException>(() => callerMemory.ConvertToPng(new byte[1], CancellationToken.None));
        Assert.Null(callerMemory.RetainedSceneGeometries);
        using SvgPreparedDocument cancelled = await CreateSceneDocumentAsync(svg);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => cancelled.ConvertToPng(cancellation.Token));
        Assert.Null(cancelled.RetainedSceneGeometries);
        using SvgPreparedDocument syncStream = await CreateSceneDocumentAsync(svg);
        using FailingSceneWriteStream syncDestination = new();

        Assert.Throws<IOException>(() => syncStream.ConvertToPng(syncDestination, CancellationToken.None));
        Assert.Null(syncStream.RetainedSceneGeometries);
        using SvgPreparedDocument asyncStream = await CreateSceneDocumentAsync(svg);
        using FailingSceneWriteStream asyncDestination = new();

        await Assert.ThrowsAsync<IOException>(async () => await asyncStream.ConvertToPngAsync(asyncDestination, CancellationToken.None));
        Assert.Null(asyncStream.RetainedSceneGeometries);
        using SvgPreparedDocument asyncCancelled = await CreateSceneDocumentAsync(svg);
        using CancellationTokenSource asyncCancellation = new();
        asyncCancellation.Cancel();

        OperationCanceledException asyncFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await asyncCancelled.ConvertToPngAsync(asyncCancellation.Token));

        Assert.Equal(asyncCancellation.Token, asyncFailure.CancellationToken);
        Assert.Null(asyncCancelled.RetainedSceneGeometries);
        using SvgPreparedDocument memorySync = await CreateSceneDocumentAsync(svg);
        using CancellationTokenSource memoryCancellation = new();
        memoryCancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => memorySync.ConvertToPng(new byte[1_000_000], memoryCancellation.Token));
        Assert.Null(memorySync.RetainedSceneGeometries);
        using SvgPreparedDocument streamSync = await CreateSceneDocumentAsync(svg);
        using CancellationTokenSource streamCancellation = new();
        streamCancellation.Cancel();
        using MemoryStream streamDestination = new();

        Assert.Throws<OperationCanceledException>(() => streamSync.ConvertToPng(streamDestination, streamCancellation.Token));
        Assert.Null(streamSync.RetainedSceneGeometries);
        using SvgPreparedDocument memoryAsync = await CreateSceneDocumentAsync(svg);
        using CancellationTokenSource memoryAsyncCancellation = new();
        memoryAsyncCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await memoryAsync.ConvertToPngAsync(new byte[1_000_000], memoryAsyncCancellation.Token));
        Assert.Null(memoryAsync.RetainedSceneGeometries);
        using SvgPreparedDocument streamAsync = await CreateSceneDocumentAsync(svg);
        using CancellationTokenSource streamAsyncCancellation = new();
        streamAsyncCancellation.Cancel();
        using MemoryStream asyncStreamDestination = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await streamAsync.ConvertToPngAsync(asyncStreamDestination, streamAsyncCancellation.Token));
        Assert.Null(streamAsync.RetainedSceneGeometries);
    }

    [Fact]
    public async Task Converted_document_releases_its_geometry_arrays_for_collection()
    {
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(CreateSceneSvg());
        WeakReference geometries = TrackSceneArrays(document);
        ReadOnlyMemory<byte> png = document.ConvertToPng(CancellationToken.None);

        Assert.True(png.Length > 0);
        CollectAggressively();

        Assert.False(geometries.IsAlive);
    }

    [Fact]
    public async Task Disposed_document_releases_its_geometry_arrays_for_collection()
    {
        using SvgPreparedDocument document = await CreateSceneDocumentAsync(CreateSceneSvg());
        WeakReference geometries = TrackSceneArrays(document);
        document.Dispose();
        CollectAggressively();

        Assert.False(geometries.IsAlive);
    }

    private static byte[] CreateSceneSvg()
    {
        System.Text.StringBuilder scene = new("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='150'>");
        for (int index = 0; index < 1000; index++)
        {
            scene.Append("<rect x='").Append(index % 300).Append("' y='").Append(index % 150).Append("' width='1' height='1' fill='red'/>");
        }

        scene.Append("</svg>");
        return System.Text.Encoding.UTF8.GetBytes(scene.ToString());
    }

    private static async Task<SvgPreparedDocument> CreateSceneDocumentAsync(byte[] svg)
    {
        (SvgFontPackCatalog catalog, CountingFontProvider provider, CountingFontSource source) = CreateExamplePack(CreateTriangleFont());
        return await SvgPreparedDocument.CreateAsync(svg, catalog, provider, new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TrackSceneArrays(SvgPreparedDocument document) => new(document.RetainedSceneGeometries);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectAggressively()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    private sealed class FailingSceneWriteStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("destination write failed");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => ValueTask.FromException(new IOException("destination write failed"));
    }
    [Fact]
    public async Task Svg_based_snapshot_skips_whitespace_only_direct_text_before_acquisition()
    {
        byte[] font = CreateTriangleFont();
        string hash = Convert.ToHexString(SHA256.HashData(font));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + font.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var provider = new CountingFontProvider(new CountingFontSource(font));
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'> \t\r\n </text></svg>"u8.ToArray();

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, provider.RequestCount);
    }

    [Theory]
    [InlineData(" \tA \r\n")]
    [InlineData(" \t<![CDATA[A]]> \r\n")]
    public async Task Direct_text_normalizes_default_whitespace_before_layout(string content)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>" + content + "</text></svg>");

        ReadOnlyMemory<byte> normalized = SvgConverter.ConvertToPng(svg, options, CancellationToken.None);
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expected.ToArray(), normalized.ToArray());
    }

    [Fact]
    public async Task Direct_text_honors_admitted_xml_space_modes()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> collapsed = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='default'> </text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> preserved = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'>\t</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(0, DecodeAlpha(collapsed, 1, 1));
        Assert.True(DecodeAlpha(preserved, 1, 1) > 0);
    }

    [Fact]
    public async Task Direct_text_honors_bounded_css_whitespace_declarations()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> expectedPreserved = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'>\t</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expectedDefault = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'/></svg>"u8.ToArray(), options, CancellationToken.None);

        ReadOnlyMemory<byte> inline = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' style='white-space: pre'>\t</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> inherited = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g style='white-space: pre'><text font-family='Example' font-size='100' y='3'>\t</text></g></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> stylesheet = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>g { white-space: pre; }</style><g><text font-family='Example' font-size='100' y='3'>\t</text></g></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> textStylesheet = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>#label { white-space: pre; }</style><text id='label' font-family='Example' font-size='100' y='3'>\t</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> span = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan style='white-space: pre'>\t</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> spanStylesheet = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>#space { white-space: pre; }</style><text font-family='Example' font-size='100' y='3'><tspan id='space'>\t</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> spanInlineOverride = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><style>#space { white-space: pre; }</style><text font-family='Example' font-size='100' y='3'><tspan id='space' style='white-space: normal'>\t</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> cssOverride = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve' style='white-space: normal'>\t</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expectedPreserved.ToArray(), inline.ToArray());
        Assert.Equal(expectedPreserved.ToArray(), inherited.ToArray());
        Assert.Equal(expectedPreserved.ToArray(), stylesheet.ToArray());
        Assert.Equal(expectedPreserved.ToArray(), textStylesheet.ToArray());
        Assert.Equal(expectedPreserved.ToArray(), span.ToArray());
        Assert.Equal(expectedPreserved.ToArray(), spanStylesheet.ToArray());
        Assert.Equal(expectedDefault.ToArray(), spanInlineOverride.ToArray());
        Assert.Equal(expectedDefault.ToArray(), cssOverride.ToArray());
    }

    [Fact]
    public async Task Root_xml_space_seeds_descendant_whitespace_inheritance()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> rooted = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\" xml:space=\"preserve\"><text font-family=\"Example\" font-size=\"100\" y=\"3\">\t</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expectedPreserved = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><text font-family=\"Example\" font-size=\"100\" y=\"3\" xml:space=\"preserve\">\t</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expectedPreserved.ToArray(), rooted.ToArray());
    }

    [Fact]
    public async Task Root_inline_whitespace_overrides_root_xml_space()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> overridden = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\" xml:space=\"preserve\" style=\"white-space: normal\"><text font-family=\"Example\" font-size=\"100\" y=\"3\">\t</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expectedDefault = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><text font-family=\"Example\" font-size=\"100\" y=\"3\"/></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expectedDefault.ToArray(), overridden.ToArray());
    }

    [Fact]
    public async Task Group_xml_space_seeds_descendant_whitespace_inheritance()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> grouped = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><g xml:space=\"preserve\"><text font-family=\"Example\" font-size=\"100\" y=\"3\">\t</text></g></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expectedPreserved = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><text font-family=\"Example\" font-size=\"100\" y=\"3\" xml:space=\"preserve\">\t</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expectedPreserved.ToArray(), grouped.ToArray());
    }

    [Fact]
    public async Task Group_inline_whitespace_overrides_group_xml_space()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> overridden = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><g xml:space=\"preserve\" style=\"white-space: normal\"><text font-family=\"Example\" font-size=\"100\" y=\"3\">\t</text></g></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expectedDefault = SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><text font-family=\"Example\" font-size=\"100\" y=\"3\"/></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expectedDefault.ToArray(), overridden.ToArray());
    }

    [Fact]
    public async Task Group_invalid_xml_space_fails_fast()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        SvgException invalid = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"><g xml:space=\"collapse\"><text font-family=\"Example\" font-size=\"100\" y=\"3\">A</text></g></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, invalid.ErrorCode);
    }

    [Fact]
    public async Task Direct_text_spans_inherit_and_override_admitted_xml_space_modes()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        ReadOnlyMemory<byte> expectedPreserved = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'>  </text></svg>"u8.ToArray(), options, CancellationToken.None);

        ReadOnlyMemory<byte> nestedInherited = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'><tspan>\t\t</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> nestedPreserve = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'><tspan xml:space='preserve'>\t\t</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> defaultOverride = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'><tspan xml:space='default'> </tspan></text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expectedDefault = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'/></svg>"u8.ToArray(), options, CancellationToken.None);
        SvgException invalid = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'><tspan xml:space='collapse'>A</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(expectedPreserved.ToArray(), nestedInherited.ToArray());
        Assert.Equal(expectedPreserved.ToArray(), nestedPreserve.ToArray());
        Assert.Equal(expectedDefault.ToArray(), defaultOverride.ToArray());
        Assert.Equal(SvgErrorCode.MalformedInput, invalid.ErrorCode);
    }

    [Fact]
    public async Task Nested_default_xml_space_trims_its_own_visible_text_edges()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> nested = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'>A<tspan xml:space='default'> A </tspan>A</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='preserve'>AAA</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expected.ToArray(), nested.ToArray());
    }

    [Fact]
    public async Task Direct_text_reuses_a_contiguous_decoded_glyph_outline()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        int expansionCount = 0;
        using IDisposable probe = GlyphExpansionProbe.Observe(() => expansionCount++);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='5'><text font-family='Example' font-size='100' y='3'>AAAA</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(1, expansionCount);
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
        Assert.True(DecodeAlpha(png, 61, 1) > 0);
    }

    [Fact]
    public async Task Direct_text_applies_scalar_letter_spacing_between_glyphs()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> direct = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3' style='letter-spacing: 50'>AA</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> inherited = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><g style='letter-spacing: 50'><text font-family='Example' font-size='100' y='3'>AA</text></g></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> stylesheet = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><style>#label { letter-spacing: 50; }</style><text id='label' font-family='Example' font-size='100' y='3'>AA</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> classStylesheet = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><style>.label { letter-spacing: 50; }</style><text class='label' font-family='Example' font-size='100' y='3'>AA</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.True(DecodeAlpha(direct, 1, 1) > 0);
        Assert.True(DecodeAlpha(direct, 111, 1) > 0);
        Assert.Equal(direct.ToArray(), inherited.ToArray());
        Assert.Equal(direct.ToArray(), stylesheet.ToArray());
        Assert.Equal(direct.ToArray(), classStylesheet.ToArray());
    }

    [Fact]
    public async Task Nested_tspan_applies_scalar_letter_spacing_without_changing_the_parent_text_chunk_anchor()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> presentation = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='400' height='5'><text font-family='Example' font-size='100' x='200' y='3' text-anchor='middle'>A<tspan letter-spacing='50'>AA</tspan>A</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> inline = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='400' height='5'><text font-family='Example' font-size='100' x='200' y='3' text-anchor='middle'>A<tspan style='letter-spacing: 50'>AA</tspan>A</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> stylesheet = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='400' height='5'><style>.wide { letter-spacing: 50; }</style><text font-family='Example' font-size='100' x='200' y='3' text-anchor='middle'>A<tspan class='wide'>AA</tspan>A</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.True(DecodeAlpha(presentation, 31, 1) > 0);
        Assert.True(DecodeAlpha(presentation, 91, 1) > 0);
        Assert.True(DecodeAlpha(presentation, 201, 1) > 0);
        Assert.True(DecodeAlpha(presentation, 311, 1) > 0);
        Assert.Equal(presentation.ToArray(), inline.ToArray());
        Assert.Equal(presentation.ToArray(), stylesheet.ToArray());
    }

    [Fact]
    public async Task Nested_tspan_applies_scalar_dx_and_dy_to_its_first_descendant_glyph()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='400' height='8'><text font-family='Example' font-size='100' x='200' y='3' text-anchor='middle'>A<tspan dx='50' dy='1'><tspan>A</tspan></tspan>A</text></svg>"u8.ToArray(), options, CancellationToken.None);
        using SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 100, 200, 3, 0, [], [new SvgTextPositionAdjustment(1, 50, 1)], SvgTextAnchor.Middle, new RenderBudget(1_024), string.Empty, null, CancellationToken.None);

        Assert.Equal(110, glyphs.Glyphs[0].X);
        Assert.Equal(220, glyphs.Glyphs[1].X);
        Assert.Equal(280, glyphs.Glyphs[2].X);
        Assert.Equal(3, glyphs.Glyphs[0].Y);
        Assert.Equal(4, glyphs.Glyphs[1].Y);
        Assert.Equal(4, glyphs.Glyphs[2].Y);
        Assert.True(DecodeAlpha(png, 111, 1) > 0);
        Assert.True(DecodeAlpha(png, 221, 2) > 0);
        Assert.True(DecodeAlpha(png, 281, 2) > 0);
    }

    [Theory]
    [InlineData("start", 200, 310, 370)]
    [InlineData("middle", 170, 250, 310)]
    [InlineData("end", 140, 190, 250)]
    public async Task Absolute_span_coordinates_start_separately_anchored_chunks(string anchorName, double firstX, double secondX, double thirdX)
    {
        SvgTextAnchor anchor = Enum.Parse<SvgTextAnchor>(anchorName, true);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new(128);
        using SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 100, 200, 3, 0, [], [new(1, 300, 6, 10, -1)], anchor, budget, string.Empty, null, CancellationToken.None);

        Assert.Equal(firstX, glyphs.Glyphs[0].X);
        Assert.Equal(secondX, glyphs.Glyphs[1].X);
        Assert.Equal(thirdX, glyphs.Glyphs[2].X);
        Assert.Equal([3d, 5d, 5d], glyphs.Glyphs.ToArray().Select(static glyph => glyph.Y));
        glyphs.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("start", 200, 260, 320)]
    [InlineData("middle", 170, 200, 260)]
    [InlineData("end", 140, 140, 200)]
    public async Task A_y_only_span_reset_preserves_the_unanchored_current_x(string anchorName, double firstX, double secondX, double thirdX)
    {
        SvgTextAnchor anchor = Enum.Parse<SvgTextAnchor>(anchorName, true);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        using SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 100, 200, 3, 0, [], [new(1, null, 6, 0, 0)], anchor, new RenderBudget(128), string.Empty, null, CancellationToken.None);

        Assert.Equal([firstX, secondX, thirdX], glyphs.Glyphs.ToArray().Select(static glyph => glyph.X));
        Assert.Equal([3d, 6d, 6d], glyphs.Glyphs.ToArray().Select(static glyph => glyph.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absolute_chunk_boundaries_stop_pair_adjustment_without_disabling_pairs_inside_the_next_chunk(bool gpos)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(gpos ? CreateTriangleGposFont() : CreateTriangleKernFont())], CancellationToken.None);
        using SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 100, 0, 3, 0, [], [new(1, null, 6, 0, 0)], SvgTextAnchor.Start, new RenderBudget(128), string.Empty, null, CancellationToken.None);

        Assert.Equal([0d, 60d, 90d], glyphs.Glyphs.ToArray().Select(static glyph => glyph.X));
    }

    [Fact]
    public async Task Absolute_and_relative_span_coordinates_continue_into_later_parent_text()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] actual = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='10'><text font-family='Example' font-size='100' y='3'>A<tspan x='120' y='6'><tspan dx='5' dy='1'>A</tspan>A</tspan>A</text></svg>"u8.ToArray(), options, CancellationToken.None).ToArray();
        byte[] expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='10'><text font-family='Example' font-size='100' y='3'>A</text><text font-family='Example' font-size='100' x='125' y='7'>A</text><text font-family='Example' font-size='100' x='185' y='7'>A</text><text font-family='Example' font-size='100' x='245' y='7'>A</text></svg>"u8.ToArray(), options, CancellationToken.None).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Direct_text_applies_validated_legacy_kerning_between_adjacent_glyphs()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleKernFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'>AA</text></svg>"u8.ToArray(), options, CancellationToken.None);
        using SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AA", 100, 0, 3, SvgTextAnchor.Start, new RenderBudget(1_024), CancellationToken.None);

        Assert.Equal(30, glyphs.Glyphs[1].X);
        Assert.True(DecodeAlpha(png, 31, 1) > 0);
        Assert.Equal(0, DecodeAlpha(png, 61, 1));

        byte[] unsupported = CreateTriangleKernFont();
        OpenTypeKernTable kern = Assert.IsType<OpenTypeKernTable>(OpenTypeFont.ParseMetrics(unsupported, CancellationToken.None).Kern);
        WriteUInt16(unsupported, kern.Range.Offset + 4, 1);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(unsupported, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Direct_text_applies_validated_GPOS_pair_adjustments_between_adjacent_glyphs()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleGposFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='5'><text font-family='Example' font-size='100' y='3'>AA</text></svg>"u8.ToArray(), options, CancellationToken.None);
        using SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AA", 100, 0, 3, SvgTextAnchor.Start, new RenderBudget(1_024), CancellationToken.None);

        Assert.Equal(30, glyphs.Glyphs[1].X);
        Assert.True(DecodeAlpha(png, 31, 1) > 0);
        Assert.Equal(0, DecodeAlpha(png, 61, 1));

        byte[] unsupported = CreateTriangleGposFont();
        OpenTypeGposPairTable gpos = Assert.IsType<OpenTypeGposPairTable>(OpenTypeFont.ParseMetrics(unsupported, CancellationToken.None).Gpos);
        WriteUInt16(unsupported, gpos.PairPosOffset + 4, 8);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(unsupported, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Default_text_preserves_inter_word_whitespace_around_a_visible_nested_span()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleAAndSpaceFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> nested = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='5'><text font-family='Example' font-size='100' y='3'>A <tspan>A</tspan> A</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='5'><text font-family='Example' font-size='100' y='3'>A A A</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expected.ToArray(), nested.ToArray());
    }

    [Fact]
    public async Task Converts_direct_text_and_its_immediate_shape_sibling_through_every_public_output_form()
    {
        byte[] font = CreateTriangleFont();
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family=\"'Example'\" font-size='100' y='3'>A</text><rect width='1' height='1' fill='red'/></svg>"u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        OpenTypeQuadraticSegment[] decoded = OpenTypeFont.GetGlyphContours(font, 1, CancellationToken.None);
        Assert.Equal([new OpenTypeOutlinePoint(10, 10), new OpenTypeOutlinePoint(20, 10), new OpenTypeOutlinePoint(10, 20)], decoded.Select(segment => segment.Start));
        Assert.Equal(decoded[0].Start, decoded[^1].End);
        using (SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, new RenderBudget(SvgRenderLimits.Default.MaximumAccountedBytes), snapshot, CancellationToken.None))
        {
            Assert.Contains(root.Geometries, geometry => geometry.Kind == SvgGeometryKind.GlyphContours);
        }

        byte[] owned = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();
        byte[] memory = new byte[owned.Length];
        int memoryLength = SvgConverter.ConvertToPng(svg, memory, options, CancellationToken.None);
        using var stream = new MemoryStream();
        int streamLength = SvgConverter.ConvertToPng(svg, stream, options, CancellationToken.None);
        byte[] asyncOwned = (await SvgConverter.ConvertToPngAsync(svg, options, CancellationToken.None)).ToArray();
        byte[] asyncMemory = new byte[owned.Length];
        int asyncMemoryLength = await SvgConverter.ConvertToPngAsync(svg, asyncMemory, options, CancellationToken.None);
        using var asyncStream = new MemoryStream();
        int asyncStreamLength = await SvgConverter.ConvertToPngAsync(svg, asyncStream, options, CancellationToken.None);

        Assert.Equal(255, DecodeAlpha(owned, 0, 0));
        Assert.True(DecodeAlpha(owned, 1, 1) > 0);
        Assert.Equal(owned, memory.AsSpan(0, memoryLength).ToArray());
        Assert.Equal(owned, stream.ToArray());
        Assert.Equal(owned, asyncOwned);
        Assert.Equal(owned, asyncMemory.AsSpan(0, asyncMemoryLength).ToArray());
        Assert.Equal(owned, asyncStream.ToArray());
        Assert.Equal(owned.Length, streamLength);
        Assert.Equal(owned.Length, asyncStreamLength);
    }

    [Fact]
    public async Task Preserves_an_immediate_shape_sibling_after_direct_cdata_text()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><![CDATA[A]]></text><rect width='1' height='1' fill='red'/></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.Equal(255, DecodeAlpha(png, 0, 0));
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Preserves_an_outer_shape_sibling_after_direct_text_in_a_group()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><g><text font-family='Example' font-size='100' y='3'>A</text></g><rect width='1' height='1' fill='red'/></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.Equal(255, DecodeAlpha(png, 0, 0));
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Preserves_an_immediate_shape_sibling_after_direct_text_with_a_comment()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A<!-- retained as ignored XML markup --></text><rect width='1' height='1' fill='red'/></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.Equal(255, DecodeAlpha(png, 0, 0));
        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Rejects_a_malformed_tail_after_direct_text()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text><rect width='1' height='1' fill='red'/>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_an_unsupported_sibling_after_direct_text()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text><foreignObject/></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public async Task Converts_direct_text_with_admitted_inline_font_properties()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text style='font-family: Example; font-size: 100' y='3'>A</text></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Converts_direct_text_with_root_and_group_inherited_font_properties()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5' font-family='Example' font-weight='400' font-size='100'><g style='font-style: normal'><text y='3'>A</text></g></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Concurrent_direct_text_conversions_share_an_immutable_snapshot_safely()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        ReadOnlyMemory<byte>[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SvgConverter.ConvertToPngAsync(svg, options, CancellationToken.None)));

        Assert.All(results, result => Assert.Equal(expected, result.ToArray()));
    }

    [Fact]
    public async Task Converts_direct_text_with_a_true_quadratic_glyph_segment()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        font[glyph.Offset + 15] = 0x32;
        OpenTypeQuadraticSegment quadratic = Assert.Single(OpenTypeFont.GetGlyphContours(font, 1, CancellationToken.None), segment => segment.Control != segment.End);
        Assert.Equal(new OpenTypeOutlinePoint(20, 10), quadratic.Control);
        Assert.Equal(new OpenTypeOutlinePoint(10, 20), quadratic.End);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Converts_direct_text_with_fractional_coordinates()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100.3' x='0.1' y='3.1'>A</text></svg>"u8.ToArray();

        byte[] png = SvgConverter.ConvertToPng(svg, options, CancellationToken.None).ToArray();

        Assert.True(DecodeAlpha(png, 1, 1) > 0);
    }

    [Fact]
    public async Task Categorizes_overflowing_direct_text_offsets_as_a_limit()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' x='1e308' dx='1e308'>A</text></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_nonfinite_transformed_glyph_coordinates_before_rasterization()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='1e308' y='-1.79e308'>A</text></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Retained_glyph_contours_hold_and_release_scene_budget_ownership()
    {
        SvgGlyphQuadraticContour[] contours = [new SvgGlyphQuadraticContour([new SvgGlyphQuadraticSegment(new(0, 0), new(1, 0), new(1, 0))])];
        RenderBudget budget = new(1_024);

        using (SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, new SvgColor(0, 0, 0, 255), budget))
        {
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Failed_scene_transfer_releases_detached_glyph_contour_ownership()
    {
        SvgGlyphQuadraticContour[] contours = [new SvgGlyphQuadraticContour([new SvgGlyphQuadraticSegment(new(0, 0), new(1, 0), new(1, 0))])];
        RenderBudget budget = new(10_000);
        using (var scene = new SvgSceneBuilder(budget, new ParserWorkCounter(1), CancellationToken.None))
        {
            SvgGlyphGeometry glyph = SvgGlyphGeometry.Create(contours, new SvgColor(0, 0, 0, 255), budget);
            scene.AddGeometry(SvgGeometry.FromGlyphContours(glyph));

            Assert.Throws<SvgException>(() => scene.Build());
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Glyph_outline_decoding_and_final_contours_observe_the_memory_limit()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgPositionedGlyph positioned = Assert.Single(SvgGlyphLayout.Layout(snapshot.Fonts[0], "A", 100, 0, 0, CancellationToken.None));
        RenderBudget budget = new(350);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], positioned, 100, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Glyph_expansion_charges_the_shared_conversion_work_counter_before_decoding()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, new RenderBudget(10_000), new ParserWorkCounter(0), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Composite_expansion_charges_empty_child_glyph_visits_to_the_shared_work_counter()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 3);
        WriteUInt16(font, composite.Offset + 12, 0);
        WriteInt16(font, composite.Offset + 14, 0);
        WriteInt16(font, composite.Offset + 16, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, new RenderBudget(10_000), new ParserWorkCounter(2), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public void Branching_composite_expansion_charges_active_scans_and_each_empty_leaf_visit_to_the_shared_work_counter()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange firstComposite = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        OpenTypeTableRange rootComposite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, firstComposite.Offset, -1);
        WriteUInt16(font, firstComposite.Offset + 10, 35);
        WriteUInt16(font, firstComposite.Offset + 12, 0);
        WriteInt16(font, firstComposite.Offset + 14, 0);
        WriteInt16(font, firstComposite.Offset + 16, 0);
        WriteUInt16(font, firstComposite.Offset + 18, 3);
        WriteUInt16(font, firstComposite.Offset + 20, 0);
        WriteInt16(font, firstComposite.Offset + 22, 0);
        WriteInt16(font, firstComposite.Offset + 24, 0);
        WriteInt16(font, rootComposite.Offset, -1);
        WriteUInt16(font, rootComposite.Offset + 10, 3);
        WriteUInt16(font, rootComposite.Offset + 12, 1);
        WriteInt16(font, rootComposite.Offset + 14, 0);
        WriteInt16(font, rootComposite.Offset + 16, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        RenderBudget successfulBudget = new(10_000);
        var successfulWork = new ParserWorkCounter(21);

        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetGlyphContours(font, 2, metrics, successfulBudget, successfulWork, CancellationToken.None))
        {
            Assert.Empty(contours.Segments);
        }

        Assert.Equal(21, successfulWork.Used);
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(10_000);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, deniedBudget, new ParserWorkCounter(20), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Glyph_expansion_observes_cancellation_at_its_entry_checkpoint()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        using IDisposable probe = GlyphExpansionProbe.Arm(cancellation.Cancel);

        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, new RenderBudget(10_000), new ParserWorkCounter(100), cancellation.Token));
    }

    [Fact]
    public void Glyph_expansion_charges_transformed_segment_insertion_to_the_shared_work_counter()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        RenderBudget successfulBudget = new(10_000);
        var successfulWork = new ParserWorkCounter(30);

        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetGlyphContours(font, 1, metrics, successfulBudget, successfulWork, CancellationToken.None))
        {
            Assert.Equal(3, contours.Segments.Length);
        }

        Assert.Equal(30, successfulWork.Used);
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(10_000);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, deniedBudget, new ParserWorkCounter(29), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Glyph_expansion_observes_cancellation_after_exact_segment_allocation_before_copying()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        RenderBudget budget = new(10_000);
        using CancellationTokenSource cancellation = new();
        bool reservedExactStorage = false;
        using IDisposable probe = GlyphDecoderBudgetProbe.Arm((phase, _) =>
        {
            if (phase != GlyphDecoderBudgetPhase.ExpandedSegmentExact) return;
            reservedExactStorage = true;
            cancellation.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, budget, new ParserWorkCounter(long.MaxValue), cancellation.Token));

        Assert.True(reservedExactStorage);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Glyph_expansion_observes_cancellation_after_exact_contour_boundary_allocation_before_copying()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        RenderBudget budget = new(10_000);
        using CancellationTokenSource cancellation = new();
        bool reservedExactStorage = false;
        using IDisposable probe = GlyphDecoderBudgetProbe.Arm((phase, _) =>
        {
            if (phase != GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact) return;
            reservedExactStorage = true;
            cancellation.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, budget, new ParserWorkCounter(long.MaxValue), cancellation.Token));

        Assert.True(reservedExactStorage);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Glyph_outline_charges_transformation_to_the_shared_work_boundary()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        var glyph = new SvgPositionedGlyph(1, 0, 0, 1);
        RenderBudget successfulBudget = new(10_000);
        var successfulWork = new ParserWorkCounter(long.MaxValue);

        using (SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font, glyph, 1, successfulBudget, successfulWork, CancellationToken.None))
        {
            SvgGlyphQuadraticContour contour = Assert.Single(contours.Contours);
            Assert.Equal(3, contour.Segments.Length);
        }
        long requiredWork = successfulWork.Used;
        Assert.True(requiredWork > 0);
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        var exactWork = new ParserWorkCounter(requiredWork);
        using (SvgGlyphContourStorage exact = SvgGlyphOutline.GetContourStorage(font, glyph, 1, successfulBudget, exactWork, CancellationToken.None))
        {
            Assert.Equal(3, Assert.Single(exact.Contours).Segments.Length);
            Assert.Equal(requiredWork, exactWork.Used);
        }
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(10_000);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphOutline.GetContourStorage(font, glyph, 1, deniedBudget, new ParserWorkCounter(requiredWork - 1), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Glyph_outline_observes_cancellation_during_transformation()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        RenderBudget budget = new(10_000);
        using CancellationTokenSource cancellation = new();
        bool transformedSegment = false;
        using IDisposable probe = GlyphOutlineProbe.Arm(() =>
        {
            transformedSegment = true;
            cancellation.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphOutline.GetContourStorage(font, new SvgPositionedGlyph(1, 0, 0, 1), 1, budget, new ParserWorkCounter(long.MaxValue), cancellation.Token));

        Assert.True(transformedSegment);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Direct_text_lowering_observes_the_complete_parser_work_boundary()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 100_000);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();

        using SvgRoot root = SvgRootParser.Parse(svg, limits, new RenderBudget(limits.MaximumAccountedBytes), snapshot, CancellationToken.None);

        int requiredWork = checked((int)root.ParserWorkUnits);
        Assert.InRange(requiredWork, 1, limits.MaximumWorkUnits);
        SvgRenderLimits exactLimits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, requiredWork);
        using SvgRoot exact = SvgRootParser.Parse(svg, exactLimits, new RenderBudget(exactLimits.MaximumAccountedBytes), snapshot, CancellationToken.None);

        Assert.Equal(requiredWork, exact.ParserWorkUnits);
        SvgRenderLimits deniedLimits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, requiredWork - 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, deniedLimits, new RenderBudget(deniedLimits.MaximumAccountedBytes), snapshot, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Direct_text_lowering_releases_scene_and_parser_reservations_when_transformation_is_cancelled()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 100_000);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using CancellationTokenSource cancellation = new();
        bool transformedSegment = false;
        using IDisposable probe = GlyphOutlineProbe.Arm(() =>
        {
            transformedSegment = true;
            cancellation.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(svg, limits, budget, snapshot, cancellation.Token));

        Assert.True(transformedSegment);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Glyph_outline_transfers_final_contour_scene_ownership_to_retained_geometry()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var glyph = new SvgPositionedGlyph(1, 0, 0, 1);
        RenderBudget budget = new(10_000);
        using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], glyph, 1, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None);

        long chargedContours = budget.Snapshot().CurrentBytes;
        long allocationPeak = budget.Snapshot().PeakBytes;
        Assert.True(chargedContours > 0);
        Assert.True(budget.Snapshot().BytesByCategory[(int)SvgMemoryCategory.Scene] > 0);
        using (SvgGlyphGeometry retained = SvgGlyphGeometry.Create(contours, new SvgColor(0, 0, 0, 255)))
        {
            Assert.Equal(chargedContours, budget.Snapshot().CurrentBytes);
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(allocationPeak - 1);
        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], glyph, 1, deniedBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Budgeted_simple_glyph_decoder_observes_and_releases_each_owned_temporary_array()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        RenderBudget budget = new(10_000);
        var observations = new List<(GlyphDecoderBudgetPhase Phase, RenderBudgetSnapshot Snapshot)>();

        using (GlyphDecoderBudgetProbe.Arm((phase, snapshot) => observations.Add((phase, snapshot))))
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetGlyphContours(font, 1, metrics, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(3, contours.Segments.Length);
            Assert.All(observations, observation => Assert.True(observation.Snapshot.CurrentBytes > 0));
            Assert.All(observations, observation => Assert.True(observation.Snapshot.BytesByCategory[(int)SvgMemoryCategory.Geometry] > 0));
        }

        Assert.Equal(
            [
                GlyphDecoderBudgetPhase.ActiveGlyphs,
                GlyphDecoderBudgetPhase.SimpleEndpoints,
                GlyphDecoderBudgetPhase.SimpleFlags,
                GlyphDecoderBudgetPhase.SimplePoints,
                GlyphDecoderBudgetPhase.SimpleSegmentCapacity,
                GlyphDecoderBudgetPhase.SimpleContourBoundaryCapacity,
                GlyphDecoderBudgetPhase.SimpleSegmentExact,
                GlyphDecoderBudgetPhase.SimpleContourBoundaryExact,
                GlyphDecoderBudgetPhase.ExpandedSegmentCapacity,
                GlyphDecoderBudgetPhase.ExpandedContourBoundaryCapacity,
                GlyphDecoderBudgetPhase.ExpandedSegmentExact,
                GlyphDecoderBudgetPhase.ExpandedContourBoundaryExact
            ],
            observations.Select(observation => observation.Phase));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        foreach ((GlyphDecoderBudgetPhase phase, RenderBudgetSnapshot snapshot) in observations.Take(8))
        {
            RenderBudget deniedBudget = new(snapshot.CurrentBytes - 1);
            var deniedPhases = new List<GlyphDecoderBudgetPhase>();

            using (GlyphDecoderBudgetProbe.Arm((observedPhase, _) => deniedPhases.Add(observedPhase)))
            {
                SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 1, metrics, deniedBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            }

            Assert.DoesNotContain(phase, deniedPhases);
            Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Budgeted_composite_decoder_observes_and_releases_component_storage()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeTableRange composite = OpenTypeFont.GetGlyphDataRange(font, 2, CancellationToken.None);
        WriteInt16(font, composite.Offset, -1);
        WriteUInt16(font, composite.Offset + 10, 3);
        WriteUInt16(font, composite.Offset + 12, 1);
        WriteInt16(font, composite.Offset + 14, 0);
        WriteInt16(font, composite.Offset + 16, 0);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        RenderBudget budget = new(10_000);
        var observations = new List<(GlyphDecoderBudgetPhase Phase, RenderBudgetSnapshot Snapshot)>();

        using (GlyphDecoderBudgetProbe.Arm((phase, snapshot) => observations.Add((phase, snapshot))))
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetGlyphContours(font, 2, metrics, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(3, contours.Segments.Length);
            Assert.Contains(observations, observation => observation.Phase == GlyphDecoderBudgetPhase.CompositeComponentsCapacity && observation.Snapshot.CurrentBytes > 0);
            Assert.Contains(observations, observation => observation.Phase == GlyphDecoderBudgetPhase.CompositeComponentsExact && observation.Snapshot.CurrentBytes > 0);
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        foreach ((GlyphDecoderBudgetPhase phase, RenderBudgetSnapshot snapshot) in observations.Where(observation => observation.Phase is GlyphDecoderBudgetPhase.CompositeComponentsCapacity or GlyphDecoderBudgetPhase.CompositeComponentsExact))
        {
            RenderBudget deniedBudget = new(snapshot.CurrentBytes - 1);
            var deniedPhases = new List<GlyphDecoderBudgetPhase>();

            using (GlyphDecoderBudgetProbe.Arm((observedPhase, _) => deniedPhases.Add(observedPhase)))
            {
                SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, deniedBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None));
                Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            }

            Assert.DoesNotContain(phase, deniedPhases);
            Assert.Equal(0, deniedBudget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public async Task Glyph_contour_transfer_rejects_reuse_after_transfer_or_disposal()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var glyph = new SvgPositionedGlyph(1, 0, 0, 1);
        RenderBudget transferredBudget = new(10_000);
        using SvgGlyphContourStorage transferred = SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], glyph, 1, transferredBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None);
        using (SvgGlyphGeometry retained = SvgGlyphGeometry.Create(transferred, new SvgColor(0, 0, 0, 255)))
        {
            Assert.Throws<InvalidOperationException>(() => SvgGlyphGeometry.Create(transferred, new SvgColor(0, 0, 0, 255)));
            Assert.Throws<InvalidOperationException>(() => _ = transferred.Contours);
            Assert.True(transferredBudget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, transferredBudget.Snapshot().CurrentBytes);

        RenderBudget disposedBudget = new(10_000);
        using SvgGlyphContourStorage disposed = SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], glyph, 1, disposedBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None);
        disposed.Dispose();

        Assert.Throws<InvalidOperationException>(() => SvgGlyphGeometry.Create(disposed, new SvgColor(0, 0, 0, 255)));
        Assert.Throws<InvalidOperationException>(() => _ = disposed.Contours);
        Assert.Equal(0, disposedBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Failed_scene_insertion_releases_transferred_glyph_contour_ownership()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var glyph = new SvgPositionedGlyph(1, 0, 0, 1);
        RenderBudget measurementBudget = new(10_000);
        long outlinePeak;
        long retainedBytes;
        using (SvgGlyphContourStorage measured = SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], glyph, 1, measurementBudget, new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            outlinePeak = measurementBudget.Snapshot().PeakBytes;
            retainedBytes = measurementBudget.Snapshot().CurrentBytes;
        }
        Assert.Equal(0, measurementBudget.Snapshot().CurrentBytes);

        long geometryCapacity = ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(4);
        RenderBudget budget = new(Math.Max(outlinePeak, retainedBytes + geometryCapacity));
        using (SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(snapshot.Fonts[0], glyph, 1, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        using (var scene = new SvgSceneBuilder(budget, new ParserWorkCounter(1), CancellationToken.None))
        {
            SvgGlyphGeometry retained = SvgGlyphGeometry.Create(contours, new SvgColor(0, 0, 0, 255));
            SvgException exception = Assert.Throws<SvgException>(() => scene.AddGeometry(SvgGeometry.FromGlyphContours(retained)));

            Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
            Assert.Throws<InvalidOperationException>(() => SvgGlyphGeometry.Create(contours, new SvgColor(0, 0, 0, 255)));
            retained.Dispose();
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Parser_failure_after_retaining_a_glyph_contour_releases_its_scene_budget()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new(1_000_000);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' font-size='100'>A</text><rect x='1%' width='1'/></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgRootParser.Parse(svg, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Reads_direct_text_in_bounded_reader_chunks()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' font-size='100'>" + new string('A', 5_000) + "</text></svg>");

        using SvgRoot root = SvgRootParser.Parse(svg, SvgRenderLimits.Default, new RenderBudget(10_000_000), snapshot, CancellationToken.None);

        Assert.Equal(5_000, root.Geometries.Length);
    }

    [Fact]
    public async Task Charges_direct_glyph_raster_work_to_the_public_conversion_limit()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 100);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, limits, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("work", exception.Message);
    }

    [Theory]
    [InlineData("clip-path='url(#missing)'")]
    [InlineData("fill-rule='positive'")]
    public async Task Rejects_unimplemented_direct_text_attributes(string attribute)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' " + attribute + ">A</text></svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_invalid_xml_space_before_direct_text_layout()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' xml:space='collapse'>A</text></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.MalformedInput, exception.ErrorCode);
    }

    [Fact]
    public async Task Concatenates_nested_attribute_free_direct_text_spans()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        ReadOnlyMemory<byte> spans = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'> \t<tspan><![CDATA[A]]></tspan>\r\n</text></svg>"u8.ToArray(), options, CancellationToken.None);
        ReadOnlyMemory<byte> expected = SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray(), options, CancellationToken.None);

        Assert.Equal(expected.ToArray(), spans.ToArray());
    }

    [Fact]
    public async Task Rejects_unadmitted_direct_text_span_attributes_and_preserves_attribute_limits()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan rotate='1'>A</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        SvgRenderLimits attributeLimits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 100_000, 128, 1);
        var attributeOptions = new SvgRenderOptions(null, 1, SvgColor.Transparent, attributeLimits, snapshot);

        SvgException attributeException = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan x='1' y='1'>A</tspan></text></svg>"u8.ToArray(), attributeOptions, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, attributeException.ErrorCode);
        Assert.Contains("attributes", attributeException.Message);
    }

    [Fact]
    public async Task Attribute_free_direct_text_spans_respect_the_remaining_node_and_depth_limits()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 1, 16, 100_000);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, limits, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan>A</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        SvgRenderLimits depthLimits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 100_000, 2);
        var depthOptions = new SvgRenderOptions(null, 1, SvgColor.Transparent, depthLimits, snapshot);

        SvgException depthException = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan>A</tspan></text></svg>"u8.ToArray(), depthOptions, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, depthException.ErrorCode);
        Assert.Contains("nesting", depthException.Message);
    }

    [Fact]
    public async Task Rejects_direct_text_before_materializing_more_than_the_admitted_character_limit()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>" + new string('A', SvgTextLimits.MaximumCharacters + 1) + "</text></svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_document_text_that_exceeds_the_aggregate_admitted_character_limit()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        string first = new('A', SvgTextLimits.MaximumCharacters / 2);
        string second = new('A', SvgTextLimits.MaximumCharacters - first.Length + 1);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text fill='none' font-family='Example' font-size='100' y='3'>" + first + "</text><text fill='none' font-family='Example' font-size='100' y='3'>" + second + "</text></svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
    }

    [Fact]
    public async Task Builds_a_simple_text_run_from_one_prepared_face()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        var style = new SvgTextStyle(new SvgFontFaceRequest("Example", 400, false), 10, 2, 3, 4, 5, 0, SvgTextAnchor.Start, string.Empty);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(64);
        using SvgTextRun run = SvgTextRunBuilder.Create(style, "A", snapshot, budget, CancellationToken.None);

        SvgPositionedGlyph glyph = Assert.Single(run.Glyphs.ToArray());
        Assert.Equal((ushort)1, glyph.GlyphIndex);
        Assert.Equal(6, glyph.X);
        Assert.Equal(8, glyph.Y);
        using SvgGlyphContourStorage contours = Assert.Single(SvgTextRunBuilder.GetGlyphContours(run, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None));
        Assert.Empty(contours.Contours);
        Assert.Equal(64, budget.Snapshot().CurrentBytes);
        run.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Text_run_releases_the_cached_outline_when_its_enumerator_stops_early()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var style = new SvgTextStyle(new SvgFontFaceRequest("Example", 400, false), 100, 0, 3, 0, 0, 0, SvgTextAnchor.Start, string.Empty);
        RenderBudget budget = new(10_000);

        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "AA", snapshot, budget, CancellationToken.None))
        {
            long glyphStorageBytes = budget.Snapshot().CurrentBytes;
            using (IEnumerator<SvgGlyphContourStorage> contours = SvgTextRunBuilder.GetGlyphContours(run, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None).GetEnumerator())
            {
                Assert.True(contours.MoveNext());
                using SvgGlyphContourStorage first = contours.Current;
                Assert.NotEmpty(first.Contours);
            }
            Assert.Equal(glyphStorageBytes, budget.Snapshot().CurrentBytes);
        }

        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Converts_glyph_outlines_from_font_units_to_svg_coordinates()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(bytes, 1, CancellationToken.None);
        WriteInt16(bytes, glyph.Offset, 1);
        WriteUInt16(bytes, glyph.Offset + 10, 1);
        WriteUInt16(bytes, glyph.Offset + 12, 0);
        bytes[glyph.Offset + 14] = 0x37;
        bytes[glyph.Offset + 15] = 0x37;
        bytes[glyph.Offset + 16] = 10;
        bytes[glyph.Offset + 17] = 5;
        bytes[glyph.Offset + 18] = 3;
        bytes[glyph.Offset + 19] = 0;

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgPositionedGlyph positioned = Assert.Single(SvgGlyphLayout.Layout(snapshot.Fonts[0], "A", 102.4, 2, 3, CancellationToken.None));
        SvgGlyphQuadraticContour contour = Assert.Single(SvgGlyphOutline.GetContours(snapshot.Fonts[0], positioned, 102.4, CancellationToken.None));
        SvgGlyphQuadraticSegment first = contour.Segments[0];

        Assert.Equal(new SvgGlyphQuadraticSegment(new(3, 2.7), new(3.5, 2.7), new(3.5, 2.7)), first);
        Assert.Equal(first.Start, contour.Segments[^1].End);
    }

    [Fact]
    public async Task Borrowed_glyph_decoding_needs_only_final_contour_storage_and_remains_unchanged()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        using OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(font.Bytes, 1, font.Metrics, new RenderBudget(10_000), null, CancellationToken.None);
        OpenTypeQuadraticSegment[] unchanged = decoded.Segments.ToArray();
        RenderBudget budget = new(SvgGlyphGeometry.GetStorageByteCount(decoded.ContourEnds.Length, decoded.Segments.Length));
        using (SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font, new(1, 2, 3, 1), 100, decoded, budget, null, CancellationToken.None))
        {
            Assert.Equal(new SvgGlyphQuadraticSegment[]
            {
                new(new(3, 2), new(4, 2), new(4, 2)),
                new(new(4, 2), new(3, 1), new(3, 1)),
                new(new(3, 1), new(3, 2), new(3, 2))
            }, Assert.Single(contours.Contours).Segments);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
            Assert.Equal(unchanged, decoded.Segments);
            Assert.Equal([3], decoded.ContourEnds);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Multi_contour_glyphs_keep_affine_mapping_and_release_numeric_failure_storage()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateOriginReturningContourFont())], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        RenderBudget budget = new(10_000);
        using (SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(font, new(1, 2, 3, 1), 1_024, new SvgAffineTransform(0, 1, -1, 0, 10, 20), budget, null, CancellationToken.None))
        {
            Assert.Collection(contours.Contours,
                first =>
                {
                    Assert.Equal(4, first.Segments.Length);
                    Assert.Equal(new SvgGlyphQuadraticSegment(new(7, 22), new(7, 32), new(7, 32)), first.Segments[0]);
                    Assert.Equal(first.Segments[0].Start, first.Segments[1].End);
                },
                second => Assert.Equal(2, second.Segments.Length));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgException error = Assert.Throws<SvgException>(() => SvgGlyphOutline.GetContourStorage(font, new(1, 2, 3, 1), 1_024, new SvgAffineTransform(double.MaxValue, 0, 0, 1, 0, 0), budget, null, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Preserves_structural_glyph_contours_when_their_segments_revisit_the_start_point()
    {
        byte[] bytes = CreateOriginReturningContourFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);

        using (OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(bytes, 1, metrics, new RenderBudget(10_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal([4, 6], decoded.ContourEnds);
        }
        using (OpenTypeGlyphContourStorage composite = OpenTypeFont.GetGlyphContours(bytes, 2, metrics, new RenderBudget(10_000), new ParserWorkCounter(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal([4, 6, 10, 12], composite.ContourEnds);
        }

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgGlyphQuadraticContour[] contours = SvgGlyphOutline.GetContours(snapshot.Fonts[0], new SvgPositionedGlyph(1, 0, 0, 1), 1, CancellationToken.None);

        Assert.Collection(
            contours,
            first =>
            {
                Assert.Equal(4, first.Segments.Length);
                Assert.Equal(first.Segments[0].Start, first.Segments[1].End);
            },
            second => Assert.Equal(2, second.Segments.Length));
    }

    [Theory]
    [InlineData("windows", false, 18, 12, 110.578125)]
    [InlineData("linux", false, 18, 12, 110.578125)]
    [InlineData("windows", true, 18, 12, 110.625)]
    [InlineData("linux", true, 18, 12, 110.625)]
    [InlineData("windows", false, 36, 17, 313.34375)]
    [InlineData("linux", false, 36, 17, 313.328125)]
    public async Task Device_layout_preserves_the_authored_origin_and_matches_measured_cumulative_offsets(string target, bool textPath, double size, int index, double expectedDeviceOffset)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1000, 3, 2, 1))], CancellationToken.None);
        const double scale = 512d / 600;
        SvgGlyphDeviceMetrics metrics = new(scale, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, textPath);
        RenderBudget budget = new(10_000);
        using (SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), new string('A', 18), size, 10, 40, 0, [], [], SvgTextAnchor.Start, metrics, budget, string.Empty, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(10, positions.Glyphs[0].X);
            Assert.Equal(40, positions.Glyphs[index].Y);
            Assert.Equal(expectedDeviceOffset, (positions.Glyphs[index].X - 10) * scale, 10);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    public async Task Device_layout_keeps_fractional_absolute_and_relative_span_origins(string target)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1000, 3, 2, 1))], CancellationToken.None);
        const double scale = 512d / 600;
        SvgGlyphDeviceMetrics metrics = new(scale, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, false);
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAAA", 36, 10.1, 40.2, 0, [], [new(2, 100.1, 50.2, .125, .25)], SvgTextAnchor.Start, metrics, new(10_000), string.Empty, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(10.1, positions.Glyphs[0].X);
        Assert.Equal(100.225, positions.Glyphs[2].X, 10);
        Assert.Equal(50.45, positions.Glyphs[2].Y, 10);
        Assert.Equal(18.421875, (positions.Glyphs[3].X - positions.Glyphs[2].X) * scale, 10);
    }

    [Fact]
    public async Task Device_layout_releases_owned_positions_on_numeric_and_work_denial()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateFont(1000, 3, 2, 1))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        foreach ((double size, long maximumWork) in new[] { (100_000d, long.MaxValue), (18d, 4L) })
        {
            RenderBudget budget = new(10_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(font, "AAA", size, 0, 0, 0, [], [], SvgTextAnchor.Start, new(1, SvgGlyphMetricTarget.Linux, false), budget, string.Empty, new(maximumWork), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    private static SvgFontProgramResolution CreateResolution(byte[] bytes)
        => CreateResolution(bytes, 400);

    private static SvgFontProgramResolution CreateResolution(byte[] bytes, int weight)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\", \"weight\": " + weight + " }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        return catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", weight, false)), new SvgMemoryFontProgramSource("caller-font", bytes));
    }

    private static byte[] CreateTriangleFont()
    {
        byte[] font = CreateFont(1000, 3, 2, 1);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(font, 1, CancellationToken.None);
        WriteInt16(font, glyph.Offset, 1);
        WriteUInt16(font, glyph.Offset + 10, 2);
        WriteUInt16(font, glyph.Offset + 12, 0);
        font[glyph.Offset + 14] = 0x37;
        font[glyph.Offset + 15] = 0x33;
        font[glyph.Offset + 16] = 0x27;
        for (int offset = glyph.Offset + 17; offset <= glyph.Offset + 21; offset++) font[offset] = 10;
        return font;
    }

    private static byte[] CreateTriangleSpaceFont()
    {
        byte[] font = CreateTriangleFont();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        OpenTypeTableRange cmap = Assert.IsType<OpenTypeTableRange>(metrics.Cmap);
        int subtable = cmap.Offset + 12;
        WriteUInt16(font, subtable + 14, 32);
        WriteUInt16(font, subtable + 20, 32);
        WriteInt16(font, subtable + 24, -31);
        return font;
    }

    private static byte[] CreateTriangleKernFont()
    {
        byte[] font = CreateTriangleFont();
        const int originalTableCount = 7;
        const int originalDirectoryLength = 12 + originalTableCount * 16;
        const int legacyKernLength = 24;
        int originalLength = font.Length;
        Array.Resize(ref font, originalLength + 16 + legacyKernLength);
        Array.Copy(font, originalDirectoryLength, font, originalDirectoryLength + 16, originalLength - originalDirectoryLength);
        for (int index = 0; index < originalTableCount; index++)
        {
            int entry = 12 + index * 16;
            uint offset = ((uint)font[entry + 8] << 24) | ((uint)font[entry + 9] << 16) | ((uint)font[entry + 10] << 8) | font[entry + 11];
            WriteUInt32(font, entry + 8, offset + 16);
        }
        WriteUInt16(font, 4, originalTableCount + 1);
        int kernOffset = originalLength + 16;
        WriteTable(font, originalTableCount, 0x6B65726E, kernOffset, legacyKernLength);
        WriteUInt16(font, kernOffset, 0);
        WriteUInt16(font, kernOffset + 2, 1);
        WriteUInt16(font, kernOffset + 4, 0);
        WriteUInt16(font, kernOffset + 6, 20);
        WriteUInt16(font, kernOffset + 8, 1);
        WriteUInt16(font, kernOffset + 10, 1);
        WriteUInt16(font, kernOffset + 12, 6);
        WriteUInt16(font, kernOffset + 14, 0);
        WriteUInt16(font, kernOffset + 16, 0);
        WriteUInt16(font, kernOffset + 18, 1);
        WriteUInt16(font, kernOffset + 20, 1);
        WriteInt16(font, kernOffset + 22, -300);
        return font;
    }

    private static byte[] CreateTriangleGposFont()
    {
        byte[] font = CreateTriangleFont();
        const int originalTableCount = 7;
        const int originalDirectoryLength = 12 + originalTableCount * 16;
        const int gposLength = 80;
        int originalLength = font.Length;
        Array.Resize(ref font, originalLength + 16 + gposLength);
        Array.Copy(font, originalDirectoryLength, font, originalDirectoryLength + 16, originalLength - originalDirectoryLength);
        for (int index = 0; index < originalTableCount; index++)
        {
            int entry = 12 + index * 16;
            uint offset = ((uint)font[entry + 8] << 24) | ((uint)font[entry + 9] << 16) | ((uint)font[entry + 10] << 8) | font[entry + 11];
            WriteUInt32(font, entry + 8, offset + 16);
        }
        WriteUInt16(font, 4, originalTableCount + 1);
        int gposOffset = originalLength + 16;
        WriteTable(font, originalTableCount, 0x47504F53, gposOffset, gposLength);
        WriteUInt16(font, gposOffset, 1);
        WriteUInt16(font, gposOffset + 2, 0);
        WriteUInt16(font, gposOffset + 4, 10);
        WriteUInt16(font, gposOffset + 6, 30);
        WriteUInt16(font, gposOffset + 8, 44);
        WriteUInt16(font, gposOffset + 10, 1);
        WriteUInt32(font, gposOffset + 12, 0x44464C54);
        WriteUInt16(font, gposOffset + 16, 8);
        WriteUInt16(font, gposOffset + 18, 4);
        WriteUInt16(font, gposOffset + 20, 0);
        WriteUInt16(font, gposOffset + 22, 0);
        WriteUInt16(font, gposOffset + 24, ushort.MaxValue);
        WriteUInt16(font, gposOffset + 26, 1);
        WriteUInt16(font, gposOffset + 30, 1);
        WriteUInt32(font, gposOffset + 32, 0x6B65726E);
        WriteUInt16(font, gposOffset + 36, 8);
        WriteUInt16(font, gposOffset + 38, 0);
        WriteUInt16(font, gposOffset + 40, 1);
        WriteUInt16(font, gposOffset + 42, 0);
        WriteUInt16(font, gposOffset + 44, 1);
        WriteUInt16(font, gposOffset + 46, 4);
        WriteUInt16(font, gposOffset + 48, 2);
        WriteUInt16(font, gposOffset + 50, 0);
        WriteUInt16(font, gposOffset + 52, 1);
        WriteUInt16(font, gposOffset + 54, 8);
        WriteUInt16(font, gposOffset + 56, 1);
        WriteUInt16(font, gposOffset + 58, 18);
        WriteUInt16(font, gposOffset + 60, 4);
        WriteUInt16(font, gposOffset + 62, 0);
        WriteUInt16(font, gposOffset + 64, 1);
        WriteUInt16(font, gposOffset + 66, 12);
        WriteUInt16(font, gposOffset + 68, 1);
        WriteUInt16(font, gposOffset + 70, 1);
        WriteInt16(font, gposOffset + 72, -300);
        WriteUInt16(font, gposOffset + 74, 1);
        WriteUInt16(font, gposOffset + 76, 1);
        WriteUInt16(font, gposOffset + 78, 1);
        return font;
    }

    private static byte[] CreateFontWithMetadata(ushort os2Version)
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        const int originalTableCount = 7;
        const int directoryExpansion = 32;
        const int nameLength = 68;
        int os2Length = os2Version switch
        {
            0 => 68,
            1 => 86,
            2 or 3 or 4 => 96,
            5 => 100,
            _ => throw new ArgumentOutOfRangeException(nameof(os2Version))
        };
        const int originalDirectoryLength = 12 + originalTableCount * 16;
        int originalLength = font.Length;
        Array.Resize(ref font, originalLength + directoryExpansion + nameLength + os2Length);
        Array.Copy(font, originalDirectoryLength, font, originalDirectoryLength + directoryExpansion, originalLength - originalDirectoryLength);
        for (int index = 0; index < originalTableCount; index++)
        {
            int entry = 12 + index * 16;
            uint offset = ((uint)font[entry + 8] << 24) | ((uint)font[entry + 9] << 16) | ((uint)font[entry + 10] << 8) | font[entry + 11];
            WriteUInt32(font, entry + 8, offset + directoryExpansion);
        }
        WriteUInt16(font, 4, originalTableCount + 2);
        int nameOffset = originalLength + directoryExpansion;
        int os2Offset = nameOffset + nameLength;
        WriteTable(font, 7, 0x6E616D65, nameOffset, nameLength);
        WriteTable(font, 8, 0x4F532F32, os2Offset, os2Length);
        WriteUInt16(font, nameOffset, 1);
        WriteUInt16(font, nameOffset + 2, 2);
        WriteUInt16(font, nameOffset + 4, 36);
        WriteUInt16(font, nameOffset + 6, 3);
        WriteUInt16(font, nameOffset + 8, 1);
        WriteUInt16(font, nameOffset + 10, 0x8000);
        WriteUInt16(font, nameOffset + 12, 1);
        WriteUInt16(font, nameOffset + 14, 14);
        WriteUInt16(font, nameOffset + 16, 0);
        WriteUInt16(font, nameOffset + 18, 3);
        WriteUInt16(font, nameOffset + 20, 1);
        WriteUInt16(font, nameOffset + 22, 0x8000);
        WriteUInt16(font, nameOffset + 24, 2);
        WriteUInt16(font, nameOffset + 26, 14);
        WriteUInt16(font, nameOffset + 28, 14);
        WriteUInt16(font, nameOffset + 30, 1);
        WriteUInt16(font, nameOffset + 32, 4);
        WriteUInt16(font, nameOffset + 34, 28);
        WriteUInt16(font, os2Offset, os2Version);
        return font;
    }

    private static byte[] CreateTriangleAAndSpaceFont()
    {
        byte[] font = CreateTriangleFont();
        int cmapOffset = font.Length;
        Array.Resize(ref font, cmapOffset + 52);
        WriteTable(font, 4, 0x636D6170, cmapOffset, 52);
        WriteUInt16(font, cmapOffset, 0);
        WriteUInt16(font, cmapOffset + 2, 1);
        WriteUInt16(font, cmapOffset + 4, 3);
        WriteUInt16(font, cmapOffset + 6, 1);
        WriteUInt32(font, cmapOffset + 8, 12);
        int subtable = cmapOffset + 12;
        WriteUInt16(font, subtable, 4);
        WriteUInt16(font, subtable + 2, 40);
        WriteUInt16(font, subtable + 6, 6);
        WriteUInt16(font, subtable + 8, 4);
        WriteUInt16(font, subtable + 10, 1);
        WriteUInt16(font, subtable + 12, 2);
        WriteUInt16(font, subtable + 14, 32);
        WriteUInt16(font, subtable + 16, 65);
        WriteUInt16(font, subtable + 18, 0xFFFF);
        WriteUInt16(font, subtable + 22, 32);
        WriteUInt16(font, subtable + 24, 65);
        WriteUInt16(font, subtable + 26, 0xFFFF);
        WriteInt16(font, subtable + 28, -31);
        WriteInt16(font, subtable + 30, -64);
        WriteInt16(font, subtable + 32, 1);
        return font;
    }

    private static byte[] CreateOriginReturningContourFont()
    {
        byte[] font = CreateFont(1_024, 3, 2, 1);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        OpenTypeTableRange loca = Assert.IsType<OpenTypeTableRange>(metrics.Loca);
        OpenTypeTableRange glyf = Assert.IsType<OpenTypeTableRange>(metrics.Glyf);
        Array.Resize(ref font, glyf.Offset + 64);
        WriteTable(font, 6, 0x676C7966, glyf.Offset, 64);
        WriteUInt32(font, loca.Offset + 12, 64);

        int simple = glyf.Offset;
        WriteInt16(font, simple, 2);
        WriteUInt16(font, simple + 10, 3);
        WriteUInt16(font, simple + 12, 5);
        WriteUInt16(font, simple + 14, 0);
        font[simple + 16] = 0x31;
        font[simple + 17] = 0x33;
        font[simple + 18] = 0x23;
        font[simple + 19] = 0x35;
        font[simple + 20] = 0x07;
        font[simple + 21] = 0x33;
        font[simple + 22] = 10;
        font[simple + 23] = 10;
        font[simple + 24] = 10;
        font[simple + 25] = 20;
        font[simple + 26] = 10;
        font[simple + 27] = 10;

        int composite = simple + 32;
        WriteInt16(font, composite, -1);
        WriteUInt16(font, composite + 10, 35);
        WriteUInt16(font, composite + 12, 1);
        WriteInt16(font, composite + 14, 0);
        WriteInt16(font, composite + 16, 0);
        WriteUInt16(font, composite + 18, 3);
        WriteUInt16(font, composite + 20, 1);
        WriteInt16(font, composite + 22, 40);
        WriteInt16(font, composite + 24, 0);
        return font;
    }

    private static byte DecodeAlpha(ReadOnlyMemory<byte> png, int x, int y)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(20, 4));
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) throw new ArgumentOutOfRangeException(nameof(x));
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.Span.Slice(offset, 4));
            if (png.Span.Slice(offset + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(png.Span.Slice(offset + 8, length));
            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] pixels = new byte[checked((width * 4 + 1) * height)];
        zlib.ReadExactly(pixels);
        int row = y * (width * 4 + 1);
        if (pixels[row] != 0) throw new InvalidDataException("Test PNG decoder expects an unfiltered scanline.");
        return pixels[row + 1 + x * 4 + 3];
    }

    private static SvgColor DecodePixel(ReadOnlyMemory<byte> image, int x, int y)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(image.Span.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(image.Span.Slice(20, 4));
        if ((uint)x >= (uint)width || (uint)y >= (uint)height) throw new ArgumentOutOfRangeException(nameof(x));
        using var compressed = new MemoryStream();
        for (int offset = 8; offset < image.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(image.Span.Slice(offset, 4));
            if (image.Span.Slice(offset + 4, 4).SequenceEqual("IDAT"u8)) compressed.Write(image.Span.Slice(offset + 8, length));
            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] pixels = new byte[checked((width * 4 + 1) * height)];
        zlib.ReadExactly(pixels);
        int row = y * (width * 4 + 1);
        if (pixels[row] != 0) throw new InvalidDataException("Test PNG decoder expects an unfiltered scanline.");
        return new SvgColor(pixels[row + 1 + x * 4], pixels[row + 1 + x * 4 + 1], pixels[row + 1 + x * 4 + 2], pixels[row + 1 + x * 4 + 3]);
    }

    private static byte[] CreateFont(ushort unitsPerEm, ushort glyphCount, ushort numberOfHMetrics, short indexToLocFormat)
    {
        const int tableCount = 7;
        const int directoryLength = 12 + tableCount * 16;
        const int headOffset = directoryLength;
        const int maxpOffset = headOffset + 54;
        const int hheaOffset = maxpOffset + 6;
        const int hmtxOffset = hheaOffset + 36;
        int hmtxLength = numberOfHMetrics * 4 + (glyphCount - numberOfHMetrics) * 2;
        int cmapOffset = hmtxOffset + hmtxLength;
        int locaOffset = cmapOffset + 44;
        int locaLength = (glyphCount + 1) * (indexToLocFormat == 0 ? 2 : 4);
        int glyfOffset = locaOffset + locaLength;
        byte[] font = new byte[glyfOffset + 52];
        WriteUInt32(font, 0, 0x00010000);
        WriteUInt16(font, 4, tableCount);
        WriteTable(font, 0, 0x68656164, headOffset, 54);
        WriteTable(font, 1, 0x6D617870, maxpOffset, 6);
        WriteTable(font, 2, 0x68686561, hheaOffset, 36);
        WriteTable(font, 3, 0x686D7478, hmtxOffset, hmtxLength);
        WriteTable(font, 4, 0x636D6170, cmapOffset, 44);
        WriteTable(font, 5, 0x6C6F6361, locaOffset, locaLength);
        WriteTable(font, 6, 0x676C7966, glyfOffset, 52);
        WriteUInt16(font, headOffset + 18, unitsPerEm);
        WriteInt16(font, headOffset + 50, indexToLocFormat);
        WriteUInt16(font, maxpOffset + 4, glyphCount);
        WriteUInt16(font, hheaOffset + 34, numberOfHMetrics);
        WriteUInt16(font, hmtxOffset, 500);
        if (numberOfHMetrics > 1) WriteUInt16(font, hmtxOffset + 4, 600);
        WriteUInt16(font, cmapOffset, 0);
        WriteUInt16(font, cmapOffset + 2, 1);
        WriteUInt16(font, cmapOffset + 4, 3);
        WriteUInt16(font, cmapOffset + 6, 1);
        WriteUInt32(font, cmapOffset + 8, 12);
        int subtable = cmapOffset + 12;
        WriteUInt16(font, subtable, 4);
        WriteUInt16(font, subtable + 2, 32);
        WriteUInt16(font, subtable + 6, 4);
        WriteUInt16(font, subtable + 14, 65);
        WriteUInt16(font, subtable + 16, 0xFFFF);
        WriteUInt16(font, subtable + 20, 65);
        WriteUInt16(font, subtable + 22, 0xFFFF);
        WriteInt16(font, subtable + 24, -64);
        WriteInt16(font, subtable + 26, 1);
        if (indexToLocFormat == 0)
        {
            WriteUInt16(font, locaOffset, 0);
            WriteUInt16(font, locaOffset + 2, 0);
            WriteUInt16(font, locaOffset + 4, 16);
            WriteUInt16(font, locaOffset + 6, 26);
        }
        else
        {
            WriteUInt32(font, locaOffset, 0);
            WriteUInt32(font, locaOffset + 4, 0);
            WriteUInt32(font, locaOffset + 8, 32);
            WriteUInt32(font, locaOffset + 12, 52);
        }
        return font;
    }

    private static byte[] CreateFormat12Font()
    {
        byte[] font = CreateFont(1024, 3, 2, 1);
        const int cmapOffset = 230;
        WriteUInt16(font, cmapOffset, 0);
        WriteUInt16(font, cmapOffset + 2, 1);
        WriteUInt16(font, cmapOffset + 4, 3);
        WriteUInt16(font, cmapOffset + 6, 10);
        WriteUInt32(font, cmapOffset + 8, 12);
        int subtable = cmapOffset + 12;
        WriteUInt16(font, subtable, 12);
        WriteUInt32(font, subtable + 4, 28);
        WriteUInt32(font, subtable + 12, 1);
        WriteUInt32(font, subtable + 16, 0x1F600);
        WriteUInt32(font, subtable + 20, 0x1F600);
        WriteUInt32(font, subtable + 24, 2);
        return font;
    }

    private static byte[] CreateFormat12FontWithLaterMalformedGroup()
    {
        byte[] font = CreateFormat12Font();
        Array.Resize(ref font, 500);
        const int cmapOffset = 400;
        WriteTable(font, 4, 0x636D6170, cmapOffset, 52);
        WriteUInt16(font, cmapOffset, 0);
        WriteUInt16(font, cmapOffset + 2, 1);
        WriteUInt16(font, cmapOffset + 4, 3);
        WriteUInt16(font, cmapOffset + 6, 10);
        WriteUInt32(font, cmapOffset + 8, 12);
        int subtable = cmapOffset + 12;
        WriteUInt16(font, subtable, 12);
        WriteUInt32(font, subtable + 4, 40);
        WriteUInt32(font, subtable + 12, 2);
        WriteUInt32(font, subtable + 16, 'A');
        WriteUInt32(font, subtable + 20, 'A');
        WriteUInt32(font, subtable + 24, 1);
        WriteUInt32(font, subtable + 28, 100);
        WriteUInt32(font, subtable + 32, 99);
        WriteUInt32(font, subtable + 36, 1);
        return font;
    }

    private static void WriteTable(byte[] destination, int index, uint tag, int offset, int length)
    {
        int entry = 12 + index * 16;
        WriteUInt32(destination, entry, tag);
        WriteUInt32(destination, entry + 8, (uint)offset);
        WriteUInt32(destination, entry + 12, (uint)length);
    }

    private static int FindTableOffset(ReadOnlySpan<byte> font, uint tag)
    {
        int tableCount = (font[4] << 8) | font[5];
        for (int index = 0; index < tableCount; index++)
        {
            int entry = 12 + index * 16;
            uint currentTag = ((uint)font[entry] << 24) | ((uint)font[entry + 1] << 16) | ((uint)font[entry + 2] << 8) | font[entry + 3];
            if (currentTag == tag) return checked((int)(((uint)font[entry + 8] << 24) | ((uint)font[entry + 9] << 16) | ((uint)font[entry + 10] << 8) | font[entry + 11]));
        }
        throw new InvalidOperationException("The requested table is missing from the synthetic font.");
    }

    private static void WriteUInt16(byte[] destination, int offset, int value)
    {
        destination[offset] = (byte)(value >> 8);
        destination[offset + 1] = (byte)value;
    }

    private static void WriteInt16(byte[] destination, int offset, short value) => WriteUInt16(destination, offset, unchecked((ushort)value));

    private static void WriteUInt32(byte[] destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }

    private sealed class CountingFontSource(byte[] bytes) : ISvgFontProgramSource
    {
        public int RequestCount { get; private set; }
        public string StableId => "counting-font";

        public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            RequestCount++;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }

    private sealed class FailingFontSource(SvgErrorCode errorCode, bool throwsSynchronously) : ISvgFontProgramSource
    {
        public string StableId => "failing-font";

        public ValueTask<ReadOnlyMemory<byte>> GetBytesAsync(CancellationToken cancellationToken)
        {
            if (throwsSynchronously) throw new SvgException(errorCode, "private-source-detail");
            return ValueTask.FromException<ReadOnlyMemory<byte>>(new SvgException(errorCode, "private-source-detail"));
        }
    }

    private sealed class FailingFontProvider(SvgErrorCode errorCode, bool throwsSynchronously) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            if (throwsSynchronously) throw new SvgException(errorCode, "private-provider-detail");
            return ValueTask.FromException<ISvgFontProgramSource?>(new SvgException(errorCode, "private-provider-detail"));
        }
    }

    private sealed class CountingFontProvider(ISvgFontProgramSource source) : ISvgFontProgramSourceProvider
    {
        public int RequestCount { get; private set; }

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            RequestCount++;
            return ValueTask.FromResult<ISvgFontProgramSource?>(source);
        }
    }

    private sealed class MissingFontProvider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => ValueTask.FromResult<ISvgFontProgramSource?>(null);
    }

}
