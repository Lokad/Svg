using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Integrity")]
public sealed class SvgOriginalTextAcceptanceTests
{
    [Fact]
    public void Original_declarations_keep_exact_sources_options_and_independent_platform_evidence()
    {
        string folder = Path.Combine(ResearchCorpus.Root, "text-node-boundaries");
        using JsonDocument acceptance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "original-acceptance.json")));
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        Dictionary<string, JsonElement> canonical = capture.RootElement.GetProperty("cases").EnumerateArray()
            .ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
        JsonElement declarations = acceptance.RootElement.GetProperty("declarations");
        Assert.Equal(76, declarations.GetArrayLength());
        Assert.Equal(67, declarations.EnumerateArray().Select(row => Required(row, "caseId")).Distinct(StringComparer.Ordinal).Count());
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement declaration in declarations.EnumerateArray())
        {
            Assert.True(ids.Add(Required(declaration, "id")));
            JsonElement row = canonical[Required(declaration, "caseId")];
            foreach (string property in new[] { "sourceSha256", "nativeSourceSha256", "nativeFontSha256" })
                Assert.Equal(Required(declaration, property), Required(row, property));
            Assert.Equal(declaration.GetProperty("font").GetBoolean(), row.GetProperty("font").GetBoolean());
            Assert.Equal(declaration.GetProperty("scale").GetDouble(), row.GetProperty("scale").GetDouble());
            byte[] source = File.ReadAllBytes(Path.Combine(folder, Required(row, "input")));
            Assert.Equal(Required(declaration, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(source)));
            foreach (string host in new[] { "windows", "linux" })
            {
                JsonElement evidence = declaration.GetProperty("platforms").GetProperty(host);
                JsonElement platform = row.GetProperty("platforms").GetProperty(host);
                Assert.Equal(Required(evidence, "rgbaSha256"), Required(platform, "rgbaSha256"));
                byte[] png = File.ReadAllBytes(Path.Combine(folder, Required(platform, "reference")));
                Assert.Equal(Required(platform, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(png)));
                RgbaImage image = PngRgbaDecoder.Decode(png, 16_384);
                Assert.Equal(evidence.GetProperty("width").GetInt32(), image.Width);
                Assert.Equal(evidence.GetProperty("height").GetInt32(), image.Height);
                Assert.Equal(Required(evidence, "rgbaSha256"), Convert.ToHexStringLower(SHA256.HashData(image.Pixels)));
                JsonElement triples = evidence.GetProperty("processPngSha256");
                Assert.Equal(3, triples.GetArrayLength());
                Assert.Single(triples.EnumerateArray().Select(value => value.GetString()).Distinct(StringComparer.Ordinal));
                foreach (JsonElement hash in triples.EnumerateArray())
                    Assert.Equal(32, Convert.FromHexString(hash.GetString() ?? throw new InvalidDataException("Missing process PNG hash.")).Length);
                JsonElement launches = evidence.GetProperty("launches");
                Assert.Equal(3, launches.GetArrayLength());
                foreach (JsonElement launch in launches.EnumerateArray())
                {
                    Assert.Equal("healthy", Required(launch, "startupHealth"));
                    Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                    Assert.False(launch.GetProperty("stderrTruncated").GetBoolean());
                    Assert.Equal(0, launch.GetProperty("sandboxAccessFailures").GetInt32());
                    Assert.Equal(0, launch.GetProperty("networkServiceCrashes").GetInt32());
                }
            }
        }
    }

    [Fact]
    public void Companion_declarations_retain_their_native_equality_or_difference()
    {
        string folder = Path.Combine(ResearchCorpus.Root, "text-node-boundaries");
        using JsonDocument acceptance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "original-acceptance.json")));
        Dictionary<string, JsonElement> declarations = acceptance.RootElement.GetProperty("declarations").EnumerateArray()
            .ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
        JsonElement pairs = acceptance.RootElement.GetProperty("pairs");
        Assert.Equal(38, pairs.GetArrayLength());
        Assert.Equal(declarations.Keys.Order(StringComparer.Ordinal), pairs.EnumerateArray()
            .SelectMany(pair => new[] { Required(pair, "candidate"), Required(pair, "control") }).Order(StringComparer.Ordinal));
        foreach (string host in new[] { "windows", "linux" })
        {
            int unequal = 0;
            foreach (JsonElement pair in pairs.EnumerateArray())
            {
                JsonElement candidate = declarations[Required(pair, "candidate")];
                JsonElement control = declarations[Required(pair, "control")];
                Assert.Equal("candidate", Required(candidate, "role"));
                Assert.Equal("control", Required(control, "role"));
                Assert.Equal(Required(candidate, "originalName"), Required(control, "originalName"));
                bool equal = Required(candidate.GetProperty("platforms").GetProperty(host), "rgbaSha256")
                    == Required(control.GetProperty("platforms").GetProperty(host), "rgbaSha256");
                Assert.Equal(pair.GetProperty("nativeEqual").GetProperty(host).GetBoolean(), equal);
                if (!equal) unequal++;
            }
            Assert.Equal(9, unequal);
        }
    }

    private static string Required(JsonElement row, string property)
        => row.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing original text declaration value.");
}
