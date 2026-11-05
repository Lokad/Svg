using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgTextPaintBoundsReferenceTests
{
    public static IEnumerable<object[]> AnchorScreeningCases() => CaptureCases("anchor-screening.json");

    [Theory]
    [MemberData(nameof(AnchorScreeningCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public Task Original_anchor_screen_preserves_each_independent_native_target(string id)
        => AssertNativeImage("anchor-screening.json", id);

    [Fact]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Original_anchor_screen_retains_source_font_options_and_healthy_native_triples()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "anchor-screening.json")));
        JsonElement root = capture.RootElement;
        JsonElement native = root.GetProperty("nativeCapture");
        JsonElement font = native.GetProperty("font");
        byte[] program = File.ReadAllBytes(Path.Combine(Folder(), Required(font, "path")));
        Assert.Equal(Required(font, "sha256"), Hash(program));
        string css = "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(program) + ")}</style>";
        JsonElement[] cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(1488, cases.Length);
        Assert.Equal(1488, cases.Select(row => Required(row, "id")).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1488, cases.Select(row => (Required(row, "cohort"), row.GetProperty("screenIndex").GetInt32(), Required(row, "role"))).Distinct().Count());
        Assert.Equal(72, cases.Count(row => Required(row, "cohort") == "original"));
        Assert.Equal(1416, cases.Count(row => Required(row, "cohort") == "matrix"));
        foreach (JsonElement row in cases)
        {
            string source = System.Text.Encoding.UTF8.GetString(CreateAnchorScreeningSource(Required(row, "cohort"), row.GetProperty("screenIndex").GetInt32(), Required(row, "role")));
            Assert.Equal(Required(row, "sourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(source)));
            string embedded = source.Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
            embedded = embedded.Insert(embedded.IndexOf('>') + 1, css);
            Assert.Equal(Required(row, "nativeSourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(embedded)));
            Assert.Equal(1, row.GetProperty("outputScale").GetDouble());
            foreach (string host in new[] { "windows", "linux" })
            {
                JsonElement reference = row.GetProperty("references").GetProperty(host);
                Assert.Equal(32, reference.GetProperty("width").GetInt32());
                Assert.Equal(32, reference.GetProperty("height").GetInt32());
                Assert.Equal(64, Required(reference, "sha256").Length);
                Assert.Equal(64, Required(reference, "rgbaSha256").Length);
            }
        }
        foreach (string host in new[] { "windows", "linux" })
        {
            JsonElement launches = native.GetProperty("hosts").GetProperty(host).GetProperty("launches");
            Assert.Equal(3, launches.GetArrayLength());
            foreach (JsonElement launch in launches.EnumerateArray())
            {
                Assert.Equal(0, launch.GetProperty("terminalExitCode").GetInt32());
                Assert.Equal(1488, launch.GetProperty("cases").GetInt32());
                Assert.Equal("healthy", Required(launch, "startupHealth"));
                Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                Assert.False(launch.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, launch.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, launch.GetProperty("networkServiceCrashes").GetInt32());
            }
        }

        static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static byte[] CreateAnchorScreeningSource(string cohort, int index, string role)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        string candidate, control;
        if (cohort == "original")
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 36);
            string anchor = new[] { "start", "middle", "end" }[index / 12];
            string dx = new[] { "2 3 4", "0 0 0", "0 4 4", "2", "2 0 0", "0 3 0" }[index / 2 % 6];
            string text = index % 2 == 0 ? "AVA" : "III";
            const string attributes = "x='16' y='22' font-family='serif' font-size='12' fill='red'";
            candidate = $"<text {attributes} dx='{dx}' text-anchor='{anchor}'>{text}</text>";
            control = $"<text {attributes} text-anchor='{anchor}'>" + Spans(text, "dx", dx) + "</text>";
        }
        else if (cohort == "matrix")
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 708);
            if (index < 648)
            {
                string anchor = new[] { "start", "middle", "end" }[index / 216];
                string text = new[] { "AVA", "III", "AAV" }[index / 72 % 3];
                string size = new[] { "8", "12", "16" }[index / 24 % 3];
                string dx = new[] { "0 0 0", "2 3 4", "-2 -3 -4", "0 4 4", "0 -30 0", "0 .25 -.75" }[index / 4 % 6];
                string map = new[] { "", "translate(.25 .5)", "scale(.75)", "scale(1.25)" }[index % 4];
                string attributes = $"font-family='serif' font-size='{size}' x='16' y='22' text-anchor='{anchor}'";
                candidate = $"<text {attributes} dx='{dx}'>{text}</text>";
                control = $"<text {attributes}>" + Spans(text, "dx", dx) + "</text>";
                if (map.Length != 0)
                {
                    candidate = $"<g transform='{map}'>" + candidate + "</g>";
                    control = $"<g transform='{map}'>" + control + "</g>";
                }
            }
            else if (index < 702)
            {
                int slot = index - 648;
                string anchor = new[] { "start", "middle", "end" }[slot / 18];
                string kind = new[] { "x", "y", "dy" }[slot / 6 % 3];
                string positions = new[] { "0 4 8", "4 16 28" }[slot / 3 % 2];
                string fit = new[] { "", "spacing", "spacingAndGlyphs" }[slot % 3];
                string attributes = $"font-family='serif' font-size='12' x='16' text-anchor='{anchor}'";
                if (kind != "y") attributes += " y='20'";
                if (kind == "x") attributes = attributes.Replace(" x='16'", "", StringComparison.Ordinal);
                if (fit.Length != 0) attributes += $" textLength='24' lengthAdjust='{fit}'";
                candidate = $"<text {attributes} {kind}='{positions}'>AVA</text>";
                control = $"<text {attributes}>" + Spans("AVA", kind, positions) + "</text>";
            }
            else
            {
                (string anchor, string dx, string translation) = (index - 702) switch
                {
                    0 => ("middle", "0 0 0", "-10.78125"), 1 => ("end", "0 0 0", "-21.578125"),
                    2 => ("middle", "2 3 4", "-14.28125"), 3 => ("end", "2 3 4", "-28.578125"),
                    4 => ("middle", "-2 -3 -4", "-7.28125"), 5 => ("end", "-2 -3 -4", "-14.578125"),
                    _ => throw new InvalidDataException("Unknown anchor calibration.")
                };
                candidate = $"<text font-family='serif' font-size='12' x='16' y='22' text-anchor='{anchor}' dx='{dx}'>AVA</text>";
                control = $"<g transform='translate({translation} 0)'><text font-family='serif' font-size='12' x='16' y='22' text-anchor='start' dx='{dx}'>AVA</text></g>";
            }
        }
        else throw new InvalidDataException("Unknown anchor cohort.");
        string body = role switch { "candidate" => candidate, "control" => control, _ => throw new InvalidDataException("Unknown anchor role.") };
        return System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");

        static string Spans(string text, string kind, string positions)
        {
            string[] values = positions.Split(' ');
            return string.Concat(text.Select((character, i) => $"<tspan {kind}='{(i < values.Length ? values[i] : "0")}'>{character}</tspan>"));
        }
    }
}
