using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(0x81, true)]
    [InlineData(0x82, false)]
    public void Curve_tag_ranges_are_inclusive_and_do_not_move_or_touch_points(byte opcode, bool expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4) { IsOnCurve = !expected }, new(5, 6, 5, 6) { IsOnCurve = !expected }, new(8, 9, 8, 9)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [3], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 2, opcode }, TrueTypeHintProgramKind.Glyph);
        Assert.True(points[0].IsOnCurve); Assert.True(points[3].IsOnCurve);
        Assert.Equal(expected, points[1].IsOnCurve); Assert.Equal(expected, points[2].IsOnCurve);
        Assert.Equal(3, points[1].X); Assert.Equal(4, points[1].Y);
        Assert.Equal(5, points[2].OriginalX); Assert.Equal(6, points[2].OriginalY);
        Assert.All(points, point => { Assert.False(point.TouchedX); Assert.False(point.TouchedY); });
    }

    [Theory]
    [InlineData(0x81)]
    [InlineData(0x82)]
    public void Reversed_curve_tag_ranges_are_empty_after_validating_endpoints(byte opcode)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), new(3, 4, 3, 4)];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [1], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB1, 1, 0, opcode }, TrueTypeHintProgramKind.Glyph);
        Assert.All(points, point => Assert.True(point.IsOnCurve));
    }

    [Theory]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, false)]
    public void ClearType_curve_tag_changes_after_both_IUPs_follow_platform_compatibility(bool windows, bool clearType, bool bothIups, bool waiver, bool expected)
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0)];
        // Original quadratic-font DirectWrite controls keep the same changed
        // tags before and after IUP, with or without the native-mode waiver.
        TrueTypeHintTarget target = Target with { Grid = clearType ? TrueTypeHintGrid.ClearType : TrueTypeHintGrid.Physical, WindowsArithmetic = windows };
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], points, [0], 16,
            new(65536, 65536), target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        if (waiver) machine.Prepare(Convert.FromHexString("B104038E"), target);
        byte[] program = bothIups ? [0x30, 0x31, 0xB1, 0, 0, 0x82] : [0x30, 0xB1, 0, 0, 0x82];
        machine.Run(program, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[0].IsOnCurve);
    }

    [Fact]
    public void Curve_tag_ranges_consume_work_per_visited_point_and_release_scratch_on_denial()
    {
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], new TrueTypeHintPoint[300], [299], 16,
            new(65536, 65536), Target, budget, new(20), CancellationToken.None))
            Assert.Equal(SvgErrorCode.LimitExceeded,
                Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B90000012B81"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
