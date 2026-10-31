using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgKerningPrecedenceTests
{
    public static IEnumerable<object[]> RecordingCases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "svgweb/font-stages/capture.json")));
        foreach (JsonElement row in manifest.RootElement.GetProperty("cases").EnumerateArray()) yield return [Required(row, "id")];
    }

    [Theory]
    [MemberData(nameof(RecordingCases))]
    [Trait("Layer", "Rendering")]
    public async Task Recorded_fill_and_distinct_stroke_path_consumers_match_the_native_image(string id)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "svgweb/font-stages");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement recipe = manifest.RootElement;
        JsonElement row = recipe.GetProperty("cases").EnumerateArray().Single(row => Required(row, "id") == id);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(row, "input")));
        Assert.Equal(Required(row, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        JsonElement font = recipe.GetProperty("font");
        Assert.Equal(Required(font, "sha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, Required(font, "program"))))));
        string catalogPath = Path.GetFullPath(Path.Combine(directory, Required(font, "catalog")));
        string fontRoot = Path.GetDirectoryName(catalogPath) ?? throw new InvalidDataException("Missing font directory.");
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(catalogPath), Required(font, "packId"));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, new FontProvider(fontRoot), CancellationToken.None);
        byte[] actual = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        JsonElement platform = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(platform, "reference")));
        string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
        Assert.Equal(Required(platform, "referenceSha256"), digest);
        Assert.Equal(3, platform.GetProperty("captures").GetArrayLength());
        foreach (JsonElement capture in platform.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(capture, "pngSha256"));
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        }
        int pixels = checked(recipe.GetProperty("width").GetInt32() * recipe.GetProperty("height").GetInt32());
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, id + ": " + JsonSerializer.Serialize(comparison));
    }
}
