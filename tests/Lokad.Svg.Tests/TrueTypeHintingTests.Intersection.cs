using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData("0,64,128,64,64,0,64,128", 64, 64)]
    [InlineData("0,0,128,128,0,128,128,0", 64, 64)]
    [InlineData("0,0,32,32,96,0,96,64", 96, 96)]
    [InlineData("-128,-128,128,128,-64,64,64,-64", 0, 0)]
    [InlineData("0,0,127,65,0,65,127,0", 64, 33)]
    [InlineData("0,0,-127,-65,0,-65,-127,0", -64, -33)]
    [InlineData("0,0,1280,0,0,128,1280,191", 640, 79)]
    [InlineData("0,0,1216,0,0,128,1216,192", 608, 80)]
    [InlineData("0,0,1216,0,0,128,1216,193", -2395, 0)]
    [InlineData("0,0,128,0,0,128,128,128", 64, 64)]
    [InlineData("0,0,128,0,128,128,0,128", 64, 64)]
    [InlineData("0,0,0,0,0,128,128,128", 32, 64)]
    [InlineData("-2,-2,-2,-2,-1,-1,-1,-1", -1, -1)]
    public void Intersection_matches_independent_native_line_vectors(string coordinates, int x, int y)
    {
        // Independently executed FreeType VER-2-13-2 Ins_ISECT/FT_MulDiv vectors,
        // including its 1/19 grazing bound; see the pinned upstream notice.
        int[] values = coordinates.Split(',').Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        TrueTypeHintPoint[] points = [new(17, 23, 17, 23), new(values[0], values[1], 1, 2),
            new(values[2], values[3], 3, 4), new(values[4], values[5], 5, 6), new(values[6], values[7], 7, 8)];
        TrueTypeHintPoint[] unchanged = points.ToArray();
        using TrueTypeHinting machine = Create(points, [4], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("00B400010203040F"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(x, points[0].X); Assert.Equal(y, points[0].Y);
        Assert.True(points[0].TouchedX); Assert.True(points[0].TouchedY);
        Assert.Equal(17, points[0].OriginalX); Assert.Equal(23, points[0].OriginalY);
        Assert.Equal(unchanged[1..], points[1..]);
    }

    [Fact]
    public void Intersection_reads_distinct_zones_and_keeps_twilight_originals()
    {
        TrueTypeHintPoint[] points = [new(0, 64, 0, 64), new(128, 64, 128, 64)];
        using TrueTypeHinting machine = Create(points, [1], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        // Initialize two twilight points to (64,0) / (64,128), then intersect
        // that line with the glyph line into independent twilight slot 2.
        machine.Run(Convert.FromHexString("B0001501B1004048B101404800B90001008048B00013B402000100010F01B00246B000234200B00246B001234201B00247B0022342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(64, machine.Storage[0]); Assert.Equal(64, machine.Storage[1]);
        Assert.Equal(0, machine.Storage[2]);
        Assert.Equal(0, points[0].X); Assert.Equal(64, points[0].Y);
    }

    [Theory]
    [InlineData("B400010203050F")]
    [InlineData("B400010205040F")]
    [InlineData("B400010503040F")]
    [InlineData("B400050203040F")]
    [InlineData("B405010203040F")]
    [InlineData("B3000102030F")]
    public void Intersection_rejects_invalid_points_and_stack_without_mutating_the_outline(string program)
    {
        TrueTypeHintPoint[] points = [new(17, 23, 17, 23), new(0, 64, 0, 64), new(128, 64, 128, 64), new(64, 0, 64, 0), new(64, 128, 64, 128)];
        TrueTypeHintPoint[] unchanged = points.ToArray();
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = Create(points, [4], [], budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(unchanged, points);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Intersection_handles_aliased_destination_and_preserves_work_and_cancellation_bounds()
    {
        TrueTypeHintPoint[] points = [new(0, 64, 0, 64), new(128, 64, 128, 64), new(64, 0, 64, 0), new(64, 128, 64, 128)];
        RenderBudget budget = new(long.MaxValue);
        ParserWorkCounter work = new(long.MaxValue);
        byte[] program = Convert.FromHexString("B400000102030F");
        using (TrueTypeHinting machine = Create(points, [3], [], budget, work, CancellationToken.None))
            machine.Run(program, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(64, points[0].X); Assert.Equal(64, points[0].Y);
        Assert.Equal(0, points[0].OriginalX);
        using (TrueTypeHinting machine = Create(points, [3], [], budget, new(work.Used - 1), CancellationToken.None))
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => machine.Run(program, TrueTypeHintProgramKind.Glyph)).ErrorCode);
        using CancellationTokenSource cancelled = new();
        using (TrueTypeHinting machine = Create(points, [3], [], budget, new(long.MaxValue), cancelled.Token))
        {
            cancelled.Cancel();
            Assert.Equal(cancelled.Token, Assert.ThrowsAny<OperationCanceledException>(() => machine.Run(program, TrueTypeHintProgramKind.Glyph)).CancellationToken);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
