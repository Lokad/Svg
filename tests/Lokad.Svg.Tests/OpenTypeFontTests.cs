using Lokad.Svg.Fonts;

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
        byte[] font = new byte[glyfOffset + 32];
        WriteUInt32(font, 0, 0x00010000);
        WriteUInt16(font, 4, tableCount);
        WriteTable(font, 0, 0x68656164, headOffset, 54);
        WriteTable(font, 1, 0x6D617870, maxpOffset, 6);
        WriteTable(font, 2, 0x68686561, hheaOffset, 36);
        WriteTable(font, 3, 0x686D7478, hmtxOffset, hmtxLength);
        WriteTable(font, 4, 0x636D6170, cmapOffset, 44);
        WriteTable(font, 5, 0x6C6F6361, locaOffset, locaLength);
        WriteTable(font, 6, 0x676C7966, glyfOffset, 32);
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
            WriteUInt16(font, locaOffset + 6, 16);
        }
        else
        {
            WriteUInt32(font, locaOffset, 0);
            WriteUInt32(font, locaOffset + 4, 0);
            WriteUInt32(font, locaOffset + 8, 32);
            WriteUInt32(font, locaOffset + 12, 32);
        }
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
}
