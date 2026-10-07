using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgMaskCommandRangeReferenceTests
{
    [Theory]
    [InlineData("adjacent-values-backward")]
    [InlineData("adjacent-values-forward")]
    [InlineData("adjacent-empty")]
    public async Task Adjacent_mask_scopes_match_browser_pixels_for_three_backgrounds_and_all_six_outputs(string name)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "mask-range-" + name + ".svg"));
        foreach ((string background, SvgColor color) in new[] { ("transparent", SvgColor.Transparent), ("white", new SvgColor(255, 255, 255, 255)), ("black", new SvgColor(0, 0, 0, 255)) })
        {
            SvgRenderOptions options = new(null, 1, color, SvgRenderLimits.Default);
            RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-mask-range-" + name + "-" + background + ".png")), 47 * 41);
            byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, 47 * 41)).IsExact);
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
