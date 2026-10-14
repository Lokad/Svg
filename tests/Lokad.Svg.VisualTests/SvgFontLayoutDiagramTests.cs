using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Retains the complete declared layout guide and orthogonal missing-semantic witnesses.</summary>
public sealed class SvgFontLayoutDiagramTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "font-layout-diagram");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Declared_layout_outputs_preserve_original_sources_and_platform_references(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement root = manifest.RootElement;
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        byte[] unchanged = input.ToArray();
        Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal("CC0-1.0", Required(item, "license"));
        string referenceKey = OperatingSystem.IsLinux() ? "linuxReference" : "reference";
        byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, referenceKey)));
        Assert.Equal(Required(item, referenceKey + "Sha256"), Convert.ToHexStringLower(SHA256.HashData(reference)));
        JsonElement captures = item.GetProperty(OperatingSystem.IsLinux() ? "linuxCaptures" : "captures");
        Assert.Equal(3, captures.GetArrayLength());
        foreach (JsonElement capture in captures.EnumerateArray())
        {
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            Assert.Equal(Required(item, referenceKey + "Sha256"), Required(capture, "pngSha256"));
        }
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, Required(root, "fontPack"))), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog,
            [new("Example", 400, false), new("Mono", 700, false), new("Missing", 400, false)], new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(new SvgViewport(item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32()), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        if (item.TryGetProperty("expectedError", out JsonElement error))
            Assert.Equal(Enum.Parse<SvgErrorCode>(error.GetString() ?? throw new InvalidDataException()),
                Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
        else
        {
            RgbaImage expected = PngRgbaDecoder.Decode(reference, SvgRenderLimits.Default.MaximumPixels);
            RgbaImage actual = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(), SvgRenderLimits.Default.MaximumPixels);
            Assert.True(PixelComparer.Compare(expected, actual).IsExact);
        }
        Assert.Equal(unchanged, input);
    }

    private static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    /// <summary>Supplies immutable existing public programs under caller ownership, without host discovery.</summary>
    /// <remarks>Cancellation is observed before I/O; independent acquisitions have no shared mutable state.</remarks>
    private sealed class Provider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = file.RelativePath switch
            {
                "fonts/NotoSans-Regular.ttf" => "../supplied-font-labels/fonts/NotoSans-Regular.ttf",
                "fonts/NotoSansMono-Bold.ttf" => "../supplied-font-documents/fonts/NotoSansMono-Bold.ttf",
                _ => throw new InvalidDataException("Unexpected supplied program."),
            };
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-layout", File.ReadAllBytes(Path.Combine(DirectoryPath, path))));
        }
    }
}
