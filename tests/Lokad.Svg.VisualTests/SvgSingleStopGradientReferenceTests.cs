using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSingleStopGradientReferenceTests
{
    [Theory]
    [InlineData("one-stop-alpha-direct", 0, 0)]
    [InlineData("radial-one-stop-alpha-direct", 0, 0)]
    [InlineData("one-stop-alpha-leaf", 177, 6)]
    [InlineData("one-stop-alpha-parent", 252, 6)]
    [InlineData("override-one-stop-alpha-leaf", 177, 6)]
    public async Task Single_stop_gradients_preserve_browser_comparisons_through_all_six_outputs(string name, int pixels, int channel)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", name + ".svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-" + name + ".png")), 61 * 47);
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, 61 * 47));
        Assert.True(comparison.DimensionsMatch);
        Assert.Equal(pixels == 0, comparison.IsExact);
        Assert.Equal(pixels, comparison.ChangedPixelCount);
        Assert.Equal(channel, comparison.MaximumChannelError);
        Assert.Equal(0, comparison.MaximumAlphaError);
        Assert.Equal(0, comparison.AlphaChangedPixelCount);
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
