using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSingleStopGradientReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["one-stop-alpha-direct", 0, 0];
        yield return ["radial-one-stop-alpha-direct", 0, 0];
        yield return ["one-stop-alpha-leaf", 0, 0];
        yield return ["one-stop-alpha-parent", 0, 0];
        yield return ["override-one-stop-alpha-leaf", 0, 0];
    }

    public static IEnumerable<object[]> OutputCases() => Cases().Where(static row => !IsExtendedOutput((string)row[0]));
    public static IEnumerable<object[]> ExtendedOutputCases() => Cases().Where(static row => IsExtendedOutput((string)row[0]));

    // New inputs remain routine; only these reviewed API repetitions are Research.
    private static bool IsExtendedOutput(string identity)
        => identity is "one-stop-alpha-leaf"
            or "one-stop-alpha-parent";

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public Task Single_stop_gradients_preserve_browser_comparisons(string name, int pixels, int channel) => VerifyCase(name, pixels, channel, false);

    [Theory]
    [MemberData(nameof(OutputCases))]
    [Trait("Layer", "Contract")]
    public Task Single_stop_gradients_preserve_browser_comparisons_through_all_six_outputs(string name, int pixels, int channel) => VerifyCase(name, pixels, channel, true);

    [Theory]
    [MemberData(nameof(ExtendedOutputCases))]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    public Task Extended_Single_stop_gradients_preserve_browser_comparisons(string name, int pixels, int channel) => VerifyCase(name, pixels, channel, true);

    private static async Task VerifyCase(string name, int pixels, int channel, bool allOutputs)
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
        if (!allOutputs) return;
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
