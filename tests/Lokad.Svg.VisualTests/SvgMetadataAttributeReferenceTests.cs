using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgMetadataAttributeReferenceTests
{
    [Fact]
    public void Descriptive_titles_preserve_local_sources_and_paint_through_canonical_conversion()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "metadata-attributes");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement source = Assert.Single(manifest.RootElement.GetProperty("cases").EnumerateArray());
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(source, "input")));
        Assert.Equal(Required(source, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        JsonElement platform = source.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        string digest = Required(platform, "referenceSha256");
        foreach (JsonElement capture in platform.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(capture, "pngSha256"));
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        }
        Assert.Equal(3, platform.GetProperty("captures").GetArrayLength());
        RgbaImage expected = new NativeReferenceImages().Get(Path.Combine(directory, Required(platform, "reference")), digest);
        byte[] png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        PixelComparison result = PixelComparer.Compare(expected, PngRgbaDecoder.Decode(png, 128 * 96));
        Assert.True(result.IsExact, JsonSerializer.Serialize(result));

        static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }
}
