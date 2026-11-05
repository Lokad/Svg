using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgUniformGroupMaskReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["round-backward"];
        yield return ["round-forward"];
        yield return ["overlap-backward"];
        yield return ["overlap-forward"];
        yield return ["fractional-box"];
        yield return ["round-stroke"];
        yield return ["round-pattern"];
        yield return ["round-mask-opacity"];
        yield return ["round-alpha-mask"];
        yield return ["round-gray-40"];
        yield return ["round-gray-c0"];
    }

    public static IEnumerable<object[]> OutputCases() => Cases().Where(static row => !IsExtendedOutput((string)row[0]));
    public static IEnumerable<object[]> ExtendedOutputCases() => Cases().Where(static row => IsExtendedOutput((string)row[0]));

    // New inputs remain routine; only these reviewed API repetitions are Research.
    private static bool IsExtendedOutput(string identity)
        => identity is "round-backward"
            or "round-forward"
            or "overlap-backward"
            or "overlap-forward"
            or "round-stroke"
            or "round-gray-40"
            or "round-gray-c0";

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public Task Composed_group_masks_match_browser_pixels_for_three_backgrounds(string name) => VerifyCase(name, false);

    [Theory]
    [MemberData(nameof(OutputCases))]
    [Trait("Layer", "Contract")]
    public Task Composed_group_masks_match_browser_pixels_for_three_backgrounds_and_all_six_outputs(string name) => VerifyCase(name, true);

    [Theory]
    [MemberData(nameof(ExtendedOutputCases))]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    public Task Extended_Composed_group_masks_match_browser_pixels_for_three_backgrounds(string name) => VerifyCase(name, true);

    private static async Task VerifyCase(string name, bool allOutputs)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "mask-layer-" + name + ".svg"));
        foreach ((string background, SvgColor color) in new[] { ("transparent", SvgColor.Transparent), ("white", new SvgColor(255, 255, 255, 255)), ("black", new SvgColor(0, 0, 0, 255)) })
        {
            SvgRenderOptions options = new(null, 1, color, SvgRenderLimits.Default);
            RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-mask-layer-" + name + "-" + background + ".png")), 43 * 37);
            byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, 43 * 37)).IsExact);
            if (!allOutputs) continue;
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
