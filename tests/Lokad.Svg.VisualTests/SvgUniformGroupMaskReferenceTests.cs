using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgUniformGroupMaskReferenceTests
{
    [Theory]
    [InlineData("round-backward")]
    [InlineData("round-forward")]
    [InlineData("overlap-backward")]
    [InlineData("overlap-forward")]
    [InlineData("fractional-box")]
    [InlineData("round-stroke")]
    [InlineData("round-pattern")]
    [InlineData("round-mask-opacity")]
    [InlineData("round-alpha-mask")]
    [InlineData("round-gray-40")]
    [InlineData("round-gray-c0")]
    public async Task Composed_group_masks_match_browser_pixels_for_three_backgrounds_and_all_six_outputs(string name)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "mask-layer-" + name + ".svg"));
        foreach ((string background, SvgColor color) in new[] { ("transparent", SvgColor.Transparent), ("white", new SvgColor(255, 255, 255, 255)), ("black", new SvgColor(0, 0, 0, 255)) })
        {
            SvgRenderOptions options = new(null, 1, color, SvgRenderLimits.Default);
            RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-mask-layer-" + name + "-" + background + ".png")), 43 * 37);
            byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, 43 * 37)).IsExact);
            byte[] destination = new byte[expected.Length];
            Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
            Assert.Equal(expected, destination);
            using MemoryStream stream = new();
            Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
            Assert.Equal(expected, stream.ToArray());
            Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
            Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
            Assert.Equal(expected, destination);
            using MemoryStream asyncStream = new();
            Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
            Assert.Equal(expected, asyncStream.ToArray());
        }
    }
}
