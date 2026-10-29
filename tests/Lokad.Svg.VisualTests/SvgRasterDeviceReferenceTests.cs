using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgRasterDeviceReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "capture.json")));
        foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public void Page_and_pattern_devices_retain_their_declared_browser_images(string id)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "capture.json")));
        Assert.Equal("recipe3-page-img-screenshot", data.RootElement.GetProperty("captureProfile").GetString());
        Assert.Equal(3, data.RootElement.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", data.RootElement.GetProperty("captureStartupHealth").GetString());
        Assert.True(data.RootElement.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        JsonElement image = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(), image.GetProperty("path").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        Assert.All(image.GetProperty("captureSha256").EnumerateArray(), value => Assert.Equal(image.GetProperty("sha256").GetString(), value.GetString()));
        RgbaImage expected = PngRgbaDecoder.Decode(reference, 1_048_576);
        if (image.TryGetProperty("canvas", out JsonElement canvas))
        {
            byte[] probe = File.ReadAllBytes(Path.Combine(Folder(), canvas.GetProperty("path").GetString() ?? throw new InvalidDataException()));
            Assert.Equal(canvas.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(probe)));
            PixelComparison profiles = PixelComparer.Compare(expected, PngRgbaDecoder.Decode(probe, 1_048_576));
            Assert.Equal(canvas.GetProperty("changedPixels").GetInt32(), profiles.ChangedPixelCount);
            Assert.Equal(canvas.GetProperty("maximumChannelError").GetInt32(), profiles.MaximumChannelError);
            Assert.Equal(canvas.GetProperty("alphaChangedPixels").GetInt32(), profiles.AlphaChangedPixelCount);
        }
        byte[] actual = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(expected, PngRgbaDecoder.Decode(actual, 1_048_576)).IsExact, id);
    }

    private static string Folder() => Path.Combine(ResearchCorpus.Root, "raster-capture-profiles");
}
