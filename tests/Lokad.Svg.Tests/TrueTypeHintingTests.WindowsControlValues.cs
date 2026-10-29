using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class TrueTypeHintingTests
{
    // Native glyph programs read the control value and magnify one coordinate
    // unit into a pixel. The resulting four-pixel band gives these independent
    // values, including the different signed-half rules of the two table axes.
    [Theory]
    [InlineData(1000, 11.5, 1, 714, 526)]
    [InlineData(1000, 11.5, 1, -714, -526)]
    [InlineData(1000, 11.5, .9990234375, 714, 526)]
    [InlineData(1000, 11.5, 1.0009765625, 714, 526)]
    [InlineData(1000, 9, 1.1240234375, 546, 353)]
    [InlineData(1000, 9, 1.1240234375, -546, -353)]
    [InlineData(1000, 9, 1.1240234375, 16384, 10608)]
    [InlineData(1000, 9, 1.1240234375, -16384, -10608)]
    [InlineData(1024, 8, 1, 1, 1)]
    [InlineData(1024, 8, 1, -1, 0)]
    public void Windows_control_initialization_keeps_the_native_axis_precision(ushort unitsPerEm, double size, double ratio, int value, int native)
    {
        TrueTypeHintDeviceScale scale = DeviceScale(size, ratio, unitsPerEm);
        Assert.Equal(native, scale.ScaleWindowsControl(value));
    }

    [Theory]
    [InlineData(1.09765625, 399, 0x00, 363)]
    [InlineData(1.09765625, -399, 0x00, -363)]
    [InlineData(1.1240234375, 503, 0x00, 448)]
    [InlineData(1.1240234375, -503, 0x00, -448)]
    [InlineData(.625, 1, 0x01, 1)]
    public void Windows_control_reads_use_the_native_fixed_point_ratio(double ratio, int backing, byte axisOpcode, int native)
    {
        TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Symmetric, true).CreateHintTarget(ratio);
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [backing], [], [], 12,
            DeviceScale(12, ratio, 1000), target, new(long.MaxValue), CancellationToken.None);
        byte[] program = [axisOpcode, 0xB0, 0, 0x45, 0xB0, 0, 0x23, 0x42];
        machine.Run(program, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(native, machine.Storage[0]);
    }

    [Theory]
    [InlineData(1.09765625, 599, 0x00, 0x01, 658)]
    [InlineData(1.09765625, -599, 0x00, 0x01, -658)]
    [InlineData(1.1240234375, 383, 0x00, 0x01, 430)]
    [InlineData(1.1240234375, -383, 0x00, 0x01, -430)]
    [InlineData(.625, 1, 0x01, 0x00, 2)]
    public void Windows_control_writes_divide_by_the_quantized_read_ratio(double ratio, short value, byte writeAxisOpcode, byte readAxisOpcode, int native)
    {
        TrueTypeHintTarget target = new OpenTypeWindowsGlyphProfile(SvgGlyphLcdMode.Symmetric, true).CreateHintTarget(ratio);
        using TrueTypeHinting machine = TrueTypeHinting.Create(new(8, 1, 0, 0), [0], [], [], 12,
            DeviceScale(12, ratio, 1000), target, new(long.MaxValue), CancellationToken.None);
        ushort encoded = unchecked((ushort)value);
        byte[] program = [writeAxisOpcode, 0xB9, 0, 0, (byte)(encoded >> 8), (byte)encoded, 0x44,
            readAxisOpcode, 0xB0, 0, 0x45, 0xB0, 0, 0x23, 0x42];
        machine.Run(program, TrueTypeHintProgramKind.Glyph);
        Assert.Equal(native, machine.Storage[0]);
    }

    private static TrueTypeHintDeviceScale DeviceScale(double size, double ratio, ushort unitsPerEm)
    {
        double horizontal = size * ratio;
        return new(checked((int)Math.Floor(size * 64 * 65536 / unitsPerEm + .5)),
            checked((int)Math.Floor(horizontal * 64 * 65536 / unitsPerEm + .5)))
        {
            WindowsVertical = (float)size / unitsPerEm,
            WindowsHorizontal = (float)horizontal / unitsPerEm
        };
    }
}
