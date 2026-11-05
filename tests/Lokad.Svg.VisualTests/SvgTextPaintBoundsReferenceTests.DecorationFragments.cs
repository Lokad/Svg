using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgTextPaintBoundsReferenceTests
{
    public static IEnumerable<object[]> DecorationFragmentCases() => CaptureCases("decoration-fragment-images.json");

    [Theory]
    [MemberData(nameof(DecorationFragmentCases))]
    [Trait("Layer", "Rendering")]
    public Task Decoration_fragments_preserve_each_original_mixed_paint_target(string id)
        => AssertNativeImage("decoration-fragment-images.json", id);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Decoration_fragments_retain_original_sources_font_binding_and_healthy_native_triples()
        => AssertDecorationCapture("decoration-fragment-images.json", 22, 122);

    private static void AssertDecorationCapture(string file, int expectedCases, int capturedCases)
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), file)));
        JsonElement native = capture.RootElement.GetProperty("nativeCapture");
        JsonElement font = native.GetProperty("font");
        byte[] program = File.ReadAllBytes(Path.Combine(Folder(), Required(font, "path")));
        Assert.Equal(Required(font, "sha256"), Hash(program));
        string css = "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(program) + ")}</style>";
        JsonElement[] cases = capture.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(expectedCases, cases.Length);
        Assert.Equal(expectedCases, cases.Select(row => Required(row, "id")).Distinct(StringComparer.Ordinal).Count());
        foreach (JsonElement row in cases)
        {
            string source = file == "decoration-screening.json"
                ? System.Text.Encoding.UTF8.GetString(CreateDecorationScreeningSource(row))
                : File.ReadAllText(Path.Combine(Folder(), Required(row, "input")));
            Assert.Equal(Required(row, "sourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(source)));
            string embedded = source.Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
            embedded = embedded.Insert(embedded.IndexOf('>') + 1, css);
            Assert.Equal(Required(row, "nativeSourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(embedded)));
        }
        foreach (string host in new[] { "windows", "linux" })
        {
            JsonElement launches = native.GetProperty("hosts").GetProperty(host).GetProperty("launches");
            Assert.Equal(3, launches.GetArrayLength());
            foreach (JsonElement launch in launches.EnumerateArray())
            {
                Assert.Equal(0, launch.GetProperty("terminalExitCode").GetInt32());
                Assert.Equal(capturedCases, launch.GetProperty("cases").GetInt32());
                Assert.Equal("healthy", Required(launch, "startupHealth"));
                Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                Assert.False(launch.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, launch.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, launch.GetProperty("networkServiceCrashes").GetInt32());
            }
        }

        static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
