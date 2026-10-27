using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgFilterStageReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string family in new[] { "filter-clip-ownership", "filter-restoration", "filter-affine-restoration", "filter-shadow-stages", "filter-alpha-stages", "filter-image-placement", "filter-stored-images", "filter-stage-boundaries", "filter-opacity-sampling" })
        {
            using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(family), "capture.json")));
            foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
                yield return [family, row.GetProperty("id").GetString() ?? throw new InvalidDataException()];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public void Filter_stages_retain_their_independent_browser_images(string family, string id)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(family), "capture.json")));
        Assert.Equal("recipe3-page-img-screenshot", data.RootElement.GetProperty("captureProfile").GetString());
        Assert.Equal(3, data.RootElement.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", data.RootElement.GetProperty("captureStartupHealth").GetString());
        Assert.True(data.RootElement.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        JsonElement image = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(family), row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(family), image.GetProperty("path").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        Assert.All(image.GetProperty("captureSha256").EnumerateArray(), value => Assert.Equal(image.GetProperty("sha256").GetString(), value.GetString()));
        RgbaImage expected = PngRgbaDecoder.Decode(reference, 1_048_576);
        if (image.TryGetProperty("independentControl", out JsonElement control))
        {
            byte[] independent = File.ReadAllBytes(Path.Combine(Folder(family), control.GetProperty("path").GetString() ?? throw new InvalidDataException()));
            Assert.Equal(control.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(independent)));
            PixelComparison contrast = PixelComparer.Compare(expected, PngRgbaDecoder.Decode(independent, 1_048_576));
            Assert.Equal(control.GetProperty("changedPixels").GetInt32(), contrast.ChangedPixelCount);
            Assert.Equal(control.GetProperty("alphaChangedPixels").GetInt32(), contrast.AlphaChangedPixelCount);
        }
        double scale = row.TryGetProperty("scale", out JsonElement outputScale) ? outputScale.GetDouble() : 1;
        SvgColor background = row.TryGetProperty("whiteBackground", out JsonElement white) && white.GetBoolean()
            ? new(255, 255, 255, 255) : SvgColor.Transparent;
        SvgRenderOptions options = new(null, scale, background, SvgRenderLimits.Default);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(expected, PngRgbaDecoder.Decode(actual, 1_048_576)).IsExact, id);
    }

    private static string Folder(string family) => Path.Combine(AppContext.BaseDirectory, "Research", family);
}
