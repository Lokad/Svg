using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

public sealed class SvgGlyphConicCenterReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("black")]
    [InlineData("blue")]
    public async Task Supplied_glyph_paint_preserves_native_conic_center_coverage(string identity)
    {
        const string bank = "glyph-conic-centers";
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement recipe = capture.RootElement;
        byte[] program = File.ReadAllBytes(Path.Combine(directory, recipe.GetProperty("font").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(recipe.GetProperty("fontSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(program)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), bank);
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("DiagnosticNoto", 400, false)), new SvgMemoryFontProgramSource(bank, program));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
        SvgNativeControlAssertions.MatchesCanonicalConversion(bank, identity, 2, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts));
    }
}
