using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;
using TextEncoding = System.Text.Encoding;

namespace Lokad.Svg.VisualTests;

public sealed class SvgFilterSourceContextReferenceTests
{
    private static readonly PaintReferenceBank Bank = new("filter-source-contexts");

    public static IEnumerable<object[]> RoutineCases() => Cases(false);
    public static IEnumerable<object[]> ResearchCases() => Cases(true);

    private static IEnumerable<object[]> Cases(bool research)
    {
        foreach (string id in Bank.CaseIds.Where(id => Bank.IsResearch(id) == research))
        {
            if (id.StartsWith("r86-extended-", StringComparison.Ordinal))
            {
                yield return new object[] { id, "candidate" };
                yield return new object[] { id, "control" };
            }
            else yield return new object[] { id, "candidate" };
        }
    }

    [Theory]
    [MemberData(nameof(RoutineCases))]
    [Trait("Layer", "Rendering")]
    public void Source_capture_and_viewport_classification_preserve_native_pixels(string id, string role)
        => AssertNativeImage(id, role);

    [Theory]
    [MemberData(nameof(ResearchCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public void Full_source_context_screen_preserves_each_original_native_target(string id, string role)
        => AssertNativeImage(id, role);

    [Fact]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Source_options_and_unequal_native_pairs_remain_independently_identified()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "filter-source-contexts", "capture.json")));
        JsonElement root = capture.RootElement;
        Assert.Equal(4621, Bank.CaseIds.Count);
        Assert.Equal(39, RoutineCases().Count());
        Assert.Equal(9190, ResearchCases().Count());
        List<string> sources = new(), references = new();
        HashSet<string> byteIdentities = new(StringComparer.Ordinal);
        int originals = 0, controls = 0, unequal = 0;
        foreach (string id in Bank.CaseIds)
        {
            JsonElement row = Bank.GetCase(id);
            int index = row.GetProperty("screenIndex").GetInt32();
            double scale = row.GetProperty("outputScale").GetDouble();
            Assert.Equal(index / 2 % 2 == 0 ? 1 : 1.5, scale);
            if (row.GetProperty("sourceSha256").ValueKind == JsonValueKind.String)
            {
                controls++;
                Assert.Equal("candidate", Required(row, "role"));
                Assert.Equal(Required(row, "sourceSha256"), Hash(CreateSource(index, "candidate", Required(row, "variant"))));
                continue;
            }
            Assert.Equal("r86-extended-" + originals, id);
            Assert.Equal(originals++, index);
            JsonElement images = row.GetProperty("references");
            if (Required(images.GetProperty("candidate"), "rgbaSha256") != Required(images.GetProperty("control"), "rgbaSha256")) unequal++;
            foreach (string role in new[] { "candidate", "control" })
            {
                string source = Hash(CreateSource(index, role, "original"));
                Assert.Equal(Required(row.GetProperty("sourceSha256"), role), source);
                byteIdentities.Add(source);
                sources.Add(id + "-" + role + ":" + source + ":" + scale.ToString("G", CultureInfo.InvariantCulture));
                JsonElement image = images.GetProperty(role);
                Assert.Equal(checked((int)(32 * scale)), image.GetProperty("width").GetInt32());
                Assert.Equal(checked((int)(32 * scale)), image.GetProperty("height").GetInt32());
                Assert.Equal(64, Required(image, "pngSha256").Length);
                Assert.Equal(64, Required(image, "rgbaSha256").Length);
                references.Add(id + "-" + role + ":" + Required(image, "pngSha256") + ":" + Required(image, "rgbaSha256")
                    + ":" + image.GetProperty("width").GetInt32() + ":" + image.GetProperty("height").GetInt32());
            }
        }
        Assert.Equal(4608, originals);
        Assert.Equal(13, controls);
        Assert.Equal(1110, unequal);
        Assert.Equal(unequal, root.GetProperty("naturallyUnequalNativePairs").GetInt32());
        Assert.Equal(9216, root.GetProperty("originalInputs").GetInt32());
        Assert.Equal(byteIdentities.Count, root.GetProperty("sourceByteIdentities").GetInt32());
        Assert.Equal(Required(root, "sourceInputsSha256"), Hash(TextEncoding.UTF8.GetBytes(string.Join('\n', sources))));
        Assert.Equal(Required(root, "nativeTargetsSha256"), Hash(TextEncoding.UTF8.GetBytes(string.Join('\n', references))));
        JsonElement native = root.GetProperty("nativeCapture");
        Assert.Equal(55296, native.GetProperty("independentNativeComparisons").GetInt32());
        Assert.True(native.GetProperty("platformTargetsEqual").GetBoolean());
        Assert.Equal(new[] { "windows", "linux" }, native.GetProperty("hosts").EnumerateArray().Select(host => Required(host, "host")));
        foreach (JsonElement host in native.GetProperty("hosts").EnumerateArray())
        {
            Assert.Equal(64, Required(host.GetProperty("pin"), "executableSha256").Length);
            Assert.Equal(3, host.GetProperty("attempts").GetArrayLength());
            foreach (JsonElement launch in host.GetProperty("attempts").EnumerateArray())
            {
                Assert.Equal(9216, launch.GetProperty("inputs").GetInt32());
                Assert.Equal(0, launch.GetProperty("terminalExitCode").GetInt32());
                Assert.Equal(0, launch.GetProperty("pixelsChangedFromOriginal").GetInt32());
                Assert.True(launch.GetProperty("sourceAndReferenceIdentitiesVerified").GetBoolean());
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
        JsonElement image = original ? row.GetProperty("references").GetProperty(role) : row.GetProperty("reference");
        Assert.Equal(image.GetProperty("width").GetInt32(), actual.Width);
        Assert.Equal(image.GetProperty("height").GetInt32(), actual.Height);
        for (int offset = 0; offset < actual.Pixels.Length; offset += 4)
            if (actual.Pixels[offset + 3] == 0) actual.Pixels.AsSpan(offset, 3).Clear();
        Assert.Equal(Required(image, "rgbaSha256"), Hash(actual.Pixels));
    }

    private static byte[] CreateSource(int index, string role, string variant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, 4608);
        string paint = (index / 1152) switch
        {
            0 => "",
            1 => "<linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            2 => "<radialGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            _ => "<pattern id='p' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='4' height='8' fill='red'/><rect x='4' width='4' height='8' fill='blue'/></pattern>"
        };
        string corners = index / 576 % 2 == 0 ? "rx='0' ry='4'" : "rx='4' ry='0'";
        string width = (index / 192 % 3) switch { 0 => ".5", 1 => "2", _ => "24" };
        string pose = (index / 48 % 4) switch { 0 => "", 1 => "translate(.5 .25)", 2 => "scale(.75 1.1)", _ => "rotate(15 16 16)" };
        int consumer = index / 16 % 3, effect = index / 4 % 4;
        string fill = index % 2 == 0 ? "red" : "url(#q)";
        string style = "fill='" + fill + "' stroke='" + (index / 1152 == 0 ? "blue" : "url(#p)") + "' stroke-width='" + width + "' stroke-opacity='.75'";
        string leaf = role switch
        {
            "candidate" => "<rect x='4' y='4' width='20' height='20' " + style + " " + corners + "/>",
            "control" => "<path d='M4 4H24V24H4Z' " + style + "/>",
            _ => throw new InvalidDataException("Unknown source role.")
        };
        string definition = "", body = leaf;
        if (consumer == 1)
        {
            definition = leaf.Replace("/>", " id='s'/>", StringComparison.Ordinal);
            body = "<use href='#s'/>";
        }
        else if (consumer == 2)
        {
            definition = "<symbol id='s' viewBox='0 0 32 32'>" + leaf + "</symbol>";
            body = "<use href='#s' width='32' height='32'/>";
        }
        body = "<g transform='" + pose + "' opacity='.5'>" + body + "</g>";
        if (effect != 0)
            body = "<g " + (effect switch { 1 => "clip-path='url(#c)'", 2 => "mask='url(#m)'", _ => "filter='url(#f)'" }) + ">" + body + "</g>";
        string source = "<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'><defs>" + paint
            + "<linearGradient id='q'><stop stop-color='yellow'/><stop offset='1' stop-color='cyan'/></linearGradient><clipPath id='c'><rect x='2' y='2' width='26' height='26'/></clipPath><mask id='m' mask-type='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><rect width='32' height='32' fill='white' fill-opacity='.5'/></mask><filter id='f' filterUnits='userSpaceOnUse' x='0' y='0' width='32' height='32'><feOffset dx='1' dy='1'/></filter>"
            + definition + "</defs>" + body + "</svg>";
        source = variant switch
        {
            "original" => source,
            "without-filter" => source.Replace("<g filter='url(#f)'>", "<g>", StringComparison.Ordinal),
            "offset-zero" => source.Replace("<feOffset dx='1' dy='1'/>", "<feOffset dx='0' dy='0'/>", StringComparison.Ordinal),
            "source-alias" => source.Replace("<feOffset dx='1' dy='1'/>", "<feMerge><feMergeNode in='SourceGraphic'/></feMerge>", StringComparison.Ordinal),
            "stroke-only" => source.Replace("fill='" + fill + "' stroke=", "fill='" + fill + "' fill-opacity='0' stroke=", StringComparison.Ordinal),
            "fill-only" => source.Replace("stroke-opacity='.75'", "stroke-opacity='0'", StringComparison.Ordinal),
            "offset-pixel" => source.Replace("<feOffset dx='1' dy='1'/>", "<feOffset dx='0.6666666666666666' dy='0.6666666666666666'/>", StringComparison.Ordinal),
            "opaque-group" => source.Replace("<g transform='" + pose + "' opacity='.5'>", "<g transform='" + pose + "' opacity='1'>", StringComparison.Ordinal),
            "matrix-identity" => source.Replace("<feOffset dx='1' dy='1'/>", "<feColorMatrix type='saturate' values='1'/>", StringComparison.Ordinal),
            "blur-zero" => source.Replace("<feOffset dx='1' dy='1'/>", "<feGaussianBlur stdDeviation='0'/>", StringComparison.Ordinal),
            "blur-alias" => source.Replace("<feOffset dx='1' dy='1'/>", "<feGaussianBlur stdDeviation='.001'/>", StringComparison.Ordinal),
            "blur-copy" => source.Replace("<feOffset dx='1' dy='1'/>", "<feGaussianBlur stdDeviation='.04'/>", StringComparison.Ordinal),
            _ => throw new InvalidDataException("Unknown source control variant.")
        };
        return TextEncoding.UTF8.GetBytes(source);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Required(JsonElement row, string field) => row.GetProperty(field).GetString() ?? throw new InvalidDataException("Missing " + field + ".");
}
