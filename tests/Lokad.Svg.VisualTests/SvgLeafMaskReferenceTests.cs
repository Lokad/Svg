using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgLeafMaskReferenceTests
{
    [Theory]
    [InlineData("mask-leaf-leaf-round-backward.svg", "original-mask-leaf-leaf-round-backward", 47, 41)]
    [InlineData("mask-leaf-adjacent-groups-backward.svg", "original-mask-leaf-adjacent-groups-backward", 47, 41)]
    [InlineData("mask-leaf-leaf-round-forward.svg", "original-mask-leaf-leaf-round-forward", 47, 41)]
    [InlineData("mask-leaf-adjacent-groups-forward.svg", "original-mask-leaf-adjacent-groups-forward", 47, 41)]
    [InlineData("mask-leaf-leaf-fill-stroke.svg", "original-mask-leaf-leaf-fill-stroke", 47, 41)]
    [InlineData("mask-leaf-leaf-path-fill-stroke.svg", "original-mask-leaf-leaf-path-fill-stroke", 47, 41)]
    [InlineData("mask-leaf-leaf-pattern.svg", "original-mask-leaf-leaf-pattern", 47, 41)]
    [InlineData("mask-leaf-leaf-inside-opacity.svg", "original-mask-leaf-leaf-inside-opacity", 47, 41)]
    [InlineData("mask-leaf-adjacent-leaves.svg", "original-mask-leaf-adjacent-leaves", 47, 41)]
    [InlineData("mask-leaf-use-mask.svg", "original-mask-leaf-use-mask", 47, 41)]
    [InlineData("mask-leaf-group-use-masked-leaf.svg", "original-mask-leaf-group-use-masked-leaf", 47, 41)]
    [InlineData("mask-leaf-leaf-integer-region.svg", "original-mask-leaf-leaf-integer-region", 47, 41)]
    [InlineData("mask-leaf-forward-group-copy.svg", "original-mask-leaf-forward-group-copy", 49, 43)]
    [InlineData("mask-leaf-nested-masked-copy.svg", "original-mask-leaf-nested-masked-copy", 49, 43)]
    [InlineData("mask-leaf-ancestor-unbake-copy.svg", "original-mask-leaf-ancestor-unbake-copy", 49, 43)]
    [InlineData("mask-leaf-custom-region-copy.svg", "original-mask-leaf-custom-region-copy", 49, 43)]
    [InlineData("mask-leaf-forward-clip-leaf.svg", "original-mask-leaf-forward-clip-leaf", 49, 43)]
    [InlineData("mask-leaf-bounding-box-use-union.svg", "original-mask-leaf-bounding-box-use-union", 49, 43)]
    [InlineData("mask-leaf-root-opacity-leaf.svg", "original-mask-leaf-root-opacity-leaf", 49, 43)]
    [InlineData("mask-leaf-empty-leaf-adjacent.svg", "original-mask-leaf-empty-leaf-adjacent", 49, 43)]
    [InlineData("mask-leaf-forward-use-stroke.svg", "original-mask-leaf-forward-use-stroke", 49, 43)]
    [InlineData("mask-leaf-single-leaf-symbol-stroke.svg", "original-mask-leaf-single-leaf-symbol-stroke", 49, 43)]
    [InlineData("research-mask-layer-round-leaf.svg", "original-research-mask-layer-round-leaf", 43, 37)]
    public void Leaf_and_instance_masks_match_browser_pixels_for_three_backgrounds(string filename, string identifier, int width, int height)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", filename));
        foreach ((string background, SvgColor color) in new[] { ("transparent", SvgColor.Transparent), ("white", new SvgColor(255, 255, 255, 255)), ("black", new SvgColor(0, 0, 0, 255)) })
        {
            SvgRenderOptions options = new(null, 1, color, SvgRenderLimits.Default);
            RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", identifier + "-" + background + ".png")), checked(width * height));
            byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, checked(width * height))).IsExact);
        }
    }
}
