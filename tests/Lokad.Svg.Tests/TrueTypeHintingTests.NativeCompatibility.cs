using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(true, true, 0, true, false, false, 0)]
    [InlineData(true, true, 0, true, true, true, 0)]
    [InlineData(true, true, 0, true, false, true, 0)]
    [InlineData(true, true, 1, true, false, true, 1)]
    [InlineData(true, true, 0, false, false, true, 1)]
    [InlineData(true, true, -1, true, false, true, 1)]
    [InlineData(true, false, 0, true, false, true, 1)]
    [InlineData(false, true, 0, true, false, true, 1)]
    public void Native_VacuFormRound_switch_requires_the_exact_function_zero_prefix(bool windows, bool clearType, int function, bool matching, bool waiver, bool interpolated, int expected)
    {
        // Original-font DirectWrite controls independently read the switch
        // before/after IUP, in native mode, and with a changed prefix/function.
        byte[] definition = Convert.FromHexString("B0002C4523466020B02660B004262348482D");
        if (function >= 0) definition[1] = (byte)function;
        if (!matching) definition[3] = 0x21;
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0)];
        TrueTypeHintTarget target = Target with { Grid = clearType ? TrueTypeHintGrid.ClearType : TrueTypeHintGrid.Physical, WindowsArithmetic = windows };
        RenderBudget budget = new(long.MaxValue);
        using (TrueTypeHinting machine = TrueTypeHinting.Create(new(16, 10, 2, 0), [64], points, [0], 16,
            new(65536, 65536), target, budget, new(long.MaxValue), CancellationToken.None))
        {
            if (function >= 0) machine.Run(definition, TrueTypeHintProgramKind.Font);
            machine.Prepare(Convert.FromHexString(waiver ? "B1080142B104038E" : "B1080142"), target);
            machine.Run(Convert.FromHexString(interpolated ? "3031B00843B0092342" : "B00843B0092342"), TrueTypeHintProgramKind.Glyph);
            Assert.Equal(expected, machine.Storage[9]);
            Assert.Equal(1, machine.Storage[8]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void VacuFormRound_reads_in_preparation_and_glyphs_preserve_writes_and_redefinitions()
    {
        TrueTypeHintTarget target = Target with { Grid = TrueTypeHintGrid.ClearType, WindowsArithmetic = true };
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(16, 10, 1, 0), [64], [], [], 16,
            new(65536, 65536), target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B0002C4523466020B02660B004262348482D"), TrueTypeHintProgramKind.Font);
        machine.Prepare(Convert.FromHexString("B1080142B109084342"), target);
        Assert.Equal(0, machine.Storage[9]);
        machine.Run(Convert.FromHexString("B1080242B00843B0092342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(0, machine.Storage[9]);
        Assert.Equal(2, machine.Storage[8]);
        machine.Run(Convert.FromHexString("B0002C2D"), TrueTypeHintProgramKind.Font);
        machine.Run(Convert.FromHexString("B00843B0092342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(2, machine.Storage[9]);
    }

    [Fact]
    public void A_native_compatibility_switch_does_not_hide_out_of_range_storage()
    {
        TrueTypeHintTarget target = Target with { Grid = TrueTypeHintGrid.ClearType, WindowsArithmetic = true };
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(16, 8, 1, 0), [64], [], [], 16,
            new(65536, 65536), target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Run(Convert.FromHexString("B0002C4523466020B02660B004262348482D"), TrueTypeHintProgramKind.Font);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Run(Convert.FromHexString("B00843"), TrueTypeHintProgramKind.Glyph)).ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Font_unit_control_writes_keep_their_scale_in_simple_and_composite_glyphs(bool composite)
    {
        // Native RCVT witnesses retain -78 for a -156 font-unit parent write
        // at half scale; assembled device points do not change operand units.
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), default, default, default, default];
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [64], [], [], 16,
            new(32768, 32768), Target with { WindowsArithmetic = true }, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.HintGlyph(Convert.FromHexString("B90000FF6470"), points, [0], composite);
        Assert.Equal(-78, machine.ControlValues[0]);
        Assert.Equal(0, points[0].X);
    }

    [Theory]
    [InlineData(511, 0, 16, true, false)]
    [InlineData(511, 1, 16, true, true)]
    [InlineData(511, 2, 16, false, false)]
    [InlineData(511, 4, 16, false, false)]
    [InlineData(0, 1, 16, false, false)]
    [InlineData(272, 1, 16, true, true)]
    [InlineData(272, 1, 17, false, false)]
    [InlineData(4607, 1, 16, false, false)]
    public void Authored_simple_dropout_modes_observe_scan_control(int control, byte scanType, int ppem, bool simple, bool excludeStubs)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], ppem,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Prepare(new byte[] { 0xB8, (byte)(control >> 8), (byte)control, 0x85, 0xB0, scanType, 0x8D }, Target);
        machine.Run(ReadOnlyMemory<byte>.Empty, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(simple, machine.GlyphSimpleDropouts);
        Assert.Equal(excludeStubs, machine.GlyphExcludeDropoutStubs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Composite_and_following_glyph_restore_prepared_simple_dropout_policy(byte scanType)
    {
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 0, 0, 0), [], [], [], 16,
            new(65536, 65536), Target, new(long.MaxValue), new(long.MaxValue), CancellationToken.None);
        machine.Prepare(new byte[] { 0xB8, 1, 255, 0x85, 0xB0, scanType, 0x8D }, Target);
        TrueTypeHintPoint[] points = [new(0, 0, 0, 0), default, default, default, default];
        machine.HintGlyph(Convert.FromHexString("B0028D"), points, [0], false);
        Assert.False(machine.GlyphSimpleDropouts);
        machine.HintGlyph(ReadOnlyMemory<byte>.Empty, points, [0], true);
        Assert.True(machine.GlyphSimpleDropouts);
        Assert.Equal(scanType == 1, machine.GlyphExcludeDropoutStubs);
        machine.HintGlyph(ReadOnlyMemory<byte>.Empty, points, [0], false);
        Assert.True(machine.GlyphSimpleDropouts);
    }
}
