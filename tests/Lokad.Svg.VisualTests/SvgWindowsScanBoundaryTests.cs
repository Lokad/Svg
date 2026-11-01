using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgWindowsScanBoundaryTests
{
    [Fact]
    [Trait("Layer", "Rendering")]
    public Task A_turning_counter_uses_prepared_scan_curves_without_changing_the_fitted_outline()
        => AssertNativeFontFixture("windows-scan-boundary", "original-scan-turn", "ScanTurn");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Conditional_pixel_delta_functions_preserve_native_recognition_and_ordinary_shift_controls()
        => AssertNativeFontFixture("windows-conditional-shift", "original-conditional-shift", "ConditionalShift");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Stack_projection_keeps_supplied_components_and_the_independent_freedom_direction()
        => AssertNativeFontFixture("windows-stack-vectors", "original-stack-vectors", "StackVectors");

    [Fact]
    [Trait("Layer", "Rendering")]
    public Task Windows_movement_keeps_copied_and_independently_loaded_directions_distinct()
        => AssertNativeFontFixture("windows-movement", "original-windows-movement", "WindowsMovement");

    private static async Task AssertNativeFontFixture(string bank, string packId, string family)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement recipe = capture.RootElement;
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(recipe, "input")));
        byte[] program = File.ReadAllBytes(Path.Combine(directory, Required(recipe, "font")));
        Assert.Equal(Required(recipe, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(Required(recipe, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(program)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), packId);
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new(family, 400, false)), new SvgMemoryFontProgramSource(bank, program));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
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
}
