using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Lokad.Svg.Tests;

public sealed class OpenTypeFontTests
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
    public void Checks_cancellation_before_font_parsing()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationCanceledException exception = Assert.Throws<OperationCanceledException>(() => OpenTypeFont.ParseMetrics(CreateFont(1024, 3, 2, 0), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void Maps_a_BMP_scalar_through_a_format_4_cmap()
    {
        byte[] font = CreateFont(1024, 3, 2, 0);

        Assert.Equal((ushort)1, OpenTypeFont.GetGlyphIndex(font, 'A', CancellationToken.None));
        Assert.Equal((ushort)0, OpenTypeFont.GetGlyphIndex(font, 'B', CancellationToken.None));
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
        var successfulWork = new ParserWorkCounter(6);

        using (SvgPositionedGlyphStorage glyphs = SvgGlyphLayout.CreateStorage(font, "A", 10, 0, 0, SvgTextAnchor.Start, successfulBudget, successfulWork, CancellationToken.None))
        {
            Assert.Single(glyphs.Glyphs);
        }

        Assert.Equal(6, successfulWork.Used);
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(1_024);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(font, "A", 10, 0, 0, SvgTextAnchor.Start, deniedBudget, new ParserWorkCounter(5), CancellationToken.None));

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
    public void Maps_a_supplementary_scalar_through_a_format_12_cmap()
    {
        byte[] font = CreateFormat12Font();

        Assert.Equal((ushort)2, OpenTypeFont.GetGlyphIndex(font, 0x1F600, CancellationToken.None));
        Assert.Equal((ushort)0, OpenTypeFont.GetGlyphIndex(font, 0x1F601, CancellationToken.None));
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

        OpenTypeQuadraticSegment[] segments = OpenTypeFont.BuildQuadraticContours(points, [3]);

        Assert.Equal(new OpenTypeQuadraticSegment(new(0, 0), new(4, 4), new(6, 2)), segments[0]);
        Assert.Equal(new OpenTypeQuadraticSegment(new(6, 2), new(8, 0), new(12, 0)), segments[1]);
        Assert.Equal(new OpenTypeQuadraticSegment(new(12, 0), new(0, 0), new(0, 0)), segments[2]);
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
    public async Task Svg_based_snapshot_applies_caller_work_limits_to_metric_parsing()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 12);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Format, diagnostic.Stage);
        Assert.Equal("files/example.ttf:0", diagnostic.FaceIdentity);
        Assert.Equal(1, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_applies_caller_work_limits_to_program_integrity_verification()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 15);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), limits, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Integrity, Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic).Stage);
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
    public async Task Svg_based_snapshot_does_not_acquire_fonts_for_text_with_child_elements()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused'><tspan>x</tspan></text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_does_not_acquire_fonts_for_transformed_text()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><g transform='translate(1)'><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None);

        Assert.Empty(snapshot.Fonts);
        Assert.Equal(0, source.RequestCount);
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
    public async Task Svg_based_snapshot_rejects_unsupported_text_attributes_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused' clip-path='url(#missing)'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_nondefault_text_fill_rules_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Unused' fill-rule='evenodd'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

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

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><g style='fill-rule:evenodd'><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

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

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><style>g { fill-rule: evenodd; }</style><g><text font-family='Unused'>x</text></g></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task Svg_based_snapshot_rejects_a_late_stylesheet_before_font_acquisition()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Unused\", \"resolvedFamily\": \"Unused\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        var source = new CountingFontSource(bytes);

        SvgException exception = await Assert.ThrowsAsync<SvgException>(async () => await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><rect width='1' height='1'/><style>g { display: inline; }</style><text font-family='Unused'>x</text></svg>"u8.ToArray(), catalog, new CountingFontProvider(source), CancellationToken.None));

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
        var style = new SvgTextStyle(new SvgFontFaceRequest("Example", 400, false), 10, 0, 0, 0, 0, SvgTextAnchor.Start, "caption");

        SvgException exception = Assert.Throws<SvgException>(() => SvgTextRunBuilder.Create(style, "B", snapshot, new RenderBudget(1_024), CancellationToken.None));

        SvgFontDiagnostic diagnostic = Assert.IsType<SvgFontDiagnostic>(exception.FontDiagnostic);
        Assert.Equal(SvgFontFailureStage.Glyph, diagnostic.Stage);
        Assert.Equal("caption", diagnostic.SourceElementId);
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
    public async Task Render_options_retain_the_caller_owned_font_snapshot()
    {
        byte[] bytes = CreateFont(1024, 3, 2, 1);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        Assert.Same(snapshot, options.FontSnapshot);
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
    public async Task Glyph_outline_decoding_and_transformation_reserve_their_temporary_segments()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgPositionedGlyph positioned = Assert.Single(SvgGlyphLayout.Layout(snapshot.Fonts[0], "A", 100, 0, 0, CancellationToken.None));
        RenderBudget budget = new(350);

        SvgException exception = Assert.Throws<SvgException>(() => SvgGlyphOutline.GetContours(snapshot.Fonts[0], positioned, 100, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None));

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
    public void Branching_composite_expansion_charges_each_empty_leaf_visit_to_the_shared_work_counter()
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
        var successfulWork = new ParserWorkCounter(7);

        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetGlyphContours(font, 2, metrics, successfulBudget, successfulWork, CancellationToken.None))
        {
            Assert.Empty(contours.Segments);
        }

        Assert.Equal(7, successfulWork.Used);
        Assert.Equal(0, successfulBudget.Snapshot().CurrentBytes);
        RenderBudget deniedBudget = new(10_000);

        SvgException exception = Assert.Throws<SvgException>(() => OpenTypeFont.GetGlyphContours(font, 2, metrics, deniedBudget, new ParserWorkCounter(6), CancellationToken.None));

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
    public async Task Parser_failure_after_retaining_a_glyph_contour_releases_its_scene_budget()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        RenderBudget budget = new(1_000_000);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='Example' font-size='100'>A</text><rect x='0.1' width='1'/></svg>"u8.ToArray();

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
    public async Task Charges_direct_glyph_sampling_work_to_the_public_conversion_limit()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        SvgRenderLimits limits = new(300_000, 1_024, 16, 256, 1_024, 16, 16, 100);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, limits, snapshot);
        byte[] svg = "<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'>A</text></svg>"u8.ToArray();

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode);
        Assert.Contains("Glyph raster work", exception.Message);
    }

    [Theory]
    [InlineData("clip-path='url(#missing)'")]
    [InlineData("fill-rule='evenodd'")]
    public async Task Rejects_unimplemented_direct_text_attributes(string attribute)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3' " + attribute + ">A</text></svg>");

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public async Task Rejects_direct_text_child_elements_until_span_semantics_are_implemented()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleFont())], CancellationToken.None);
        var options = new SvgRenderOptions(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'><text font-family='Example' font-size='100' y='3'><tspan>A</tspan></text></svg>"u8.ToArray(), options, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
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
        var style = new SvgTextStyle(new SvgFontFaceRequest("Example", 400, false), 10, 2, 3, 4, 5, SvgTextAnchor.Start, string.Empty);

        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(64);
        using SvgTextRun run = SvgTextRunBuilder.Create(style, "A", snapshot, budget, CancellationToken.None);

        SvgPositionedGlyph glyph = Assert.Single(run.Glyphs);
        Assert.Equal((ushort)1, glyph.GlyphIndex);
        Assert.Equal(6, glyph.X);
        Assert.Equal(8, glyph.Y);
        Assert.Empty(Assert.Single(SvgTextRunBuilder.GetGlyphContours(run, budget, new ParserWorkCounter(long.MaxValue), CancellationToken.None)));
        Assert.Equal(64, budget.Snapshot().CurrentBytes);
        run.Dispose();
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

    private static SvgFontProgramResolution CreateResolution(byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string manifest = "{ \"packId\": \"test-pack\", \"files\": [{ \"relativePath\": \"files/example.ttf\", \"byteSize\": " + bytes.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/example.ttf\" }] }";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "test-pack");
        return catalog.Bind(catalog.Resolve(new SvgFontFaceRequest("Example", 400, false)), new SvgMemoryFontProgramSource("caller-font", bytes));
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
