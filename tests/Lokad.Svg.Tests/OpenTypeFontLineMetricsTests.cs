using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData((ushort)0, 4)]
    [InlineData((ushort)2, 6)]
    public async Task Middle_baseline_uses_supplied_height_or_fallback_and_releases_on_adjustment_denial(ushort version, double expectedShift)
    {
        byte[] bytes = CreateFontWithMetadata(version);
        if (version == 2) WriteUInt16(bytes, FindTableOffset(bytes, 0x4F532F32) + 86, 768);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        SvgTextStyle style = new(new SvgFontFaceRequest("Example", 400, false), 16, 2, 3, 0, 0, 0, SvgTextAnchor.Start, string.Empty)
        { Baseline = SvgTextBaseline.Middle };
        ParserWorkCounter work = new(long.MaxValue);
        RenderBudget budget = new(1_000_000);
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "AA", snapshot, budget, work, CancellationToken.None))
        {
            Assert.All(run.Glyphs.ToArray(), glyph => Assert.Equal(3 + expectedShift, glyph.Y));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            SvgTextRunBuilder.Create(style, "AA", snapshot, denied, new ParserWorkCounter(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using SvgTextRun retry = SvgTextRunBuilder.Create(style, "AA", snapshot, denied, CancellationToken.None);
        Assert.All(retry.Glyphs.ToArray(), glyph => Assert.Equal(3 + expectedShift, glyph.Y));
    }

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
