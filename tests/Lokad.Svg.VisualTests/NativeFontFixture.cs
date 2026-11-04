using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

internal static class NativeFontFixture
{
    internal static async Task AssertMatches(string bank, string packId, string family)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement recipe = capture.RootElement;
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(recipe, "input")));
        Assert.Equal(Required(recipe, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        using SvgFontSnapshot fonts = await CreateFontsAsync(bank, recipe, packId, new(family, 400, false));
        byte[] actual = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        JsonElement platform = recipe.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(platform, "reference")));
        string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
        Assert.Equal(Required(platform, "referenceSha256"), digest);
        Assert.Equal(3, platform.GetProperty("captures").GetArrayLength());
        foreach (JsonElement process in platform.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(process, "pngSha256"));
            Assert.Equal("healthy", Required(process, "startupHealth"));
            Assert.True(process.GetProperty("profileCleaned").GetBoolean());
        }
        int pixels = checked(recipe.GetProperty("width").GetInt32() * recipe.GetProperty("height").GetInt32());
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));

        static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }

    internal static async Task AssertCaseMatches(string bank, string identity, int expectedBankCases, SvgFontFaceRequest request)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        using SvgFontSnapshot fonts = await CreateFontsAsync(bank, capture.RootElement, bank, request);
        SvgNativeControlAssertions.MatchesCanonicalConversion(bank, identity, expectedBankCases, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts));
    }

    /// <summary>Validates the committed program and returns the snapshot each test must dispose.</summary>
    private static ValueTask<SvgFontSnapshot> CreateFontsAsync(string bank, JsonElement recipe, string packId, SvgFontFaceRequest request)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        byte[] program = File.ReadAllBytes(Path.Combine(directory, Required("font")));
        Assert.Equal(Required("fontSha256"), Convert.ToHexStringLower(SHA256.HashData(program)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), packId);
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(request), new SvgMemoryFontProgramSource(bank, program));
        return SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);

        string Required(string name) => recipe.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }

}
