using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgNestedPatternReferenceTests
{
    [Fact]
    public void Nested_tiles_keep_template_aliases_shared_content_and_polygon_sampling()
        => SvgNativeControlAssertions.MatchesCanonicalConversion("nested-patterns", "nested-grid", 2);

    [Fact]
    public async Task Nested_tile_glyphs_keep_direct_and_path_text_pattern_paints()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "kerning-precedence");
        byte[] program = File.ReadAllBytes(Path.Combine(directory, "fonts", "PlainKern.ttf"));
        Assert.Equal("0526c14b283c2bc05eecdc9677615a32f12d27c881fbc1ac52759716828076a7", Convert.ToHexStringLower(SHA256.HashData(program)));
        string bank = Path.Combine(ResearchCorpus.Root, "nested-patterns");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(bank, "capture.json")));
        JsonElement row = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "nested-glyphs");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(program)), row.GetProperty("fontProgram").GetProperty("sha256").GetString());
        string browser = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(bank, "inputs", "nested-glyphs.svg")))
            .Replace("</defs>", "<style>@font-face{font-family:PlainKern;src:url(data:font/ttf;base64," + Convert.ToBase64String(program) + ")}</style></defs>", StringComparison.Ordinal);
        Assert.Equal(row.GetProperty("browserSourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(browser))));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), "public-kerning-precedence");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("PlainKern", 400, false)), new SvgMemoryFontProgramSource("nested-glyphs", program));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
        SvgNativeControlAssertions.MatchesCanonicalConversion("nested-patterns", "nested-glyphs", 2,
            new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts));
    }
}
