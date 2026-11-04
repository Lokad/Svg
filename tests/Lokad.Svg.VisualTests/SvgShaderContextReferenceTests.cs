using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

public sealed class SvgShaderContextReferenceTests
{
    private static readonly PaintReferenceBank Bank = new("shader-contexts");

    public static IEnumerable<object[]> RoutineCases()
    {
        foreach (string id in Bank.CaseIds.Where(id => !Bank.IsResearch(id)))
        {
            if (id.StartsWith("hairline-context-", StringComparison.Ordinal))
                yield return new object[] { id, "candidate" };
            yield return new object[] { id, "control" };
        }
    }

    public static IEnumerable<object[]> ResearchCases()
    {
        foreach (string id in Bank.CaseIds.Where(Bank.IsResearch))
        {
            yield return new object[] { id, "candidate" };
            yield return new object[] { id, "control" };
        }
    }

    [Theory]
    [MemberData(nameof(RoutineCases))]
    [Trait("Layer", "Rendering")]
    public void Shared_bounds_and_sampling_stages_preserve_native_pixels(string id, string role)
        => AssertNativeImage(id, role);

    [Theory]
    [MemberData(nameof(ResearchCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public void Full_context_screen_preserves_each_original_native_target(string id, string role)
        => AssertNativeImage(id, role);

    [Fact]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Original_sources_options_and_independent_native_contexts_remain_joined()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "shader-contexts", "capture.json")));
        JsonElement root = capture.RootElement;
        Assert.Equal(6486, Bank.CaseIds.Count);
        Assert.Equal(42, RoutineCases().Count());
        Assert.Equal(12924, ResearchCases().Count());
        List<string> sources = new(), references = new();
        HashSet<string> sourceBytes = new(StringComparer.Ordinal);
        int originals = 0, controls = 0;
        foreach (string id in Bank.CaseIds)
        {
            JsonElement row = Bank.GetCase(id);
            int index = row.GetProperty("screenIndex").GetInt32();
            double scale = row.GetProperty("outputScale").GetDouble();
            Assert.Equal(index / 2 % 2 == 0 ? 1 : 1.5, scale);
            JsonElement reference = row.GetProperty("reference");
            Assert.Equal(checked((int)(32 * scale)), reference.GetProperty("width").GetInt32());
            Assert.Equal(checked((int)(32 * scale)), reference.GetProperty("height").GetInt32());
            foreach (string field in new[] { "pngSha256", "rgbaSha256" }) Assert.Equal(64, Required(reference, field).Length);
            bool original = row.GetProperty("sourceSha256").ValueKind == JsonValueKind.Object;
            if (!original)
            {
                controls++;
                Assert.Equal("control", Required(row, "role"));
                Assert.Equal(Required(row, "sourceSha256"), Hash(CreateSource(index, "control", Required(row, "variant"))));
                continue;
            }
            Assert.Equal("hairline-context-" + originals, id);
            Assert.Equal(originals++, index);
            foreach (string role in new[] { "candidate", "control" })
            {
                string hash = Hash(CreateSource(index, role, "original"));
                Assert.Equal(Required(row.GetProperty("sourceSha256"), role), hash);
                sourceBytes.Add(hash);
                sources.Add(id + "-" + role + ":" + hash + ":" + scale.ToString("G", CultureInfo.InvariantCulture));
                references.Add(id + "-" + role + ":" + Required(reference, "pngSha256") + ":" + Required(reference, "rgbaSha256")
                    + ":" + reference.GetProperty("width").GetInt32() + ":" + reference.GetProperty("height").GetInt32());
            }
        }
        Assert.Equal(6480, originals);
        Assert.Equal(6, controls);
        Assert.Equal(12960, root.GetProperty("originalInputs").GetInt32());
        Assert.Equal(sourceBytes.Count, root.GetProperty("sourceByteIdentities").GetInt32());
        Assert.Equal(Required(root, "sourceInputsSha256"), Hash(TextEncoding.UTF8.GetBytes(string.Join('\n', sources))));
        Assert.Equal(Required(root, "nativeTargetsSha256"), Hash(TextEncoding.UTF8.GetBytes(string.Join('\n', references))));
        JsonElement native = root.GetProperty("nativeCapture");
        Assert.Equal(77760, native.GetProperty("independentNativeComparisons").GetInt32());
        Assert.True(native.GetProperty("platformTargetsEqual").GetBoolean());
        Assert.Equal(new[] { "windows", "linux" }, native.GetProperty("hosts").EnumerateArray().Select(host => Required(host, "host")));
        foreach (JsonElement host in native.GetProperty("hosts").EnumerateArray())
        {
            Assert.Equal(Required(host, "browserSha256"), Required(host.GetProperty("pin"), "executableSha256"));
            Assert.Equal(3, host.GetProperty("attempts").GetArrayLength());
            foreach (JsonElement launch in host.GetProperty("attempts").EnumerateArray())
            {
                Assert.Equal(12960, launch.GetProperty("inputs").GetInt32());
                Assert.True(launch.GetProperty("sourceAndReferenceIdentitiesVerified").GetBoolean());
                Assert.Equal(0, launch.GetProperty("pixelsChangedFromOriginal").GetInt32());
                JsonElement health = launch.GetProperty("health");
                Assert.Equal("healthy", Required(health, "startupHealth"));
                Assert.True(health.GetProperty("profileCleaned").GetBoolean());
                Assert.False(health.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, health.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, health.GetProperty("networkServiceCrashes").GetInt32());
            }
        }
    }

    private static void AssertNativeImage(string id, string role)
    {
        JsonElement row = Bank.GetCase(id);
        bool original = row.GetProperty("sourceSha256").ValueKind == JsonValueKind.Object;
        byte[] input = CreateSource(row.GetProperty("screenIndex").GetInt32(), role, original ? "original" : Required(row, "variant"));
        Assert.Equal(original ? Required(row.GetProperty("sourceSha256"), role) : Required(row, "sourceSha256"), Hash(input));
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        RgbaImage actual = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(), 2304);
        JsonElement reference = row.GetProperty("reference");
        Assert.Equal(reference.GetProperty("width").GetInt32(), actual.Width);
        Assert.Equal(reference.GetProperty("height").GetInt32(), actual.Height);
        // Match PixelComparer: RGB beneath zero alpha is unspecified.
        for (int offset = 0; offset < actual.Pixels.Length; offset += 4)
            if (actual.Pixels[offset + 3] == 0) actual.Pixels.AsSpan(offset, 3).Clear();
        Assert.Equal(Required(reference, "rgbaSha256"), Hash(actual.Pixels));
    }

    private static byte[] CreateSource(int index, string role, string variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 6480);
        string paint = (index / 2160) switch
        {
            0 => "<linearGradient id='p' gradientUnits='userSpaceOnUse' x2='32'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            1 => "<radialGradient id='p' gradientUnits='userSpaceOnUse' cx='16' cy='16' r='20'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>"
        };
        string shape = (index / 540 % 4) switch
        {
            0 => "M4 4H24V24H4Z", 1 => "M4 24Q4 4 24 4", 2 => "M4 24C4 4 24 4 24 24", _ => "M4 20A10 8 17 0 1 24 10"
        };
        string width = (index / 180 % 3) switch { 0 => ".25", 1 => ".75", _ => "1.5" };
        string cap = (index / 60 % 3) switch { 0 => "butt", 1 => "round", _ => "square" };
        string pose = (index / 20 % 3) switch { 0 => "translate(.5 .25)", 1 => "scale(.75 1.1)", _ => "rotate(15 16 16)" };
        int consumer = index / 4 % 5;
        string leaf = "<path d='" + shape + "' fill='red' stroke='url(#p)' stroke-width='" + width + "' stroke-linecap='" + cap + "' stroke-opacity='.75'"
            + (index % 2 == 1 ? " vector-effect='non-scaling-stroke'" : "") + "/>";
        string definition = "", body;
        switch (role)
        {
            case "candidate":
                definition = consumer == 1 ? "<symbol id='s' viewBox='0 0 32 32'>" + leaf + "</symbol>" : leaf.Replace("/>", " id='s'/>", StringComparison.Ordinal);
                body = consumer == 1 ? "<use href='#s' width='32' height='32'/>" : "<use href='#s'/>";
                break;
            case "control":
                body = consumer == 1 ? "<svg width='32' height='32' viewBox='0 0 32 32'>" + leaf + "</svg>" : leaf;
                break;
            default: throw new InvalidDataException("Unknown context source role.");
        }
        body = "<g transform='" + pose + "' opacity='.5'>" + body + "</g>";
        if (consumer >= 2)
            body = "<g " + (consumer switch { 2 => "clip-path='url(#c)'", 3 => "mask='url(#m)'", _ => "filter='url(#f)'" }) + ">" + body + "</g>";
        string source = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + paint
            + "<clipPath id='c'><rect x='5.25' y='3.75' width='19.5' height='22'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.5'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter>"
            + definition + "</defs>" + body + "</svg>";
        source = variant switch
        {
            "original" => source,
            "without-filter" => source.Replace("<g filter='url(#f)'>", "<g>", StringComparison.Ordinal),
            "offset-zero" => source.Replace("<feOffset dx='1' dy='1'/>", "<feOffset dx='0' dy='0'/>", StringComparison.Ordinal),
            "offset-pixel" => source.Replace("<feOffset dx='1' dy='1'/>", "<feOffset dx='0.6666666666666666' dy='0.6666666666666666'/>", StringComparison.Ordinal),
            "offset-srgb" => source.Replace("<feOffset dx='1' dy='1'/>", "<feOffset dx='1' dy='1' color-interpolation-filters='sRGB'/>", StringComparison.Ordinal),
            "source-alias" => source.Replace("<feOffset dx='1' dy='1'/>", "<feMerge><feMergeNode in='SourceGraphic'/></feMerge>", StringComparison.Ordinal),
            _ => throw new InvalidDataException("Unknown context control variant.")
        };
        return TextEncoding.UTF8.GetBytes(source);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Required(JsonElement row, string field) => row.GetProperty(field).GetString() ?? throw new InvalidDataException("Missing " + field + ".");
}
