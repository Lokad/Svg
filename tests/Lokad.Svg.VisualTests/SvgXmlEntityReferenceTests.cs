using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgXmlEntityReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("literal-markup")]
    [InlineData("literal-resources")]
    [InlineData("encoded-resources")]
    public void Internal_general_entities_preserve_native_markup_and_resource_output(string name)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "xml-entities");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        string host = OperatingSystem.IsWindows() ? "windows" : "linux";
        JsonElement row = document.RootElement.GetProperty("rows").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name + ".svg");
        Assert.True(row.GetProperty("routineRenderingReference").GetBoolean());
        JsonElement platform = row.GetProperty("hosts").EnumerateArray()
            .Single(item => item.GetProperty("host").GetString() == host);
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".svg"));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(source)));
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, host, name + ".png"));
        string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
        JsonElement captures = platform.GetProperty("captures");
        Assert.Equal(3, captures.GetArrayLength());
        foreach (JsonElement capture in captures.EnumerateArray())
        {
            Assert.Equal(digest, capture.GetProperty("pngSha256").GetString());
            Assert.Equal("healthy", capture.GetProperty("startupHealth").GetString());
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        }
        byte[] actual = SvgConverter.ConvertToPng(source, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        int pixels = checked(platform.GetProperty("width").GetInt32() * platform.GetProperty("height").GetInt32());
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
    }
}
