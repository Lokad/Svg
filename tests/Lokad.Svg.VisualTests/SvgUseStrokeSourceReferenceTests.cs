using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgUseStrokeSourceReferenceTests
{
    [Theory]
    [InlineData("integer-path-use-miter-leaf-opacity", 0, 0, 0)]
    [InlineData("integer-path-use-miter-parent-opacity", 0, 0, 0)]
    [InlineData("integer-path-use-round-leaf-opacity", 0, 0, 0)]
    [InlineData("integer-path-use-round-parent-opacity", 0, 0, 0)]
    public async Task Integer_path_instance_strokes_retain_browser_comparisons_through_all_six_outputs(string name, int pixels, int channel, int alpha)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", name + ".svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-" + name + ".png")), 41 * 35);
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, 41 * 35));
        Assert.True(comparison.DimensionsMatch);
        Assert.Equal(pixels == 0, comparison.IsExact);
        Assert.Equal(pixels, comparison.ChangedPixelCount);
        Assert.Equal(channel, comparison.MaximumChannelError);
        Assert.Equal(alpha, comparison.MaximumAlphaError);
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
