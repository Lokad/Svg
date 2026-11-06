using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgTextFeatureDispatchReferenceTests
{
    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("ccmp", "source")]
    [InlineData("ccmp", "visual")]
    [InlineData("context", "source")]
    [InlineData("context", "control")]
    [InlineData("normalization", "source")]
    [InlineData("normalization", "control")]
    [InlineData("normalization-fallback", "source")]
    [InlineData("normalization-fallback", "control")]
    public async Task Selected_features_and_canonical_coverage_match_each_native_role(string identity, string role)
    {
        string bank = Path.Combine(ResearchCorpus.Root, "text-feature-dispatch");
        string recipe = identity == "ccmp" ? Path.Combine(bank, "ccmp.json") : Path.Combine(bank, identity, "capture.json");
        string directory = Path.GetDirectoryName(recipe) ?? throw new InvalidDataException();
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(recipe));
        JsonElement row = capture.RootElement, entry = row.GetProperty("roles").GetProperty(role);
        if (entry.TryGetProperty("font", out _)) CheckProgram(entry);
        else if (row.TryGetProperty("font", out _)) CheckProgram(row);
        else foreach (JsonElement program in row.GetProperty("programs").EnumerateArray()) CheckProgram(program);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(entry, "input")));
        Assert.Equal(Required(entry, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        string manifest = Path.Combine(directory, entry.TryGetProperty("fontPack", out _) ? Required(entry, "fontPack") : Required(row, "fontPack"));
        using JsonDocument declaration = JsonDocument.Parse(File.ReadAllBytes(manifest));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(manifest), Required(declaration.RootElement, "packId"));
        string fontRoot = row.TryGetProperty("fontRoot", out _) ? Path.Combine(directory, Required(row, "fontRoot")) : directory;
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, catalog, new ProgramProvider(fontRoot), CancellationToken.None);
        byte[] actual = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts), CancellationToken.None).ToArray();
        JsonElement native = entry.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
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
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 128 * 72), PngRgbaDecoder.Decode(actual, 128 * 72));
        Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));

        void CheckProgram(JsonElement item)
            => Assert.Equal(Required(item, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, Required(item, "font"))))));
        static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }

    /// <summary>Serialized fixture reader; lends immutable program memory to the snapshot and retains no cache or disposable owner.</summary>
    private sealed class ProgramProvider(string directory) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, File.ReadAllBytes(Path.Combine(directory, file.RelativePath))));
        }
    }
}
