using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(800, -200, 750, -250, 0, 800, -200, 900, 200)]
    [InlineData(800, -200, 750, -250, 128, 750, -250, 750, 250)]
    [InlineData(0, 0, 750, -250, 0, 750, -250, 900, 200)]
    [InlineData(0, 0, 0, 0, 0, 900, -200, 900, 200)]
    public void Retains_native_alphabetic_metric_precedence(int hheaAscent, int hheaDescent, int typoAscent, int typoDescent, int flags, int ascent, int descent, int windowsAscent, int windowsDescent)
    {
        byte[] bytes = CreateFontWithMetadata(1);
        int hhea = FindTableOffset(bytes, 0x68686561), os2 = FindTableOffset(bytes, 0x4F532F32);
        WriteUInt16(bytes, hhea + 4, unchecked((ushort)hheaAscent));
        WriteUInt16(bytes, hhea + 6, unchecked((ushort)hheaDescent));
        WriteUInt16(bytes, os2 + 62, (ushort)flags);
        WriteUInt16(bytes, os2 + 68, unchecked((ushort)typoAscent));
        WriteUInt16(bytes, os2 + 70, unchecked((ushort)typoDescent));
        WriteUInt16(bytes, os2 + 74, 900); WriteUInt16(bytes, os2 + 76, 200);

        Assert.Equal(new(ascent, descent, windowsAscent, windowsDescent), OpenTypeFont.ParseMetrics(bytes, CancellationToken.None).LineMetrics);
    }

    [Fact]
    public void Short_version_zero_metadata_keeps_the_validated_hhea_metrics()
    {
        byte[] bytes = CreateFontWithMetadata(0);
        int hhea = FindTableOffset(bytes, 0x68686561);
        WriteUInt16(bytes, hhea + 4, 800); WriteUInt16(bytes, hhea + 6, unchecked((ushort)-200));
        Assert.Equal(new(800, -200, 800, 200), OpenTypeFont.ParseMetrics(bytes, CancellationToken.None).LineMetrics);
    }
}
