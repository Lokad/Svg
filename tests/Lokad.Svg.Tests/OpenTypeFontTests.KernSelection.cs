using System.Buffers.Binary;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(1, -100)]
    [InlineData(2, -100)]
    public void Selected_Gpos_does_not_add_or_reject_unused_legacy_kerning(int subtableCount, short adjustment)
    {
        byte[] font = WithLegacyKerning(CreateKerningGraphFont([CreateKerningLookup(2, 0, CreatePairList(1, adjustment))], [0]), subtableCount);
        byte[] unchanged = font.ToArray();

        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(adjustment, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
        Assert.Equal(unchanged, font);
    }

    [Fact]
    public void Missing_Gpos_kern_feature_retains_active_legacy_kerning()
    {
        byte[] source = CreateTriangleGposFont();
        OpenTypeGposPairTable graph = Assert.IsType<OpenTypeGposPairTable>(OpenTypeFont.ParseMetrics(source, CancellationToken.None).Gpos);
        int features = graph.Range.Offset + BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(graph.Range.Offset + 6));
        WriteUInt32(source, features + 2, 0x74657374); // A valid, unselected feature tag.
        byte[] font = WithLegacyKerning(source, 1);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);

        Assert.Equal(-300, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
    }

    [Fact]
    public void Required_unsupported_legacy_kerning_remains_explicit()
    {
        byte[] font = WithLegacyKerning(CreateTriangleFont(), 2);

        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unused_legacy_table_still_requires_a_valid_Sfnt_range(bool length)
    {
        byte[] font = WithLegacyKerning(CreateTriangleGposFont(), 1);
        int record = 12 + (BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4)) - 1) * 16;
        WriteUInt32(font, record + (length ? 12 : 8), (uint)font.Length + 1);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Invalid_selected_Gpos_does_not_fall_back_to_valid_legacy_kerning()
    {
        byte[] source = CreateTriangleGposFont();
        OpenTypeGposPairTable graph = Assert.IsType<OpenTypeGposPairTable>(OpenTypeFont.ParseMetrics(source, CancellationToken.None).Gpos);
        WriteUInt16(source, graph.Range.Offset + 6, ushort.MaxValue);
        byte[] font = WithLegacyKerning(source, 1);

        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    private static byte[] WithLegacyKerning(byte[] source, int subtableCount)
    {
        byte[] legacy = CreateTriangleKernFont();
        OpenTypeKernTable kern = Assert.IsType<OpenTypeKernTable>(OpenTypeFont.ParseMetrics(legacy, CancellationToken.None).Kern);
        int subtableLength = kern.Range.Length - 4;
        int count = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(4));
        int directoryEnd = 12 + count * 16;
        int offset = source.Length + 16;
        int length = 4 + subtableCount * subtableLength;
        byte[] result = new byte[offset + length];
        source.AsSpan(0, directoryEnd).CopyTo(result);
        source.AsSpan(directoryEnd).CopyTo(result.AsSpan(directoryEnd + 16));
        for (int index = 0; index < count; index++)
        {
            int field = 12 + index * 16 + 8;
            WriteUInt32(result, field, BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(field)) + 16);
        }
        WriteUInt16(result, 4, (ushort)(count + 1));
        WriteTable(result, count, 0x6B65726E, offset, length);
        WriteUInt16(result, offset + 2, (ushort)subtableCount);
        for (int index = 0; index < subtableCount; index++)
            legacy.AsSpan(kern.Range.Offset + 4, subtableLength).CopyTo(result.AsSpan(offset + 4 + index * subtableLength));
        return result;
    }
}
