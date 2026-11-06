using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgTextLayoutAdmissionReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("rtl", "logical")]
    [InlineData("rtl", "visual")]
    [InlineData("syllabic", "logical")]
    [InlineData("syllabic", "visual")]
    public async Task Covered_ascii_remains_exact_when_the_same_font_also_covers_unsupported_layout(string identity, string role)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "text-layout-admission");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray().Single(item => Required(item, "id") == identity);
        byte[] program = File.ReadAllBytes(Path.Combine(directory, Required(row, "font")));
        Assert.Equal(Required(row, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(program)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, Required(row, "fontPack"))), Required(row, "packId"));
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("BidiProbe", 400, false)), new SvgMemoryFontProgramSource(identity, program));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
        JsonElement control = row.GetProperty("roles").GetProperty(role);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(control, "input")));
        Assert.Equal(Required(control, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        JsonElement native = control.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(native, "reference")));
        string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
        Assert.Equal(Required(native, "referenceSha256"), digest);
        Assert.Equal(3, native.GetProperty("captures").GetArrayLength());
        foreach (JsonElement process in native.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(process, "pngSha256"));
            Assert.Equal(0, process.GetProperty("terminalExitCode").GetInt32());
            Assert.Equal("healthy", Required(process, "startupHealth"));
            Assert.True(process.GetProperty("profileCleaned").GetBoolean());
        }
        byte[] actual = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 128 * 72), PngRgbaDecoder.Decode(actual, 128 * 72));
        Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));

        static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }
}
