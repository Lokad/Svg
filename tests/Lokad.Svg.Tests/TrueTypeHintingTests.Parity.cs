using Lokad.Svg.Rendering;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void Windows_arithmetic_does_not_invent_the_symmetric_getinfo_bit(bool windows, int odd)
    {
        TrueTypeHintTarget target = Target with { WindowsArithmetic = windows, QueryFlags = TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned, Grid = TrueTypeHintGrid.ClearType };
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], [], [], 48, new(98304, 98304), target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
        machine.Run(Convert.FromHexString("B05876B02856B0002342B8080088B0012342"), TrueTypeHintProgramKind.Glyph);
        Assert.Equal(odd, machine.Storage[0]);
        Assert.Equal(0, machine.Storage[1]);
        Assert.Equal(SvgErrorCode.MalformedInput, Assert.Throws<SvgException>(() => machine.Prepare(ReadOnlyMemory<byte>.Empty, target with { WindowsArithmetic = !windows })).ErrorCode);
    }

    [Theory]
    [InlineData("", 0, false, true, false, true)]
    [InlineData("", 21, false, true, false, true)]
    [InlineData("", 31, false, true, false, true)]
    [InlineData("", 32, true, false, true, false)]
    [InlineData("", 64, true, false, true, false)]
    [InlineData("", 96, false, true, false, true)]
    [InlineData("", 128, false, true, false, true)]
    [InlineData("", -31, false, true, false, true)]
    [InlineData("", -32, true, false, true, false)]
    [InlineData("", -96, false, true, false, true)]
    [InlineData("B05876", 21, false, false, false, true)]
    [InlineData("B05876", 40, false, false, true, false)]
    [InlineData("19", 40, false, false, true, false)]
    [InlineData("7A", 40, false, false, true, false)]
    public void Parity_preserves_native_round_state_and_physical_grid_distinctions(string rounding, int value,
        bool classicOdd, bool classicEven, bool symmetricOdd, bool symmetricEven)
    {
        // Independent DirectWrite outline-mask and FreeType 2.13.2 outline
        // witnesses distinguish both branches, including fractional results.
        Check(false, classicOdd, classicEven);
        Check(true, symmetricOdd, symmetricEven);

        void Check(bool symmetric, bool odd, bool even)
        {
            TrueTypeHintTarget target = Target with
            {
                QueryFlags = symmetric ? TrueTypeHintQueryFlags.Symmetric | TrueTypeHintQueryFlags.ClearType : TrueTypeHintQueryFlags.Grayscale,
                Grid = symmetric ? TrueTypeHintGrid.ClearType : TrueTypeHintGrid.Physical
            };
            using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 2, 0, 0), [], [], [], 48,
                new(98304, 98304), target, new RenderBudget(new HostMemoryBudget(long.MaxValue)), CancellationToken.None);
            byte[] prefix = Convert.FromHexString(rounding);
            byte[] program = new byte[prefix.Length + 16];
            prefix.CopyTo(program, 0);
            byte[] predicates = [0xB8, (byte)(value >> 8), (byte)value, 0x56, 0xB0, 0, 0x23, 0x42,
                0xB8, (byte)(value >> 8), (byte)value, 0x57, 0xB0, 1, 0x23, 0x42];
            predicates.CopyTo(program, prefix.Length);
            machine.Run(program, TrueTypeHintProgramKind.Glyph);
            Assert.Equal(odd ? 1 : 0, machine.Storage[0]);
            Assert.Equal(even ? 1 : 0, machine.Storage[1]);
        }
    }
}
