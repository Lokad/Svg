using System.Buffers.Binary;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false, -100)]
    [InlineData(true, -50)]
    public void Kern_features_with_the_same_tag_are_selected_by_the_default_Latin_language(bool latin, int expected)
    {
        byte[] font = CreateLanguageKerningFont(latin, false);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(expected, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
    }

    [Fact]
    public void Unselected_default_script_lookups_do_not_poison_selected_Latin_kerning()
    {
        byte[] font = CreateLanguageKerningFont(true, true);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(font, CancellationToken.None);
        Assert.Equal(-50, OpenTypeFont.GetPairAdjustment(font, 1, 1, metrics, CancellationToken.None));
    }

    [Fact]
    public void Selected_language_feature_indices_are_validated()
    {
        byte[] font = CreateLanguageKerningFont(true, false);
        WriteUInt16(font, font.Length - 2, 99);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => OpenTypeFont.ParseMetrics(font, CancellationToken.None)).ErrorCode);
    }

    private static byte[] CreateLanguageKerningFont(bool latin, bool unsupportedDefault)
    {
        byte[] font = CreateKerningGraphFont([
            CreateKerningLookup(unsupportedDefault ? (ushort)5 : (ushort)2, 0, CreatePairList(1, -100)),
            CreateKerningLookup(2, 0, CreatePairList(1, -50))], [0]);
        const int directory = 12 + 7 * 16;
        int table = BinaryPrimitives.ReadInt32BigEndian(font.AsSpan(directory + 8));
        int length = BinaryPrimitives.ReadInt32BigEndian(font.AsSpan(directory + 12));
        const int featuresLength = 26;
        int scripts = length + featuresLength;
        int scriptCount = latin ? 2 : 1;
        int scriptHeader = 2 + scriptCount * 6;
        int totalLength = scripts + scriptHeader + scriptCount * 12;
        Array.Resize(ref font, table + totalLength);
        WriteTable(font, 7, 0x47504F53, table, totalLength);
        WriteUInt16(font, table + 4, (ushort)scripts);
        WriteUInt16(font, table + 6, (ushort)length);
        int features = table + length;
        WriteUInt16(font, features, 2);
        for (int index = 0; index < 2; index++)
        {
            WriteUInt32(font, features + 2 + index * 6, 0x6B65726E);
            WriteUInt16(font, features + 6 + index * 6, (ushort)(14 + index * 6));
            WriteUInt16(font, features + 16 + index * 6, 1);
            WriteUInt16(font, features + 18 + index * 6, (ushort)index);
        }
        WriteUInt16(font, table + scripts, (ushort)scriptCount);
        for (int index = 0; index < scriptCount; index++)
        {
            int script = scriptHeader + index * 12;
            WriteUInt32(font, table + scripts + 2 + index * 6, index == 0 ? 0x44464C54U : 0x6C61746EU);
            WriteUInt16(font, table + scripts + 6 + index * 6, (ushort)script);
            WriteUInt16(font, table + scripts + script, 4);
            WriteUInt16(font, table + scripts + script + 6, ushort.MaxValue);
            WriteUInt16(font, table + scripts + script + 8, 1);
            WriteUInt16(font, table + scripts + script + 10, (ushort)index);
        }
        return font;
    }
}
