using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class TrueTypeHintingTests
{
    private static TrueTypeHintTarget Target => new(35, TrueTypeHintQueryFlags.Grayscale, TrueTypeHintControlScale.FractionalFontUnits, TrueTypeHintPreparation.Single);

    [Theory]
    [InlineData(2, (int)TrueTypeHintQueryFlags.Rotated)]
    [InlineData(4, (int)TrueTypeHintQueryFlags.Stretched)]
    [InlineData(8, (int)TrueTypeHintQueryFlags.FontVariations)]
    [InlineData(16, (int)TrueTypeHintQueryFlags.VerticalPhantoms)]
    [InlineData(32, (int)TrueTypeHintQueryFlags.Grayscale)]
    [InlineData(64, (int)TrueTypeHintQueryFlags.ClearType)]
    [InlineData(128, (int)TrueTypeHintQueryFlags.CompatibleWidths)]
    [InlineData(256, (int)TrueTypeHintQueryFlags.HorizontalStripes)]
    [InlineData(512, (int)TrueTypeHintQueryFlags.Bgr)]
    [InlineData(1024, (int)TrueTypeHintQueryFlags.SubpixelPositioned)]
    [InlineData(2048, (int)TrueTypeHintQueryFlags.Symmetric)]
    [InlineData(4096, (int)TrueTypeHintQueryFlags.ClearTypeGrayscale)]
    public void Getinfo_keeps_each_query_in_its_declared_result_bit(int selector, int expected)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 8, 0, 0), [], [], [], 48, 65536, Target with { Version = 42, QueryFlags = TrueTypeHintQueryFlags.All }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(new byte[] { 0xB8, (byte)(selector >> 8), (byte)selector, 0x88, 0xB0, 0, 0x23, 0x42 }, TrueTypeHintProgramKind.Glyph);
        Assert.Equal((int)expected, machine.Storage[0]);
    }

    [Fact]
    public void Getinfo_combines_only_selected_supported_bits_and_requested_version()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 8, 0, 0), [], [], [], 48, 65536, Target with { Version = 42, QueryFlags = TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned | TrueTypeHintQueryFlags.Symmetric }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B8184188B0002342B00088B0012342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(42 | (int)(TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.Symmetric), machine.Storage[0]);
        Assert.Equal(0, machine.Storage[1]);
    }

    [Fact]
    public void Rejects_unknown_target_flags_before_scratch_allocation()
    {
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => TrueTypeHinting.Create(new(8, 8, 0, 0), [], [], [], 48, 65536, Target with { QueryFlags = (TrueTypeHintQueryFlags)(1 << 20) }, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Theory]
    [InlineData("B9FFFF000362B0002342", -21)] // DIV truncates -64/3 toward zero.
    [InlineData("B9FFFF002063B0002342", -1)] // MUL rounds the negative half unit.
    [InlineData("B97FFF7FFF60B0002342", 65534)]
    [InlineData("B101032350B0002342", 0)] // SWAP makes 3 < 1 false.
    public void Keeps_signed_push_arithmetic_and_stack_order(string program, int expected)
    {
        var budget = new RenderBudget(long.MaxValue);
        using (TrueTypeHinting machine = Create([], [], [0], budget, new(long.MaxValue), CancellationToken.None))
        {
            machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph);
            Assert.Equal(expected, machine.Storage[0]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Distinguishes_fitted_and_original_distances_after_point_rounding()
    {
        TrueTypeHintPoint[] points = [new(40, 0, 40, 0), new(100, 0, 100, 0)];
        using TrueTypeHinting machine = Create(points, [1], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);

        machine.Run(Convert.FromHexString("B0002FB0012FB1000149B0002342B100014AB0012342"), TrueTypeHintProgramKind.Glyph);

        Assert.Equal(64, points[0].X);
        Assert.Equal(128, points[1].X);
        Assert.True(points[0].TouchedX);
        Assert.False(points[0].TouchedY);
        Assert.Equal(-64, machine.Storage[0]);
        Assert.Equal(-60, machine.Storage[1]);
    }

    [Fact]
    public void Initializes_twilight_originals_from_cvt_before_rounding()
    {
        using TrueTypeHinting machine = Create([], [], [80], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("00B00013B101003FB00015B00147B0002342B00146B0012342"), TrueTypeHintProgramKind.Preparation);

        Assert.Equal(80, machine.Storage[0]);
        Assert.Equal(64, machine.Storage[1]);
    }

    [Fact]
    public void Interpolates_reversed_references_and_shifts_points_outside_them()
    {
        TrueTypeHintPoint[] points = [new(300, 0, 300, 0), new(150, 0, 150, 0), new(0, 0, 0, 0), new(-50, 0, -50, 0), new(350, 0, 350, 0)];
        points[0].X = 320; points[0].TouchedX = points[2].TouchedX = true;
        using TrueTypeHinting machine = Create(points, [4], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);

        machine.Run(new byte[] { 0x31 }, TrueTypeHintProgramKind.Glyph);

        Assert.Equal(new int[] { 320, 160, 0, -50, 370 }, points.Select(point => point.X));
        Assert.False(points[1].TouchedX);
        Assert.Equal(150, points[1].OriginalX);
    }

    [Fact]
    public void Preparation_restores_cvt_but_retains_storage_functions_and_explicit_queries()
    {
        using TrueTypeHinting machine = Create([], [], [64], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B0002CB020882D"), TrueTypeHintProgramKind.Font);
        byte[] prep = Convert.FromHexString("B00043B00160B0002342B00045B00160B0002344B0002BB0012342");
        machine.Prepare(prep, Target with { QueryFlags = TrueTypeHintQueryFlags.None });
        Assert.Equal(1, machine.Storage[0]);
        Assert.Equal(0, machine.Storage[1]);
        Assert.Equal(65, machine.ControlValues[0]);

        machine.Prepare(prep, Target);

        Assert.Equal(2, machine.Storage[0]);
        Assert.Equal(4096, machine.Storage[1]);
        Assert.Equal(65, machine.ControlValues[0]);
        Assert.Equal(1, machine.DefinedFunctions);
    }

    [Theory]
    [InlineData("B000584103581B5900B001584001591BB063591BB107054259", 5)]
    [InlineData("B00158B10704421B4002585959", 4)]
    public void Skips_nested_conditionals_without_treating_push_data_as_instructions(string program, int expected)
    {
        using TrueTypeHinting machine = Create([], [], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, machine.Storage[7]);
    }

    [Theory]
    [InlineData(false, 32)]
    [InlineData(true, 64)]
    public void Instruction_control_restores_default_glyph_rounding_when_requested(bool reset, int expected)
    {
        TrueTypeHintPoint[] points = [new(32, 0, 32, 0)];
        using TrueTypeHinting machine = Create(points, [0], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Prepare(reset ? Convert.FromHexString("19B102028E") : new byte[] { 0x19 }, Target);
        machine.Run(Convert.FromHexString("B0002F"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(expected, points[0].X);
    }

    [Fact]
    public void Instruction_control_inhibits_glyph_execution()
    {
        using TrueTypeHinting machine = Create([], [], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Prepare(Convert.FromHexString("B101018E"), Target);
        machine.Run(new byte[] { 0x06 }, TrueTypeHintProgramKind.Glyph);
    }

    [Theory]
    [InlineData("21")]
    [InlineData("B8FF")]
    [InlineData("4002FF")]
    [InlineData("B00025")]
    [InlineData("B1080342")]
    [InlineData("B1010044")]
    [InlineData("B00213")]
    [InlineData("B00234")]
    [InlineData("B0012F")]
    [InlineData("B1020062")]
    [InlineData("B97FFF7FFF63B87FFF63")]
    [InlineData("B0002B")]
    [InlineData("B0002C2D")]
    [InlineData("B00158")]
    [InlineData("B00058")]
    [InlineData("59")]
    [InlineData("1B")]
    [InlineData("B000584102002059")]
    [InlineData("B8FFFC1C")]
    public void Categorizes_invalid_operands_and_flow_without_leaking_scratch(string program)
    {
        var budget = new RenderBudget(long.MaxValue);
        using (TrueTypeHinting machine = Create([new(0, 0, 0, 0)], [0], [0], budget, new(long.MaxValue), CancellationToken.None))
            Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("B0002CB007")]
    [InlineData("B0002CB0012C2D2D")]
    [InlineData("2D")]
    public void Rejects_unterminated_nested_or_stray_function_definitions(string program)
    {
        using TrueTypeHinting machine = Create([], [], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Font)).ErrorCode);
    }

    [Theory]
    [InlineData("06")]
    [InlineData("89")]
    public void Executed_unimplemented_instructions_remain_explicit(string program)
    {
        using TrueTypeHinting machine = Create([], [], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString(program), TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }

    [Fact]
    public void Rejects_vertical_phantom_access_without_inventing_metrics()
    {
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0) { Unavailable = true }];
        using TrueTypeHinting machine = Create(points, [], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B00046"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }

    [Fact]
    public void Bounds_back_jumps_by_work_and_recursive_calls_by_depth()
    {
        var budget = new RenderBudget(long.MaxValue);
        using (TrueTypeHinting machine = Create([], [], [], budget, new(100), CancellationToken.None))
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B8FFFD1C"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (TrueTypeHinting machine = Create([], [], [], budget, new(long.MaxValue), CancellationToken.None))
        {
            machine.Run(Convert.FromHexString("B0002CB0002B2D"), TrueTypeHintProgramKind.Font);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B0002B"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Bounds_empty_function_loops_and_program_bytes()
    {
        using TrueTypeHinting machine = Create([], [], [], new(long.MaxValue), new(100), CancellationToken.None);
        machine.Run(Convert.FromHexString("B0002C2D"), TrueTypeHintProgramKind.Font);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => machine.Run(new byte[65536], TrueTypeHintProgramKind.Glyph)).ErrorCode);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B87FFFB0002A"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }

    [Fact]
    public void Enforces_declared_stack_capacity_during_a_push()
    {
        using TrueTypeHinting machine = Create([], [], [], new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        byte[] push = new byte[19]; push[0] = 0x40; push[1] = 17;
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(push, TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_query_policies_and_changes_to_the_creation_scale()
    {
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => TrueTypeHinting.Create(new(16, 8, 4, 8), [], [], [], 16, 65536, Target with { Version = 256 }, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
        using TrueTypeHinting machine = Create([], [], [], budget, new(long.MaxValue), CancellationToken.None);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Prepare(ReadOnlyMemory<byte>.Empty, Target with { ControlScale = TrueTypeHintControlScale.FullPrecision })).ErrorCode);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(ReadOnlyMemory<byte>.Empty, (TrueTypeHintProgramKind)3)).ErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void Reserves_machine_arrays_before_allocation(long maximumBytes)
    {
        var budget = new RenderBudget(maximumBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Create([], [], [0], budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
    }

    [Fact]
    public void Releases_partial_initialization_on_work_expiry_then_allows_retry()
    {
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Create([], [], [1, 2], budget, new(2), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using (TrueTypeHinting retry = Create([], [], [1, 2], budget, new(long.MaxValue), CancellationToken.None)) Assert.Equal(2, retry.ControlValues[1]);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Observes_cancellation_before_allocation_and_on_empty_or_inhibited_programs()
    {
        using var cancellation = new CancellationTokenSource();
        var budget = new RenderBudget(long.MaxValue);
        using (TrueTypeHinting machine = Create([], [], [], budget, new(long.MaxValue), cancellation.Token))
        {
            machine.Prepare(Convert.FromHexString("B101018E"), Target);
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => machine.Run(ReadOnlyMemory<byte>.Empty, TrueTypeHintProgramKind.Glyph));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.ThrowsAny<OperationCanceledException>(() => Create([], [], [], budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData(4097, 0, 0, 0)]
    [InlineData(16, 4097, 0, 0)]
    [InlineData(16, 0, 1025, 0)]
    [InlineData(16, 0, 0, 4097)]
    public void Rejects_excess_declared_storage_before_reserving(int stack, int storage, int functions, int twilight)
    {
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => TrueTypeHinting.Create(new(stack, storage, functions, twilight), [], [], [], 16, 65536, Target, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Fact]
    public void An_empty_program_can_declare_zero_stack_storage()
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(0, 0, 0, 0), [], [], [], 16, 65536, Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(ReadOnlyMemory<byte>.Empty, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B000"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 2)]
    public void Rejects_disordered_or_out_of_range_contour_ends(ushort first, ushort last)
    {
        var budget = new RenderBudget(long.MaxValue);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => Create([new(0, 0, 0, 0), new(1, 0, 1, 0)], [first, last], [], budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().PeakBytes);
    }

    [Fact]
    public void Disposal_releases_program_borrows_and_rejects_reuse()
    {
        var budget = new RenderBudget(long.MaxValue);
        TrueTypeHinting machine = Create([], [], [], budget, new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B0002C2D"), TrueTypeHintProgramKind.Font);
        machine.Dispose(); machine.Dispose();
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Throws<InvalidOperationException>(() => machine.Run(ReadOnlyMemory<byte>.Empty, TrueTypeHintProgramKind.Glyph));
        Assert.Throws<InvalidOperationException>(() => machine.Prepare(ReadOnlyMemory<byte>.Empty, Target));
        Assert.Throws<InvalidOperationException>(() => machine.Storage.ToArray());
    }

    private static TrueTypeHinting Create(TrueTypeHintPoint[] points, ushort[] ends, int[] cvt, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => TrueTypeHinting.Create(new(16, 8, 4, 8), cvt, points, ends, 16, 65536, Target, budget, work, token);
}
