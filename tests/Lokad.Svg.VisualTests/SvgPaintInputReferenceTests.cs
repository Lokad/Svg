using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPaintInputReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string folder in new[] { "offset-merge-inputs", "linear-shape-source-maps", "radial-axis-source-maps", "gradient-spread-stages", "gradient-paint-replay" })
        {
            using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(folder), "capture.json")));
            foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
                yield return [folder, row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case id.")];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Paint_inputs_match_each_native_image(string folder, string id)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(folder), "capture.json")));
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        bool hasPlatformReferences = row.TryGetProperty("platforms", out JsonElement platforms);
        JsonElement expected = hasPlatformReferences && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            ? platforms.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux") : row;
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(folder), row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.")));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(folder), expected.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.")));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(expected.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        bool hasFonts = row.TryGetProperty("font", out JsonElement font) && font.GetBoolean();
        byte[] fontBytes = hasFonts ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research/supplied-font-labels/fonts/NotoSans-Regular.ttf")) : [];
        using SvgFontSnapshot? fonts = hasFonts
            ? await SvgFontSnapshot.CreateAsync(input,
                SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research/static-css-document/font-pack.json")), "public-noto-sans"),
                new FontProvider(fontBytes), CancellationToken.None)
            : null;
        if (hasFonts)
        {
            Assert.Equal(row.GetProperty("nativeFontSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(fontBytes)));
            string nativeInput = System.Text.Encoding.UTF8.GetString(input).Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
            string css = "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(fontBytes) + ")}</style>";
            nativeInput = nativeInput.Insert(nativeInput.IndexOf('>') + 1, css);
            Assert.Equal(row.GetProperty("nativeSourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nativeInput))));
        }
        SvgRenderOptions options = row.TryGetProperty("scale", out JsonElement scale)
            ? new(null, scale.GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts)
            : new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        if (!hasPlatformReferences || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 4096), PngRgbaDecoder.Decode(actual, 4096)).IsExact, id);
    }

    private static string Folder(string name) => Path.Combine(AppContext.BaseDirectory, "Research", name);

    private sealed class FontProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        /// <summary>Returns borrowed immutable OFL bytes, observes cancellation and permits concurrent requests.</summary>
        /// <remarks>The source owns no storage; the test retains bytes through snapshot disposal.</remarks>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}
