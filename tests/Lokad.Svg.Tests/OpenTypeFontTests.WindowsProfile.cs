using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1.25, false)]
    [InlineData(1.5, true)]
    public async Task Windows_glyph_source_selects_the_profile_at_actual_raster_scale(double scale, bool symmetric)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("000100030008000A000E0007FFFF000F"), false);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(1, 0, 0, 0), 11.5, budget))
        using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(scale, 0, scale, 0, budget, CancellationToken.None))
            Assert.Equal(symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural, contours.WindowsLcdMode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(8, true, false)]
    [InlineData(8.49, true, false)]
    [InlineData(8.5, false, true)]
    [InlineData(11.5, false, true)]
    [InlineData(14.48, false, true)]
    [InlineData(14.4921875, true, true)]
    public void Windows_profile_uses_gasp_ranges_after_native_size_quantization(double size, bool symmetric, bool fitting)
    {
        // Pinned Skia Windows scaler: version-1 symmetric-smoothing bit,
        // rounded after F26 size quantization; fitting is selected separately.
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("000100030008000A000E0007FFFF000F"), true);
        byte[] unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        Assert.True(metrics.HasGlyphInstructions);
        OpenTypeWindowsGlyphProfile profile = OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, size, CancellationToken.None);
        Assert.Equal(symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural, profile.LcdMode);
        Assert.Equal(fitting, profile.GridFit);
        Assert.Equal(unchanged, bytes);
    }

    [Theory]
    [InlineData("", true, 20, false)]
    [InlineData("", true, 20.01, true)]
    [InlineData("", false, 16, true)]
    [InlineData("000000010008000A", true, 16, false)]
    [InlineData("000200010008000A", true, 16, false)]
    [InlineData("00010000", false, 16, true)]
    public void Windows_profile_preserves_native_absent_legacy_and_unknown_table_fallbacks(string table, bool instructions, double size, bool symmetric)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString(table), instructions);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        Assert.Equal(instructions, metrics.HasGlyphInstructions);
        Assert.Equal(symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural, OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, size, CancellationToken.None).LcdMode);
        Assert.True(OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, size, CancellationToken.None).GridFit);
    }

    [Theory]
    [InlineData("0001", SvgErrorCode.MalformedInput)]
    [InlineData("000100020008000A", SvgErrorCode.MalformedInput)]
    [InlineData("000100020008000A0008000F", SvgErrorCode.MalformedInput)]
    [InlineData("00010401", SvgErrorCode.LimitExceeded)]
    public void Windows_profile_bounds_reached_gasp_data(string table, SvgErrorCode expected)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString(table), true);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        Assert.Equal(expected, Assert.Throws<SvgException>(() => OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, 8, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public void Windows_profile_observes_cancellation_without_owning_font_storage()
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("000100030008000A000E0007FFFF000F"), true);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, 12, CancellationToken.None);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, 12, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private static byte[] CreateWindowsProfileFont(byte[] gasp, bool instructions)
    {
        byte[] original = CreateTriangleFont();
        int maxp = original.Length;
        Array.Resize(ref original, original.Length + 32);
        WriteTable(original, 1, 0x6D617870, maxp, 32);
        WriteUInt32(original, maxp, 0x10000); WriteUInt16(original, maxp + 4, 3);
        WriteUInt16(original, maxp + 6, 3); WriteUInt16(original, maxp + 8, 1);
        WriteUInt16(original, maxp + 14, 2); WriteUInt16(original, maxp + 24, 8);
        WriteUInt16(original, maxp + 26, instructions ? 7 : 0);
        if (gasp.Length == 0) return original;
        const int directoryLength = 12 + 7 * 16;
        byte[] font = new byte[original.Length + 16 + gasp.Length];
        original.AsSpan(0, directoryLength).CopyTo(font);
        original.AsSpan(directoryLength).CopyTo(font.AsSpan(directoryLength + 16));
        WriteUInt16(font, 4, 8);
        for (int index = 0; index < 7; index++)
        {
            int entry = 12 + index * 16;
            uint offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(entry + 8, 4));
            WriteUInt32(font, entry + 8, offset + 16);
        }
        WriteTable(font, 7, 0x67617370, original.Length + 16, gasp.Length);
        gasp.CopyTo(font.AsSpan(original.Length + 16));
        return font;
    }
}
