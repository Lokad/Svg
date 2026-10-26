using System.Buffers.Binary;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(false, "AA", 1)]
    [InlineData(false, "AAA", 1)]
    [InlineData(false, "AAAA", 2)]
    [InlineData(true, "AAA", 1)]
    public async Task Latin_ligatures_follow_font_preference_and_retain_only_positioned_clusters(bool rangeCoverage, string text, int expectedCount)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(rangeCoverage))], CancellationToken.None);
        RenderBudget budget = new(1_024);
        using (SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), text, 20, 10, 40, SvgTextAnchor.Middle, budget, CancellationToken.None))
        {
            Assert.Equal(expectedCount, positions.Glyphs.Length);
            Assert.Equal((ushort)2, positions.Glyphs[0].GlyphIndex);
            Assert.Equal(10 - expectedCount * 6, positions.Glyphs[0].X);
            Assert.All(positions.Glyphs.ToArray(), glyph => Assert.Equal(40, glyph.Y));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("spacing")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task Ligatures_keep_relative_clusters_and_respect_spacing_and_absolute_boundaries(string boundary)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgTextLetterSpacingSpan[] spacing = boundary == "spacing" ? [new(0, 1, 0), new(1, 3, 1)] : [];
        SvgTextPositionAdjustment[] adjustments = boundary switch
        {
            "relative" => [new(1, 5, 0)],
            "absolute" => [new(1, 100, null, 0, 0)],
            _ => []
        };
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 20, 10, 40, 0, spacing, adjustments, SvgTextAnchor.Start, new(1_024), string.Empty, null, CancellationToken.None);
        if (boundary == "relative")
        {
            // This fixture prefers AAA over AA. Relative offsets retain that cluster.
            Assert.Equal((ushort)2, Assert.Single(positions.Glyphs.ToArray()).GlyphIndex);
            Assert.Equal(10, positions.Glyphs[0].X);
        }
        else
        {
            Assert.Equal((ushort)1, positions.Glyphs[0].GlyphIndex);
            Assert.Equal(boundary == "spacing" ? 3 : 2, positions.Glyphs.Length);
            Assert.Equal(boundary == "absolute" ? 100 : 22, positions.Glyphs[1].X);
        }
    }

    [Fact]
    public async Task Native_cursor_items_do_not_require_retaining_paint_fragments()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        RenderBudget budget = new(16_384);
        using (SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorageAndMetrics(Assert.Single(snapshot.Fonts), "AAA", 20, 32, 40, 0, 0, false,
            [], [new(1, 5, 0)], [], [], [new(1, true)], SvgTextAnchor.Middle, new(1, SvgGlyphMetricTarget.Windows, false), budget, "label", null, CancellationToken.None).Storage)
        {
            Assert.Equal((ushort)2, Assert.Single(positions.Glyphs.ToArray()).GlyphIndex);
            Assert.Equal(23.5, positions.Glyphs[0].X);
            Assert.Empty(positions.PaintFragments.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Ligature_clusters_preserve_following_utf16_position_adjustments()
    {
        byte[] font = CreateLigatureFont(false);
        OpenTypeTableRange cmap = Assert.IsType<OpenTypeTableRange>(OpenTypeFont.ParseMetrics(font, CancellationToken.None).Cmap);
        // Map contiguous Basic Latin A–B to glyphs 1–2.
        WriteUInt16(font, cmap.Offset + 12 + 14, 66);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAAB", 20, 10, 40, 0, [], [new(3, 100, 50, 2, 3)], SvgTextAnchor.Start, new(1_024), string.Empty, null, CancellationToken.None);
        Assert.Equal(2, positions.Glyphs.Length);
        Assert.Equal(102, positions.Glyphs[1].X);
        Assert.Equal(53, positions.Glyphs[1].Y);
    }

    [Theory]
    [InlineData("default", 1)]
    [InlineData("other-script", 2)]
    [InlineData("unselected", 2)]
    [InlineData("other-feature", 2)]
    public async Task Latin_ligatures_use_the_selected_default_language_feature(string selection, int expectedCount)
    {
        byte[] font = CreateLigatureFont(false);
        OpenTypeTableRange table = Assert.IsType<OpenTypeTableRange>(OpenTypeFont.ParseMetrics(font, CancellationToken.None).Gsub);
        if (selection == "default") WriteUInt32(font, table.Offset + 12, 0x44464C54);
        if (selection == "other-script") WriteUInt32(font, table.Offset + 12, 0x6379726C);
        if (selection == "unselected") WriteUInt16(font, table.Offset + 26, 0);
        if (selection == "other-feature") WriteUInt32(font, table.Offset + 32, 0x73616C74);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AA", 20, 0, 0, new(1_024), CancellationToken.None);
        Assert.Equal(expectedCount, positions.Glyphs.Length);
    }

    [Theory]
    [InlineData(50, 8, SvgErrorCode.UnsupportedFeature)]
    [InlineData(48, 1, SvgErrorCode.UnsupportedFeature)]
    [InlineData(40, 2, SvgErrorCode.UnsupportedFeature)]
    [InlineData(56, 2, SvgErrorCode.UnsupportedFeature)]
    [InlineData(58, 0, SvgErrorCode.MalformedInput)]
    [InlineData(58, 65535, SvgErrorCode.MalformedInput)]
    [InlineData(52, 33, SvgErrorCode.LimitExceeded)]
    [InlineData(64, 4097, SvgErrorCode.LimitExceeded)]
    [InlineData(60, 2, SvgErrorCode.MalformedInput)]
    [InlineData(70, 3, SvgErrorCode.MalformedInput)]
    [InlineData(72, 1, SvgErrorCode.MalformedInput)]
    [InlineData(72, 9, SvgErrorCode.LimitExceeded)]
    [InlineData(76, 3, SvgErrorCode.MalformedInput)]
    public async Task Ligature_table_failures_are_categorized_and_release_layout_storage(int fieldOffset, ushort value, SvgErrorCode expected)
    {
        byte[] font = CreateLigatureFont(false);
        OpenTypeTableRange table = Assert.IsType<OpenTypeTableRange>(OpenTypeFont.ParseMetrics(font, CancellationToken.None).Gsub);
        WriteUInt16(font, table.Offset + fieldOffset, value);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(font)], CancellationToken.None);
        RenderBudget budget = new(1_024);
        SvgException failure = Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AAA", 20, 0, 0, budget, CancellationToken.None));
        Assert.Equal(expected, failure.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Ligature_layout_releases_temporary_clusters_on_memory_work_and_cancellation_denial()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateLigatureFont(false))], CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        RenderBudget denied = new(128);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgGlyphLayout.CreateStorage(font, "AAA", 20, 0, 0, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        for (int maximumWork = 0; maximumWork < 120; maximumWork++)
        {
            RenderBudget budget = new(1_024);
            try { using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(font, "AAAAAA", 20, 0, 0, SvgTextAnchor.Start, budget, new(maximumWork), CancellationToken.None); }
            catch (SvgException failure) { Assert.Equal(SvgErrorCode.LimitExceeded, failure.ErrorCode); }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new(1_024);
        Assert.ThrowsAny<OperationCanceledException>(() => SvgGlyphLayout.CreateStorage(font, "AAA", 20, 0, 0, canceled, cancellation.Token));
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using SvgPositionedGlyphStorage retry = SvgGlyphLayout.CreateStorage(font, "AAA", 20, 0, 0, new(1_024), CancellationToken.None);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("linux")]
    public async Task Path_glyph_midpoint_uses_the_kerned_advance_before_device_rounding(string target)
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(CreateTriangleKernFont())], CancellationToken.None);
        SvgGlyphDeviceMetrics metrics = new(1.024, target == "windows" ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, true);
        using SvgPositionedGlyphStorage positions = SvgGlyphLayout.CreateStorage(Assert.Single(snapshot.Fonts), "AA", 20, 0, 0, 0, [], [], SvgTextAnchor.Start, metrics, new(1_024), string.Empty, null, CancellationToken.None);
        double kernedDeviceAdvance = target == "windows" ? 6.15625 : 6.140625;
        double unkernedDeviceAdvance = target == "windows" ? 12.296875 : 12.28125;
        Assert.Equal(kernedDeviceAdvance / 1.024, positions.Glyphs[0].Advance);
        Assert.Equal(kernedDeviceAdvance / 1.024, positions.Glyphs[1].X);
        Assert.Equal(unkernedDeviceAdvance / 1.024, positions.Glyphs[1].Advance);
    }

    private static byte[] CreateLigatureFont(bool rangeCoverage)
    {
        byte[] font = CreateTriangleFont();
        const int originalTableCount = 7;
        const int directoryLength = 12 + originalTableCount * 16;
        int originalLength = font.Length;
        int length = rangeCoverage ? 94 : 90;
        Array.Resize(ref font, originalLength + 16 + length);
        Array.Copy(font, directoryLength, font, directoryLength + 16, originalLength - directoryLength);
        for (int index = 0; index < originalTableCount; index++)
        {
            int entry = 12 + index * 16;
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8, 4));
            WriteUInt32(font, entry + 8, offset + 16);
        }
        WriteUInt16(font, 4, originalTableCount + 1);
        int table = originalLength + 16;
        WriteTable(font, originalTableCount, 0x47535542, table, length);
        foreach ((int offset, ushort value) in new (int, ushort)[]
        {
            (0, 1), (4, 10), (6, 30), (8, 44), (10, 1), (16, 8),
            (18, 4), (24, ushort.MaxValue), (26, 1), (30, 1), (36, 8),
            (40, 1), (44, 1), (46, 4), (48, 4), (52, 1), (54, 8),
            (56, 1), (58, 28), (60, 1), (62, 8), (64, 2), (66, 6),
            (68, 14), (70, 2), (72, 3), (74, 1), (76, 1), (78, 2),
            (80, 2), (82, 1), (84, rangeCoverage ? (ushort)2 : (ushort)1), (86, 1), (88, 1)
        }) WriteUInt16(font, table + offset, value);
        WriteUInt32(font, table + 12, 0x6C61746E);
        WriteUInt32(font, table + 32, 0x6C696761);
        if (rangeCoverage) WriteUInt16(font, table + 90, 1);
        return font;
    }
}
