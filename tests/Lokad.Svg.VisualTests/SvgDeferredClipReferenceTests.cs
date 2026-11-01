using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgDeferredClipReferenceTests
{
    [Theory]
    [InlineData("multiple-uses")]
    [InlineData("mixed-child")]
    [InlineData("forward-uses")]
    [InlineData("object-box-use")]
    public void Ordered_direct_and_referenced_sources_match_independent_native_coverage(string identity)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "clip-definition-sources");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement row = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(item => Required(item, "id") == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(row, "input")));
        Assert.Equal(Required(row, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        JsonElement platform = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] expected = File.ReadAllBytes(Path.Combine(directory, Required(platform, "reference")));
        string digest = Convert.ToHexStringLower(SHA256.HashData(expected));
        Assert.Equal(Required(platform, "referenceSha256"), digest);
        Assert.Equal(3, platform.GetProperty("captures").GetArrayLength());
        foreach (JsonElement receipt in platform.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(receipt, "pngSha256"));
            Assert.Equal("healthy", Required(receipt, "startupHealth"));
            Assert.True(receipt.GetProperty("profileCleaned").GetBoolean());
        }
        byte[] actual = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        int pixels = checked(manifest.RootElement.GetProperty("width").GetInt32() * manifest.RootElement.GetProperty("height").GetInt32());
        PixelComparison result = PixelComparer.Compare(PngRgbaDecoder.Decode(expected, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(result.IsExact, JsonSerializer.Serialize(result));

        static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }
}
