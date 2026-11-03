using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgDashRasterWindowReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("narrow")]
    [InlineData("wide")]
    [InlineData("thin")]
    [InlineData("round")]
    [InlineData("position-phase")]
    [InlineData("shader-clip")]
    [InlineData("non-scaling")]
    public void Source_dashes_are_constructed_in_the_owning_bitmap_before_thin_or_outline_paint(string name)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "dash-image-width");
        bool original = name is "narrow" or "wide";
        string metadata = original ? "capture.json" : name is "thin" or "round" ? "stroke-routes.json" : "interactions.json";
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, metadata)));
        string host = OperatingSystem.IsWindows() ? "windows" : "linux";
        JsonElement row = original
            ? document.RootElement.GetProperty("rows").EnumerateArray().Single(item => item.GetProperty("host").GetString() == host)
                .GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("name").GetString() == name)
            : document.RootElement.GetProperty("rows").EnumerateArray().Single(item => item.GetProperty("host").GetString() == host
                && item.TryGetProperty("input", out JsonElement input) && input.GetString() == name + ".svg");
        byte[] source = File.ReadAllBytes(Path.Combine(directory, name + ".svg"));
        Assert.Equal(row.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(source)));
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, row.GetProperty("reference").GetString() ?? throw new InvalidDataException("reference")));
        string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
        JsonElement captures = row.GetProperty("captures");
        Assert.Equal(3, captures.GetArrayLength());
        foreach (JsonElement capture in captures.EnumerateArray())
        {
            Assert.Equal(digest, capture.GetProperty("pngSha256").GetString());
            Assert.Equal("healthy", capture.GetProperty("startupHealth").GetString());
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            Assert.Equal(0, capture.GetProperty("terminalExitCode").GetInt32());
        }
        byte[] actual = SvgConverter.ConvertToPng(source, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        int pixels = checked(row.GetProperty("width").GetInt32() * (original ? row.GetProperty("height").GetInt32() : 384));
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
    }
}
