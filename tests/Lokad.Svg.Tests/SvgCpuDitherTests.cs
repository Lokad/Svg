using System.Globalization;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCpuDitherTests
{
    [Fact]
    public void Owning_bitmap_phase_matches_compiled_native_float_lanes_and_pixels()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/cpu-dither-native.csv"));
        Assert.Equal(3171, rows.Length);
        foreach (string row in rows.Skip(1))
        {
            int[] v = row.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            SvgCpuRasterGrid grid = new(v[0], v[1]);
            const float scale = 1f / 255f;
            float alpha = 69 * scale, opacity = 202 * scale;
            SvgShaderPaint sample = new SvgShaderPaint(23 * scale * alpha * opacity, 169 * scale * alpha * opacity, 205 * scale * alpha * opacity, alpha * opacity).Dither(grid.GetGradientDither(v[2], v[3]));
            Assert.Equal(v.Skip(6).Take(4), new[] { BitConverter.SingleToInt32Bits(sample.Red), BitConverter.SingleToInt32Bits(sample.Green), BitConverter.SingleToInt32Bits(sample.Blue), BitConverter.SingleToInt32Bits(sample.Alpha) });
            byte[] destination = [11, 23, 37, 97];
            sample.Blend(destination, 191);
            Assert.Equal(v.Skip(10).Select(value => (byte)value), destination);
        }
    }

    [Theory]
    [InlineData("cpu-dither-wide-linear.svg")]
    [InlineData("cpu-dither-radial.svg")]
    [InlineData("shader-coordinate-linear.svg")]
    [InlineData("shader-coordinate-affine.svg")]
    public async Task Six_outputs_agree_for_rectangle_shaders_across_cpu_contexts(string source)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", source));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }
}
