using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgTextInstanceCompositionReferenceTests
{
    public static IEnumerable<object[]> RenderingCases() => Cases(false);
    public static IEnumerable<object[]> ExclusionCases() => Cases(true);

    private static IEnumerable<object[]> Cases(bool excluded)
    {
        foreach (string collection in new[] { "text-instance-composition", "fractional-text-strokes" })
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(collection), "capture.json")));
            foreach (JsonElement row in manifest.RootElement.GetProperty("cases").EnumerateArray())
                if (row.GetProperty("platforms").GetProperty(Host()).TryGetProperty("expectedError", out _) == excluded)
                    yield return [collection, Required(row, "id")];
        }
    }

    [Theory]
    [MemberData(nameof(RenderingCases))]
    [Trait("Layer", "Rendering")]
    public async Task Each_actual_keeps_its_independent_native_reference_and_capture_context(string collection, string id)
    {
        JsonElement row = ReadCase(collection, id);
        JsonElement platform = row.GetProperty("platforms").GetProperty(Host());
        byte[] input = ReadVerified(collection, Required(row, "input"), Required(row, "sourceSha256"));
        byte[] reference = ReadVerified(collection, Required(platform, "reference"), Required(platform, "referenceSha256"));
        Assert.Equal(3, platform.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", Required(platform, "startupHealth"));
        Assert.True(platform.GetProperty("profilesCleaned").GetBoolean());
        int pixels = checked(platform.GetProperty("width").GetInt32() * platform.GetProperty("height").GetInt32());
        RgbaImage native = PngRgbaDecoder.Decode(reference, pixels);
        RgbaImage actual = PngRgbaDecoder.Decode(await Render(row, input), pixels);
        PixelComparison comparison = PixelComparer.Compare(native, actual);
        PixelComparison recorded = JsonSerializer.Deserialize<PixelComparison>(platform.GetProperty("comparison"));
        Assert.Equal(recorded, comparison);
        Assert.True(comparison.IsExact);
    }

    [Theory]
    [MemberData(nameof(ExclusionCases))]
    [Trait("Layer", "Contract")]
    public async Task Retained_nested_svg_exclusion_stays_explicit_against_a_painted_native_control(string collection, string id)
    {
        JsonElement row = ReadCase(collection, id);
        JsonElement platform = row.GetProperty("platforms").GetProperty(Host());
        byte[] input = ReadVerified(collection, Required(row, "input"), Required(row, "sourceSha256"));
        byte[] reference = ReadVerified(collection, Required(platform, "reference"), Required(platform, "referenceSha256"));
        RgbaImage native = PngRgbaDecoder.Decode(reference, checked(platform.GetProperty("width").GetInt32() * platform.GetProperty("height").GetInt32()));
        Assert.Contains(Enumerable.Range(0, native.Pixels.Length / 4), pixel => native.Pixels[pixel * 4 + 3] != 0);
        Assert.Equal("R74/C04", Required(platform, "remainingFinding"));
        Assert.Equal("UnsupportedFeature", Required(platform, "expectedError"));
        SvgException error = await Assert.ThrowsAsync<SvgException>(async () => { await Render(row, input); });
        Assert.Equal(SvgErrorCode.UnsupportedFeature, error.ErrorCode);
    }

    private static async Task<byte[]> Render(JsonElement row, byte[] input)
    {
        using SvgFontSnapshot? fonts = row.GetProperty("font").GetBoolean()
            ? await SvgFontSnapshot.CreateAsync(input, SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "static-css-document", "font-pack.json")), "public-noto-sans"), new FontPrograms(), CancellationToken.None)
            : null;
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        return SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
    }

    private static JsonElement ReadCase(string collection, string id)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(collection), "capture.json")));
        return manifest.RootElement.GetProperty("cases").EnumerateArray().Single(row => Required(row, "id") == id).Clone();
    }

    private static byte[] ReadVerified(string collection, string relative, string hash)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Folder(collection), relative));
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static string Required(JsonElement row, string name)
        => row.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name + ".");

    private static string Folder(string collection) => Path.Combine(AppContext.BaseDirectory, "Research", collection);
    private static string Host() => OperatingSystem.IsWindows() ? "windows" : "linux";

    private sealed class FontPrograms : ISvgFontProgramSourceProvider
    {
        private static readonly byte[] Bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels", "fonts", "NotoSans-Regular.ttf"));

        /// <summary>Transfers a fresh source to acquisition over immutable shared bytes; honors cancellation and permits concurrent independent calls.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("fonts/NotoSans-Regular.ttf", file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("instance-composition-noto", Bytes));
        }
    }
}
