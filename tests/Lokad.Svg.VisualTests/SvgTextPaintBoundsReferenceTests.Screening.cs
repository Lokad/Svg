using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgTextPaintBoundsReferenceTests
{
    public static IEnumerable<object[]> ScreeningCases()
    {
        foreach (string id in Banks["precision-screening.json"].CaseIds) yield return [id];
    }

    [Theory]
    [MemberData(nameof(ScreeningCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public Task Full_text_paint_screen_preserves_each_original_native_target(string id)
        => AssertNativeImage("precision-screening.json", id);

    [Fact]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Full_text_paint_screen_retains_sources_options_fonts_and_native_capture_contexts()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "precision-screening.json")));
        JsonElement root = capture.RootElement;
        Assert.Equal(960, root.GetProperty("cases").GetArrayLength());
        Dictionary<string, JsonElement> cases = root.GetProperty("cases").EnumerateArray()
            .ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
        Assert.Equal(960, cases.Count);
        JsonElement native = root.GetProperty("nativeCapture");
        Dictionary<string, string> programs = new(StringComparer.Ordinal);
        foreach (JsonElement font in native.GetProperty("fonts").EnumerateArray())
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Folder(), Required(font, "path")));
            Assert.Equal(Required(font, "sha256"), Hash(bytes));
            programs.Add(Required(font, "family"), Convert.ToBase64String(bytes));
        }
        Assert.Equal(2, programs.Count);
        string css = "<style>@font-face{font-family:ReviewFont;font-weight:400;src:url(data:font/ttf;base64,"
            + programs["ReviewFont"] + ")}</style><style>@font-face{font-family:ReviewMono;font-weight:700;src:url(data:font/ttf;base64,"
            + programs["ReviewMono"] + ")}</style>";
        foreach (JsonElement row in cases.Values)
        {
            int index = row.GetProperty("screenIndex").GetInt32();
            string role = Required(row, "role");
            Assert.Equal("spacing-paint-" + index + "-" + role, Required(row, "id"));
            Assert.Equal(index % 2 == 0 ? 1 : 1.5, row.GetProperty("outputScale").GetDouble());
            string source = System.Text.Encoding.UTF8.GetString(CreateScreeningSource(index, role));
            Assert.Equal(Required(row, "sourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(source)));
            string embedded = source.Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal)
                .Replace("font-family='monospace'", "font-family='ReviewMono'", StringComparison.Ordinal);
            embedded = embedded.Insert(embedded.IndexOf('>') + 1, css);
            Assert.Equal(Required(row, "nativeSourceSha256"), Hash(System.Text.Encoding.UTF8.GetBytes(embedded)));
            foreach (string host in new[] { "windows", "linux" })
            {
                JsonElement reference = row.GetProperty("references").GetProperty(host);
                Assert.Equal(checked((int)(160 * row.GetProperty("outputScale").GetDouble())), reference.GetProperty("width").GetInt32());
                Assert.Equal(checked((int)(80 * row.GetProperty("outputScale").GetDouble())), reference.GetProperty("height").GetInt32());
                Assert.Equal(64, Required(reference, "sha256").Length);
                Assert.Equal(64, Required(reference, "rgbaSha256").Length);
                JsonElement other = cases["spacing-paint-" + index + "-" + (role == "candidate" ? "control" : "candidate")];
                Assert.NotEqual(Required(reference, "rgbaSha256"), Required(other.GetProperty("references").GetProperty(host), "rgbaSha256"));
            }
            Assert.NotEqual(Required(row.GetProperty("references").GetProperty("windows"), "rgbaSha256"),
                Required(row.GetProperty("references").GetProperty("linux"), "rgbaSha256"));
        }
        using JsonDocument retained = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "precision-residuals.json")));
        Assert.Equal(39, retained.RootElement.GetProperty("cases").GetArrayLength());
        foreach (JsonElement original in retained.RootElement.GetProperty("cases").EnumerateArray())
        {
            JsonElement row = cases[Required(original, "id")];
            foreach (string field in new[] { "sourceSha256", "nativeSourceSha256" })
                Assert.Equal(Required(original, field), Required(row, field));
            Assert.Equal(original.GetProperty("outputScale").GetDouble(), row.GetProperty("outputScale").GetDouble());
            foreach (string host in new[] { "windows", "linux" })
                foreach (string field in new[] { "sha256", "rgbaSha256" })
                    Assert.Equal(Required(original.GetProperty("references").GetProperty(host), field), Required(row.GetProperty("references").GetProperty(host), field));
        }
        foreach (string host in new[] { "windows", "linux" })
        {
            JsonElement launches = native.GetProperty("hosts").GetProperty(host).GetProperty("launches");
            Assert.Equal(3, launches.GetArrayLength());
            foreach (JsonElement launch in launches.EnumerateArray())
            {
                Assert.Equal(0, launch.GetProperty("terminalExitCode").GetInt32());
                Assert.Equal(960, launch.GetProperty("cases").GetInt32());
                Assert.Equal("healthy", Required(launch, "startupHealth"));
                Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                Assert.False(launch.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, launch.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, launch.GetProperty("networkServiceCrashes").GetInt32());
            }
        }

        static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static byte[] CreateScreeningSource(int index, string role)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 480);
        (string attributes, string pathAttributes, string text) = (index / 24) switch
        {
            0 => ("letter-spacing='.1'", "", "AVj"),
            1 => ("letter-spacing='.75'", "", "AVj"),
            2 => ("letter-spacing='-.1'", "", "AVj"),
            3 => ("letter-spacing='-1.1'", "", "AVj"),
            4 => ("letter-spacing='.1' word-spacing='1.25'", "", "A V"),
            5 => ("letter-spacing='.1' word-spacing='1.25'", "", " AV"),
            6 => ("letter-spacing='-.1' word-spacing='-.75'", "", "AV "),
            7 => ("letter-spacing='1.1' word-spacing='.1'", "", "A\u00a0V"),
            8 => ("letter-spacing='.1' text-anchor='middle'", "startOffset='50%'", "AVj"),
            9 => ("letter-spacing='-.1' text-anchor='end'", "startOffset='90%'", "AVj"),
            10 => ("letter-spacing='.1' textLength='32' lengthAdjust='spacingAndGlyphs'", "", "AVj"),
            11 => ("letter-spacing='.1' textLength='48'", "", "AVj"),
            12 => ("letter-spacing='.1' textLength='48'", "", "A"),
            13 => ("letter-spacing='.1'", "", "A<tspan letter-spacing='.75'>V</tspan>j"),
            14 => ("letter-spacing='.1'", "", "A<tspan font-family='monospace' font-weight='700' font-size='16'>V</tspan>j"),
            15 => ("letter-spacing='.1' rotate='10 -20 30'", "", "AVj"),
            16 => ("letter-spacing='.1' transform='scale(1.5)'", "", "AVj"),
            17 => ("letter-spacing='.1' word-spacing='.1' transform='matrix(1 .25 .1 1 1 2)'", "", "A V"),
            18 => ("letter-spacing='-.1' word-spacing='-12'", "", "A V"),
            19 => ("letter-spacing='-24'", "startOffset='80'", "AVj"),
            _ => throw new InvalidDataException("Unknown text-paint configuration.")
        };
        string units = index / 4 % 2 == 0 ? "objectBoundingBox" : "userSpaceOnUse";
        string definition = (index / 8 % 3) switch
        {
            0 => "<linearGradient id='p' gradientUnits='" + units + "'" + (units == "userSpaceOnUse" ? " x1='4' y1='0' x2='120' y2='0'" : "") + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            1 => "<radialGradient id='p' gradientUnits='" + units + "'" + (units == "userSpaceOnUse" ? " cx='68' cy='36' r='50'" : "") + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' patternUnits='" + units + "' width='" + (units == "userSpaceOnUse" ? "8" : ".5") + "' height='" + (units == "userSpaceOnUse" ? "8" : "1") + "'><rect width='4' height='80' fill='red'/><rect x='4' width='156' height='80' fill='blue'/></pattern>"
        };
        string ink = index / 2 % 2 == 0 ? "fill='url(#p)'" : "fill='none' stroke='url(#p)' stroke-width='.75'";
        string source = "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='80'><defs><path id='baseline' d='M4 36Q70 8 140 36'/>" + definition
            + "</defs><text xml:space='preserve' font-family='serif' font-size='24' " + attributes + " " + ink + "><textPath href='#baseline' " + pathAttributes + ">" + text + "</textPath></text></svg>";
        source = role switch
        {
            "candidate" => source.Replace("</defs>", "</defs><g opacity=\".5\">", StringComparison.Ordinal).Replace("</svg>", "</g></svg>", StringComparison.Ordinal),
            "control" => source,
            _ => throw new InvalidDataException("Unknown native comparison role.")
        };
        return System.Text.Encoding.UTF8.GetBytes(source);
    }
}
