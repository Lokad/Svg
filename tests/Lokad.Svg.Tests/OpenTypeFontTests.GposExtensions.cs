using System.Buffers.Binary;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(false, 8, -100)]
    [InlineData(true, 8, -300)]
    [InlineData(false, 65_544, -100)]
    [InlineData(true, 65_544, -300)]
    public void Gpos_extension_payloads_share_pair_execution_and_full_width_offsets(bool classes, int byteOffset, int expected)
    {
        ushort[] pairs = classes ? CreateClassPairs(true, -300) : CreatePairList(1, -100);
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(9, 0, GposExtension(2, pairs, byteOffset))], [0]);
        byte[] unchanged = font.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
        SvgPositionedGlyph[] glyphs = [new(1, 0, 0, 0), new(1, 0, 0, 0)];
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, glyphs, metrics, CancellationToken.None));
        Assert.Equal(0, OpenTypeFont.GetPairAdjustment(font, 0, 1, metrics, CancellationToken.None));
        Assert.Equal(unchanged, font);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(0, -25)]
    [InlineData(-100, -125)]
    public void Gpos_extensions_retain_zero_matches_and_mixed_lookup_order(short first, int expected)
    {
        byte[] font = CreateKerningGraphFont([
            CreateKerningLookup(9, 0, GposExtension(2, CreatePairList(1, first), 8), GposExtension(2, CreateClassPairs(false, -300), 8)),
            CreateKerningLookup(2, 0, CreatePairList(1, -25))], [1, 0, 1]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData(8, 1, -100)]
    [InlineData(2, 1, 0)]
    [InlineData(4, 2, 0)]
    public void Gpos_extensions_retain_outer_filter_flags(ushort flags, ushort glyphClass, int expected)
    {
        byte[] source = CreateKerningGraphFont([CreateKerningLookup(9, flags, GposExtension(2, CreatePairList(1, -100), 8))], [0]);
        byte[] font = AppendTestFontTable(source, 0x47444546, [1, 0, 12, 0, 0, 0, 1, 1, 1, glyphClass]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Gpos_extension_context_coverage_and_mark_runs_keep_their_rejections()
    {
        ushort[] context = [2, 12, 0, 0, 0, 0, 1, 1, 0];
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(9, 0, GposExtension(8, context, 8))], [0]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        OpenTypeFont.ValidateGlyphPositioning(font, 1, metrics, CancellationToken.None);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ValidateGlyphPositioning(font, 0, metrics, CancellationToken.None)).ErrorCode);
        byte[] source = CreateKerningGraphFont([CreateKerningLookup(9, 8, GposExtension(2, CreatePairList(1, -100), 8))], [0]);
        byte[] marked = AppendTestFontTable(source, 0x47444546, [1, 0, 12, 0, 0, 0, 1, 1, 1, 3]);
        OpenTypeFontMetrics markMetrics = OpenTypeFont.ParseMetrics(marked, CancellationToken.None);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => OpenTypeFont.ValidateGlyphPositioning(marked, 1, markMetrics, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [InlineData("header", SvgErrorCode.MalformedInput)]
    [InlineData("payload", SvgErrorCode.MalformedInput)]
    [InlineData("zero-offset", SvgErrorCode.MalformedInput)]
    [InlineData("header-offset", SvgErrorCode.MalformedInput)]
    [InlineData("outside", SvgErrorCode.MalformedInput)]
    [InlineData("large-offset", SvgErrorCode.MalformedInput)]
    [InlineData("nested", SvgErrorCode.MalformedInput)]
    [InlineData("missing-type", SvgErrorCode.MalformedInput)]
    [InlineData("mixed-types", SvgErrorCode.MalformedInput)]
    [InlineData("format", SvgErrorCode.UnsupportedFeature)]
    [InlineData("unsupported-type", SvgErrorCode.UnsupportedFeature)]
    [InlineData("filtered-context", SvgErrorCode.UnsupportedFeature)]
    public void Gpos_extension_admission_validates_payload_domains(string defect, SvgErrorCode expected)
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(9, 0,
            GposExtension(2, CreatePairList(1, -100), 8), GposExtension(2, CreateClassPairs(false, -300), 8))], [0]);
        var table = FindTestFontTable(font, 0x47504F53);
        int list = table.Offset + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(table.Offset + 8));
        int lookup = list + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(list + 2));
        int wrapper = lookup + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(lookup + 6));
        switch (defect)
        {
            case "header": WriteUInt32(font, table.Record + 12, (uint)(wrapper - table.Offset + 7)); break;
            case "payload": WriteUInt32(font, table.Record + 12, (uint)(wrapper - table.Offset + 11)); break;
            case "zero-offset": WriteUInt32(font, wrapper + 4, 0); break;
            case "header-offset": WriteUInt32(font, wrapper + 4, 2); break;
            case "outside": WriteUInt32(font, wrapper + 4, (uint)table.Length); break;
            case "large-offset": WriteUInt32(font, wrapper + 4, uint.MaxValue); break;
            case "nested": WriteUInt16(font, wrapper + 2, 9); break;
            case "missing-type": WriteUInt16(font, wrapper + 2, 0); break;
            case "mixed-types":
                int second = lookup + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(lookup + 8));
                WriteUInt16(font, second + 2, 8); break;
            case "format": WriteUInt16(font, wrapper, 2); break;
            case "unsupported-type": WriteUInt16(font, wrapper + 2, 1); break;
            case "filtered-context": WriteUInt16(font, wrapper + 2, 8); WriteUInt16(font, lookup + 2, 8); break;
            default: throw new ArgumentException(defect, nameof(defect));
        }
        Assert.Equal(expected, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Gpos_extension_scans_preserve_cancellation()
    {
        byte[] font = CreateKerningGraphFont([CreateKerningLookup(9, 0, GposExtension(2, CreateClassPairs(false, -300), 8))], [0]);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        CancellationToken token = new(true);
        Assert.Equal(token, Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.ParseMetrics(font, token)).CancellationToken);
        Assert.Equal(token, Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.ValidateGlyphPositioning(font, 1, metrics, token)).CancellationToken);
        Assert.Equal(token, Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, token)).CancellationToken);
    }

    private static ushort[] GposExtension(ushort type, ushort[] payload, int byteOffset)
    {
        ushort[] result = new ushort[byteOffset / 2 + payload.Length];
        result[0] = 1;
        result[1] = type;
        result[2] = (ushort)(byteOffset >> 16);
        result[3] = (ushort)byteOffset;
        payload.CopyTo(result, byteOffset / 2);
        return result;
    }
}
