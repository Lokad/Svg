using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgTextPositionLengthReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task Declaration_font_sizes_and_position_lists_match_independent_pixels(string name)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "text-position-lengths");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement capture = document.RootElement;
        byte[] input = File.ReadAllBytes(Path.Combine(directory, name + ".svg"));
        byte[] program = File.ReadAllBytes(Path.Combine(directory, capture.GetProperty("font").GetString() ?? throw new InvalidDataException("font")));
        Assert.Equal(capture.GetProperty("inputFiles").GetProperty(name + ".svg").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(capture.GetProperty("fontSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(program)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), "text-position-lengths");
        SvgFontProgramResolution resolution = catalog.Bind(catalog.Resolve(new("DiagnosticNoto", 400, false)), new SvgMemoryFontProgramSource("text-position-lengths", program));
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([resolution], CancellationToken.None);
        byte[] actual = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        JsonElement row = capture.GetProperty("rows").EnumerateArray().Single(item => item.GetProperty("host").GetString() == (OperatingSystem.IsWindows() ? "windows" : "linux"));
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, row.GetProperty("reference").GetString() ?? throw new InvalidDataException("reference")));
        string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
        foreach (string property in new[] { "captures", "relativeNativeCaptures" })
        {
            JsonElement processes = row.GetProperty(property);
            Assert.Equal(3, processes.GetArrayLength());
            foreach (JsonElement process in processes.EnumerateArray())
            {
                Assert.Equal(digest, process.GetProperty("pngSha256").GetString());
                Assert.Equal("healthy", process.GetProperty("startupHealth").GetString());
                Assert.True(process.GetProperty("profileCleaned").GetBoolean());
                Assert.Equal(0, process.GetProperty("terminalExitCode").GetInt32());
            }
        }
        int pixels = checked(row.GetProperty("width").GetInt32() * row.GetProperty("height").GetInt32());
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
    }
}
