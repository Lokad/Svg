using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Category", "Research")]
public sealed partial class SvgShaderOriginalReferenceTests
{
    private static readonly PaintReferenceBank Bank = new("shader-originals");

    public static IEnumerable<object[]> Cases() => Bank.CaseIds.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public async Task Original_shader_and_alpha_screen_preserves_each_native_target(string id)
    {
        JsonElement row = Bank.GetCase(id);
        byte[] input = CreateSource(row);
        Assert.Equal(Required(row, "sourceSha256"), Hash(input));
        using SvgFontSnapshot? fonts = row.GetProperty("font").GetBoolean()
            ? await SvgFontSnapshot.CreateAsync(input, NotoPaintPrograms.Catalog, new NotoPaintPrograms(), CancellationToken.None) : null;
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RgbaImage image = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(), 1024);
        JsonElement expected = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        Assert.Equal(expected.GetProperty("width").GetInt32(), image.Width);
        Assert.Equal(expected.GetProperty("height").GetInt32(), image.Height);
        // Match PixelComparer: RGB beneath zero alpha is unspecified.
        for (int offset = 0; offset < image.Pixels.Length; offset += 4)
            if (image.Pixels[offset + 3] == 0) image.Pixels.AsSpan(offset, 3).Clear();
        Assert.Equal(Required(expected, "rgbaSha256"), Hash(image.Pixels));
    }

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Original_shader_and_alpha_screen_retains_sources_fonts_and_healthy_native_triples()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "shader-originals/capture.json")));
        JsonElement native = capture.RootElement.GetProperty("nativeCapture"), font = native.GetProperty("font");
        byte[] program = File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "shader-originals", Required(font, "path")));
        Assert.Equal(Required(font, "sha256"), Hash(program));
        string css = "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(program) + ")}</style>";
        Assert.Equal(1134, Bank.CaseIds.Count);
        foreach (string id in Bank.CaseIds)
        {
            JsonElement row = Bank.GetCase(id);
            byte[] input = CreateSource(row);
            Assert.Equal(Required(row, "sourceSha256"), Hash(input));
            string source = System.Text.Encoding.UTF8.GetString(input);
            if (row.GetProperty("font").GetBoolean())
            {
                source = source.Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
                source = source.Insert(source.IndexOf('>') + 1, css);
            }
            Assert.Equal(Required(row, "nativeSourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(source)));
            Assert.Equal(1, row.GetProperty("outputScale").GetDouble());
        }
        foreach (string host in new[] { "windows", "linux" })
        {
            string joined = string.Join('\n', Bank.CaseIds.Select(id =>
            {
                JsonElement row = Bank.GetCase(id), reference = row.GetProperty("references").GetProperty(host);
                Assert.Equal(32, reference.GetProperty("width").GetInt32());
                Assert.Equal(32, reference.GetProperty("height").GetInt32());
                return id + ":" + Required(row, "nativeSourceSha256") + ":" + Required(reference, "pngSha256") + ":" + Required(reference, "rgbaSha256") + ":32:32";
            }));
            string digest = Hash(System.Text.Encoding.UTF8.GetBytes(joined));
            JsonElement launches = native.GetProperty("hosts").GetProperty(host).GetProperty("launches");
            Assert.Equal(3, launches.GetArrayLength());
            foreach (JsonElement launch in launches.EnumerateArray())
            {
                Assert.Equal(0, launch.GetProperty("terminalExitCode").GetInt32());
                Assert.Equal(1134, launch.GetProperty("cases").GetInt32());
                Assert.Equal(digest, Required(launch, "caseDigestSha256"));
                Assert.Equal("healthy", Required(launch, "startupHealth"));
                Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                Assert.False(launch.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, launch.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, launch.GetProperty("networkServiceCrashes").GetInt32());
            }
        }
    }

    private static string Required(JsonElement row, string field)
        => row.GetProperty(field).GetString() ?? throw new InvalidDataException(field);

    private static string Hash(byte[] body) => Convert.ToHexStringLower(SHA256.HashData(body));
}
