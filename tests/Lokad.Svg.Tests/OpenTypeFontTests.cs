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

    private static byte[] CreateFont(ushort unitsPerEm, ushort glyphCount, ushort numberOfHMetrics, short indexToLocFormat)
    {
        const int tableCount = 4;
        const int directoryLength = 12 + tableCount * 16;
        const int headOffset = directoryLength;
        const int maxpOffset = headOffset + 54;
        const int hheaOffset = maxpOffset + 6;
        const int hmtxOffset = hheaOffset + 36;
        int hmtxLength = numberOfHMetrics * 4 + (glyphCount - numberOfHMetrics) * 2;
        byte[] font = new byte[hmtxOffset + hmtxLength];
        WriteUInt32(font, 0, 0x00010000);
        WriteUInt16(font, 4, tableCount);
        WriteTable(font, 0, 0x68656164, headOffset, 54);
        WriteTable(font, 1, 0x6D617870, maxpOffset, 6);
        WriteTable(font, 2, 0x68686561, hheaOffset, 36);
        WriteTable(font, 3, 0x686D7478, hmtxOffset, hmtxLength);
        WriteUInt16(font, headOffset + 18, unitsPerEm);
        WriteInt16(font, headOffset + 50, indexToLocFormat);
        WriteUInt16(font, maxpOffset + 4, glyphCount);
        WriteUInt16(font, hheaOffset + 34, numberOfHMetrics);
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
