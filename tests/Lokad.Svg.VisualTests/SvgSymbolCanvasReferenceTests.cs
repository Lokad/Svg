using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSymbolCanvasReferenceTests
{
    private static readonly PaintReferenceBank Bank = new("symbol-fractional-frames", "canvas-capture.json");

    public static IEnumerable<object[]> RoutineCases() => Cases(false);
    public static IEnumerable<object[]> ResearchCases() => Cases(true);

    private static IEnumerable<object[]> Cases(bool research)
    {
        foreach (string id in Bank.CaseIds.Where(id => Bank.IsResearch(id) == research))
        {
            if (id.StartsWith("r86-symbol-", StringComparison.Ordinal))
            {
                yield return new object[] { id, "candidate" };
                yield return new object[] { id, "control" };
            }
            else yield return new object[] { id, id.EndsWith("-control", StringComparison.Ordinal) ? "control" : "candidate" };
        }
    }

    [Theory]
    [MemberData(nameof(RoutineCases))]
    [Trait("Layer", "Rendering")]
    public void Retained_canvas_stages_and_local_clipping_match_native_pixels(string id, string role)
        => AssertNativeImage(id, role);

    [Theory]
    [MemberData(nameof(ResearchCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public void Every_original_symbol_and_nested_viewport_retains_its_native_target(string id, string role)
        => AssertNativeImage(id, role);

    [Fact]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Original_source_options_and_native_targets_remain_independently_identified()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root,
            "symbol-fractional-frames", "canvas-capture.json")));
        JsonElement root = capture.RootElement;
        List<string> sources = [], references = [];
        HashSet<string> identities = new(StringComparer.Ordinal);
        int originals = 0, controls = 0;
        foreach (JsonElement row in root.GetProperty("cases").EnumerateArray())
        {
            string id = Required(row, "id");
            if (Required(row, "kind") != "original")
            {
                controls++;
                Assert.Equal(Required(row, "sourceSha256"), Hash(CreateSource(row, Required(row, "role"))));
                continue;
            }
            Assert.Equal("r86-symbol-" + originals, id);
            Assert.Equal(originals++, row.GetProperty("screenIndex").GetInt32());
            double scale = row.GetProperty("outputScale").GetDouble();
            foreach (string role in new[] { "candidate", "control" })
            {
                string source = Hash(CreateSource(row, role));
                Assert.Equal(Required(row.GetProperty("sourceSha256"), role), source);
                identities.Add(source);
                sources.Add(id + "-" + role + ":" + source + ":" + scale.ToString("G", CultureInfo.InvariantCulture));
                JsonElement image = row.GetProperty("references").GetProperty(role);
                Assert.Equal((int)(32 * scale), image.GetProperty("width").GetInt32());
                Assert.Equal((int)(32 * scale), image.GetProperty("height").GetInt32());
                references.Add(id + "-" + role + ":" + Required(image, "pngSha256") + ":" + Required(image, "rgbaSha256")
                    + ":" + image.GetProperty("width").GetInt32() + ":" + image.GetProperty("height").GetInt32());
            }
        }
        Assert.Equal(480, originals);
        Assert.Equal(16, controls);
        Assert.Equal(960, root.GetProperty("originalInputs").GetInt32());
        Assert.Equal(identities.Count, root.GetProperty("originalSourceByteIdentities").GetInt32());
        Assert.Equal(Required(root, "sourceInputsSha256"), Hash(TextEncoding.UTF8.GetBytes(string.Join('\n', sources))));
        Assert.Equal(Required(root, "nativeTargetsSha256"), Hash(TextEncoding.UTF8.GetBytes(string.Join('\n', references))));
        JsonElement native = root.GetProperty("nativeCapture");
        Assert.Equal(5856, native.GetProperty("independentNativeComparisons").GetInt32());
        Assert.True(native.GetProperty("platformTargetsEqual").GetBoolean());
        Assert.Equal(new[] { "windows", "linux" }, native.GetProperty("hosts").EnumerateArray().Select(host => Required(host, "host")));
        foreach (JsonElement host in native.GetProperty("hosts").EnumerateArray())
        {
            Assert.Equal(3, host.GetProperty("attempts").GetArrayLength());
            foreach (JsonElement attempt in host.GetProperty("attempts").EnumerateArray())
            {
                Assert.Equal(960, attempt.GetProperty("originalInputs").GetInt32());
                Assert.Equal(16, attempt.GetProperty("additionalInputs").GetInt32());
                Assert.Equal(0, attempt.GetProperty("nativeTargetChanges").GetInt32());
                foreach (JsonProperty launch in attempt.GetProperty("launches").EnumerateObject())
                {
                    Assert.Equal(0, launch.Value.GetProperty("terminalExitCode").GetInt32());
                    JsonElement health = launch.Value.GetProperty("health");
                    Assert.Equal("healthy", Required(health, "startupHealth"));
                    Assert.True(health.GetProperty("profileCleaned").GetBoolean());
                    Assert.False(health.GetProperty("stderrTruncated").GetBoolean());
                    Assert.Equal(0, health.GetProperty("sandboxAccessFailures").GetInt32());
                    Assert.Equal(0, health.GetProperty("networkServiceCrashes").GetInt32());
                }
            }
        }
    }

    private static void AssertNativeImage(string id, string role)
    {
        JsonElement row = Bank.GetCase(id);
        bool original = Required(row, "kind") == "original";
        byte[] input = CreateSource(row, role);
        Assert.Equal(original ? Required(row.GetProperty("sourceSha256"), role) : Required(row, "sourceSha256"), Hash(input));
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        using SvgPngResult png = SvgConverter.ConvertToOwnedPng(input, options, CancellationToken.None);
        RgbaImage actual = PngRgbaDecoder.Decode(png.Memory, 2304);
        JsonElement reference = original ? row.GetProperty("references").GetProperty(role) : row.GetProperty("reference");
        Assert.Equal(reference.GetProperty("width").GetInt32(), actual.Width);
        Assert.Equal(reference.GetProperty("height").GetInt32(), actual.Height);
        for (int offset = 0; offset < actual.Pixels.Length; offset += 4)
            if (actual.Pixels[offset + 3] == 0) actual.Pixels.AsSpan(offset, 3).Clear();
        Assert.Equal(Required(reference, "rgbaSha256"), Hash(actual.Pixels));
    }

    private static byte[] CreateSource(JsonElement row, string role)
    {
        if (role is not ("candidate" or "control")) throw new InvalidDataException("Unknown symbol source role.");
        string kind = Required(row, "kind");
        int index = kind == "material" ? 0 : row.GetProperty("screenIndex").GetInt32();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 480);
        string shape = kind == "material" ? Required(row, "shape")
            : (index / 48 % 5) switch { 0 => "zero-rect", 1 => "rounded-rect", 2 => "ellipse", 3 => "path", _ => "polygon" };
        string paint = "<linearGradient id='p'" + (index / 240 == 0 ? " x2='1' y2='1'" : " gradientUnits='userSpaceOnUse' x1='2' y1='2' x2='28' y2='24'")
            + "><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        string style = index / 6 % 2 == 0 ? "fill='url(#p)' stroke='none'"
            : "fill='green' stroke='url(#p)' stroke-width='2' stroke-opacity='.75'";
        string pose = (index / 12 % 4) switch { 0 => "", 1 => "translate(.5 .25)", 2 => "scale(.75 1.1)", _ => "rotate(15 16 16)" };
        int box = index / 2 % 3;
        if (kind == "material")
        {
            string material = Required(row, "material");
            paint = material switch
            {
                "solid" => "",
                "radial" => "<radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
                "pattern" => "<pattern id='p' patternUnits='userSpaceOnUse' width='4' height='4'><rect width='2' height='4' fill='red'/><rect x='2' width='2' height='4' fill='blue'/></pattern>",
                _ => throw new InvalidDataException("Unknown source material.")
            };
            style = "fill='green' stroke='" + (material == "solid" ? "#c05030" : "url(#p)") + "' stroke-width='2' stroke-opacity='.75'";
            box = 1;
        }
        string leaf = shape switch
        {
            "zero-rect" => "<rect x='4' y='4' width='20' height='20' rx='0' ry='4' " + style + "/>",
            "rounded-rect" => "<rect x='4' y='4' width='20' height='20' rx='4' ry='4' " + style + "/>",
            "ellipse" => "<ellipse cx='14' cy='14' rx='10' ry='8' " + style + "/>",
            "path" => "<path d='M4 4H24V24H4Z' " + style + "/>",
            "polygon" => "<polygon points='4,4 24,4 24,24 4,24' " + style + "/>",
            _ => throw new InvalidDataException("Unknown source shape.")
        };
        string viewBox = box == 2 ? "-4 -4 40 40" : "0 0 32 32";
        string width = box switch { 0 => "32", 1 => "40", _ => "28" };
        string height = box switch { 0 => "32", 1 => "24", _ => "28" };
        string x = box == 2 ? "2" : "0", y = box == 2 ? "1" : "0";
        string definition = role == "candidate" ? "<symbol id='s' viewBox='" + viewBox + "' preserveAspectRatio='none'>" + leaf + "</symbol>" : "";
        string body = role == "candidate" ? "<use href='#s' x='" + x + "' y='" + y + "' width='" + width + "' height='" + height + "'/>"
            : "<svg x='" + x + "' y='" + y + "' width='" + width + "' height='" + height + "' viewBox='" + viewBox + "' preserveAspectRatio='none'>" + leaf + "</svg>";
        string source = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + paint + definition
            + "</defs><g transform='" + pose + "' opacity='.5'>" + body + "</g></svg>";
        if (kind == "alpha")
        {
            source = source.Replace("opacity='.5'", "opacity='" + Required(row, "opacity") + "'", StringComparison.Ordinal);
            if (row.GetProperty("white").GetBoolean())
                source = source.Replace("stop-color='red'", "stop-color='white'", StringComparison.Ordinal)
                    .Replace("stop-color='blue'", "stop-color='white'", StringComparison.Ordinal);
        }
        else if (kind == "solid") source = source.Replace("url(#p)", "#c05030", StringComparison.Ordinal);
        else if (kind is not ("original" or "material")) throw new InvalidDataException("Unknown source kind.");
        return TextEncoding.UTF8.GetBytes(source);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Required(JsonElement row, string property) => row.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing " + property + ".");
}
