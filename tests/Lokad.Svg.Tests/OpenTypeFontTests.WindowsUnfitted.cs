using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(15, true)]
    public void Windows_fitting_matches_native_default_policy_independently_of_legacy_gridfit(int flags, bool fitting)
    {
        // Independent DirectWrite DEFAULT and factory-1 vectors: a symmetric
        // smoothing range uses its symmetric fitting bit; legacy GRIDFIT does
        // not override an unfitted symmetric range. Legacy ranges keep fitting.
        byte[] table = [0, 1, 0, 1, 255, 255, 0, (byte)flags];
        byte[] bytes = CreateWindowsProfileFont(table, true);
        OpenTypeWindowsGlyphProfile profile = OpenTypeFont.GetWindowsGlyphProfile(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(fitting, profile.GridFit);
        Assert.Equal((flags & 8) == 0 ? SvgGlyphLcdMode.Natural : SvgGlyphLcdMode.Symmetric, profile.LcdMode);
    }

    [Fact]
    public void Windows_unfitted_initial_points_keep_signed_halves_and_independent_axis_scales()
    {
        OpenTypeUnhintedDeviceScale half = OpenTypeUnhintedDeviceScale.CreateWindows(8, 8, 1024);
        foreach ((int input, int expectedX, int expectedY) in new[] { (-3, -1, -2), (-1, 0, -1), (1, 1, 1), (3, 2, 2) })
        {
            Assert.Equal(expectedY, half.Apply(input));
            Assert.Equal(expectedX, half.ApplyHorizontal(input));
        }
        OpenTypeUnhintedDeviceScale stretched = OpenTypeUnhintedDeviceScale.CreateWindows(8, 16, 1024);
        Assert.Equal(1, stretched.ApplyHorizontal(1));
        Assert.Equal(1, stretched.Apply(1));
        Assert.Equal(3, stretched.ApplyHorizontal(3));
        Assert.Equal(2, stretched.Apply(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Windows_unfitted_size_and_component_placement_precede_final_point_rounding(bool composite)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("00010001FFFF000A"), true);
        int head = FindTableOffset(bytes, 0x68656164);
        WriteUInt16(bytes, head + 18, 2048);
        WriteUInt16(bytes, head + 16, 8); // Authored integer-size flag survives the unfitted range.
        if (composite)
        {
            OpenTypeTableRange range = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
            WriteInt16(bytes, range.Offset, -1);
            WriteUInt16(bytes, range.Offset + 10, 519); // USE_MY_METRICS, ROUND_XY_TO_GRID, word offsets.
            WriteUInt16(bytes, range.Offset + 12, 1);
            WriteInt16(bytes, range.Offset + 14, 10);
            WriteInt16(bytes, range.Offset + 16, 10);
        }
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        ushort glyph = composite ? (ushort)2 : (ushort)1;
        using (OpenTypeGlyphContourStorage fractional = OpenTypeFont.GetWindowsUnfittedContours(bytes, glyph, metrics, 5.75, budget, new(long.MaxValue), CancellationToken.None))
        using (OpenTypeGlyphContourStorage integral = OpenTypeFont.GetWindowsUnfittedContours(bytes, glyph, metrics, 6, budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(integral.Segments, fractional.Segments);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetWindowsUnfittedContours(bytes, glyph, metrics, 8, budget, new(long.MaxValue), CancellationToken.None))
        {
            // Composite (10+10)*.25 = 5, rather than rounding both operands
            // to 3 and adding them, or snapping the unfitted component offset.
            Assert.Equal(composite ? 5 : 3, contours.Segments[0].Start.X);
            Assert.Equal(composite ? 5 : 3, contours.Segments[0].Start.Y);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(.5, 0)]
    [InlineData(1, .25)]
    public async Task Font_selected_unfitted_ranges_skip_unreached_programs_but_required_fitting_still_rejects_them(double scale, double translation)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("000100020008000AFFFF000F"), true);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(bytes, 1, CancellationToken.None);
        Array.Copy(bytes, glyph.Offset + 14, bytes, glyph.Offset + 15, 8);
        WriteUInt16(bytes, glyph.Offset + 12, 1);
        bytes[glyph.Offset + 14] = 0x4C; // Valid but unsupported MPS, reached only in fitted ranges.
        byte[] unchanged = bytes.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(1, 0, 0, 0), 8, new(1, 0, 0, 1, translation, 0), budget))
        {
            using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(scale, 0, scale, 0, budget, new(long.MaxValue), CancellationToken.None))
                Assert.NotEmpty(contours.Segments);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => source.CreateWindowsContours(1.25, 0, 1.25, 0, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Windows_unfitted_negative_outline_points_keep_distinct_axis_half_rules()
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("00010001FFFF000A"), true);
        WriteUInt16(bytes, FindTableOffset(bytes, 0x68656164) + 18, 2048);
        OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(bytes, 1, CancellationToken.None);
        WriteInt16(bytes, glyph.Offset + 2, 10);
        WriteInt16(bytes, glyph.Offset + 4, -10);
        WriteInt16(bytes, glyph.Offset + 6, 20);
        WriteInt16(bytes, glyph.Offset + 8, 0);
        WriteInt16(bytes, FindTableOffset(bytes, 0x686D7478) + 6, 10);
        bytes[glyph.Offset + 14] = 0x17; // First point (10,-10), then (20,-10) and (10,0).
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, budget, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(3, contours.Segments[0].Start.X);
            Assert.Equal(-3, contours.Segments[0].Start.Y);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Windows_unfitted_component_metrics_rebase_after_scaling_the_inherited_origin()
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("00010001FFFF000A"), true);
        WriteUInt16(bytes, FindTableOffset(bytes, 0x68656164) + 18, 2048);
        OpenTypeTableRange child = OpenTypeFont.GetGlyphDataRange(bytes, 1, CancellationToken.None);
        WriteInt16(bytes, child.Offset + 2, 10);
        WriteInt16(bytes, FindTableOffset(bytes, 0x686D7478) + 6, 4);
        OpenTypeTableRange parent = OpenTypeFont.GetGlyphDataRange(bytes, 2, CancellationToken.None);
        WriteInt16(bytes, parent.Offset, -1);
        WriteInt16(bytes, parent.Offset + 2, 20);
        WriteUInt16(bytes, parent.Offset + 10, 515);
        WriteUInt16(bytes, parent.Offset + 12, 1);
        WriteInt16(bytes, parent.Offset + 14, 10);
        WriteInt16(bytes, parent.Offset + 16, 0);
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetWindowsUnfittedContours(bytes, 2, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, budget, new(long.MaxValue), CancellationToken.None))
        {
            // Placed point 20 -> 5; inherited metric origin (10-4) -> 2.
            // Rebasing first would incorrectly round (20-6)*.25 to 4.
            Assert.Equal(3, contours.Segments[0].Start.X);
            Assert.Equal(6, contours.Segments[0].End.X);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Windows_unfitted_horizontal_scale_keeps_numeric_limits(double ratio)
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("00010001FFFF000A"), true);
        RenderBudget budget = new(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 8, ratio, null, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Windows_unfitted_owners_release_on_denial_work_and_cancellation_including_size_collapse()
    {
        byte[] bytes = CreateWindowsProfileFont(Convert.FromHexString("00010001FFFF000A"), true);
        WriteUInt16(bytes, FindTableOffset(bytes, 0x68656164) + 16, 8);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, metrics, 8, budget, new(long.MaxValue), CancellationToken.None))
            Assert.NotEmpty(contours.Segments);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, metrics, 8, denied, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, metrics, 8, budget, new(1), CancellationToken.None)).ErrorCode);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, metrics, .001, budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        using (OpenTypeGlyphContourStorage collapsed = OpenTypeFont.GetWindowsUnfittedContours(bytes, 1, metrics, .001, budget, new(long.MaxValue), CancellationToken.None))
            Assert.Empty(collapsed.Segments);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
