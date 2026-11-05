using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgRoundedRectReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["rounded-source-decimal"];
        yield return ["rounded-source-oval"];
        yield return ["rounded-source-half-width"];
        yield return ["rounded-source-half-height"];
        yield return ["rounded-source-clamped-width"];
        yield return ["rounded-source-reflection"];
        yield return ["rounded-source-fractional-stroke"];
        yield return ["rounded-source-fill-and-stroke"];
    }

    public static IEnumerable<object[]> OutputCases() => Cases().Where(static row => !IsExtendedOutput((string)row[0]));
    public static IEnumerable<object[]> ExtendedOutputCases() => Cases().Where(static row => IsExtendedOutput((string)row[0]));

    // New inputs remain routine; only these reviewed API repetitions are Research.
    private static bool IsExtendedOutput(string identity)
        => identity is "rounded-source-oval"
            or "rounded-source-half-width"
            or "rounded-source-half-height"
            or "rounded-source-clamped-width"
            or "rounded-source-reflection";

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public Task Rounded_source_geometry_matches_browser_pixels(string name) => VerifyCase(name, false);

    [Theory]
    [MemberData(nameof(OutputCases))]
    [Trait("Layer", "Contract")]
    public Task Rounded_source_geometry_matches_browser_pixels_through_all_six_outputs(string name) => VerifyCase(name, true);

    [Theory]
    [MemberData(nameof(ExtendedOutputCases))]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    public Task Extended_Rounded_source_geometry_matches_browser_pixels(string name) => VerifyCase(name, true);

    private static async Task VerifyCase(string name, bool allOutputs)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", name + ".svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-" + name + ".png")), 41 * 35);
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, 41 * 35)).IsExact);
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
