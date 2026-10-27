using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgFilterSourceCoverageReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "images.json")));
        Assert.Equal("canvas-svg-image", data.RootElement.GetProperty("captureProfile").GetString());
        foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public void Source_aliases_materialization_and_coverage_match_their_own_browser_images(string id)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "images.json")));
        Assert.Equal(3, data.RootElement.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", data.RootElement.GetProperty("captureStartupHealth").GetString());
        Assert.True(data.RootElement.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        JsonElement image = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(), image.GetProperty("path").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        using JsonDocument declared = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "declared-profile.json")));
        Assert.Equal("recipe3-page-img-screenshot", declared.RootElement.GetProperty("captureProfile").GetString());
        Assert.Equal(3, declared.RootElement.GetProperty("freshProcessCaptures").GetInt32());
        JsonElement qualified = declared.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), qualified.GetProperty("conversionSourceSha256").GetString());
        byte[] captureInput = File.ReadAllBytes(Path.Combine(Folder(), qualified.GetProperty("captureInput").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(qualified.GetProperty("captureInputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(captureInput)));
        JsonElement primary = qualified.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] primaryBytes = File.ReadAllBytes(Path.Combine(Folder(), primary.GetProperty("path").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(primary.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(primaryBytes)));
        Assert.All(primary.GetProperty("captureSha256").EnumerateArray(), value => Assert.Equal(primary.GetProperty("sha256").GetString(), value.GetString()));
        RgbaImage primaryImage = PngRgbaDecoder.Decode(primaryBytes, 1_048_576);
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), primaryImage).IsExact, id + " capture profiles");
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RgbaImage expected = PngRgbaDecoder.Decode(reference, 1_048_576);
        Assert.Equal(image.GetProperty("width").GetInt32(), expected.Width);
        Assert.Equal(image.GetProperty("height").GetInt32(), expected.Height);
        Assert.True(PixelComparer.Compare(primaryImage, PngRgbaDecoder.Decode(actual, 1_048_576)).IsExact, id);
    }

    private static string Folder() => Path.Combine(AppContext.BaseDirectory, "Research", "filter-source-coverage");
}
