using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class CorporateCohortReferenceTests
{
    [Fact]
    public void Every_frozen_case_preserves_its_recorded_disposition_and_available_oracle()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests/References/inventory");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "corporate-cohort-v1.json")));
        using JsonDocument measurements = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "corporate-cohort-current.json")));
        Dictionary<string, JsonElement> results = measurements.RootElement.GetProperty("results").EnumerateArray().ToDictionary(result => Required(result, "id"), StringComparer.Ordinal);
        Assert.Equal(258, results.Count);
        Assert.Equal(27, measurements.RootElement.GetProperty("documents").GetArrayLength());
        int renders = 0;
        int malformed = 0;
        int unsupported = 0;
        int exact = 0;
        int known = 0;
        int measured = 0;
        int gaps = 0;
        int unmeasured = 0;
        List<string> exactDocuments = new();
        foreach (JsonElement document in manifest.RootElement.GetProperty("documents").EnumerateArray())
        {
            bool allExact = true;
            foreach (JsonElement variant in document.GetProperty("variants").EnumerateArray())
            {
                string id = Required(variant, "id");
                JsonElement result = results[id];
                byte[] input = File.ReadAllBytes(Resolve(Required(variant, "renderInput")));
                Assert.Equal(Required(variant, "renderInputSha256"), Hash(input));
                Assert.Equal(Hash(input), Required(result, "inputSha256"));
                string state = Required(result, "oracleState");
                if (state == "transport-limit-gap")
                {
                    Assert.False(variant.GetProperty("oracleTransportAdmitted").GetBoolean());
                    gaps++;
                }
                else if (state == "unmeasured") unmeasured++;
                else Assert.Equal("measured", state);
                int width = variant.GetProperty("width").GetInt32();
                int height = variant.GetProperty("height").GetInt32();
                RgbaImage? oracle = null;
                if (state == "measured")
                {
                    byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(result, "referencePng")));
                    Assert.Equal(Required(result, "referenceSha256"), Hash(reference));
                    JsonElement provenance = result.GetProperty("captureProvenance");
                    Assert.Equal(Required(variant, "oracleInputSha256"), Required(provenance, "oracleInputSha256"));
                    JsonElement[] captures = provenance.GetProperty("captures").EnumerateArray().ToArray();
                    Assert.Equal(3, captures.Length);
                    Assert.All(captures, capture =>
                    {
                        Assert.Equal(Hash(reference), Required(capture, "pngSha256"));
                        Assert.Equal("healthy", Required(capture, "startupHealth"));
                        Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
                    });
                    oracle = PngRgbaDecoder.Decode(reference, checked(width * height));
                    Assert.Equal(width, oracle.Width);
                    Assert.Equal(height, oracle.Height);
                    measured++;
                }
                string outcome = Required(result, "renderOutcome");
                if (outcome != "renders")
                {
                    SvgException error = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, Options(variant), CancellationToken.None));
                    Assert.Equal(Enum.Parse<SvgErrorCode>(outcome), error.ErrorCode);
                    Assert.Equal(JsonValueKind.Null, result.GetProperty("comparison").ValueKind);
                    allExact = false;
                    if (error.ErrorCode == SvgErrorCode.MalformedInput) malformed++;
                    else if (error.ErrorCode == SvgErrorCode.UnsupportedFeature) unsupported++;
                    else Assert.Fail("Unexpected rejection: " + id);
                    continue;
                }
                byte[] png = SvgConverter.ConvertToPng(input, Options(variant), CancellationToken.None).ToArray();
                Assert.Equal(Required(result, "candidateSha256"), Hash(png));
                RgbaImage candidate = PngRgbaDecoder.Decode(png, checked(width * height));
                Assert.Equal(width, candidate.Width);
                Assert.Equal(height, candidate.Height);
                renders++;
                if (oracle is null) { allExact = false; continue; }
                PixelComparison diff = PixelComparer.Compare(oracle, candidate);
                JsonElement expected = result.GetProperty("comparison").GetProperty("diff");
                Assert.True(diff.DimensionsMatch);
                Assert.Equal(expected.GetProperty("IsExact").GetBoolean(), diff.IsExact);
                Assert.Equal(expected.GetProperty("ChangedPixelCount").GetInt64(), diff.ChangedPixelCount);
                Assert.Equal(expected.GetProperty("MaximumChannelError").GetByte(), diff.MaximumChannelError);
                Assert.Equal(expected.GetProperty("MaximumAlphaError").GetByte(), diff.MaximumAlphaError);
                if (diff.IsExact) exact++;
                else { known++; allExact = false; }
            }
            if (allExact) exactDocuments.Add(Required(document, "id"));
        }
        Assert.Equal(240, renders);
        Assert.Equal(0, malformed);
        Assert.Equal(18, unsupported);
        Assert.Equal(258, measured);
        Assert.Equal(70, exact);
        Assert.Equal(170, known);
        Assert.Equal(0, gaps);
        Assert.Equal(0, unmeasured);
        Assert.Equal(measured, measurements.RootElement.GetProperty("oracleCounts").GetProperty("measured").GetInt32());
        Assert.Equal(exact, measurements.RootElement.GetProperty("fidelityCounts").GetProperty("exact").GetInt32());
        Assert.Equal(known, measurements.RootElement.GetProperty("fidelityCounts").GetProperty("known-difference").GetInt32());
        Assert.Equal(measured - exact - known, measurements.RootElement.GetProperty("fidelityCounts").GetProperty("measuredRendererRejections").GetInt32());
        Assert.Equal(new[] { "inv-002", "inv-010", "inv-020", "inv-027" }, exactDocuments);
        Assert.Equal(15, measurements.RootElement.GetProperty("oracleTransportFailures").GetProperty("failures").GetArrayLength());
    }

    [Theory]
    [InlineData("small", "transparent", 0, 0)]
    [InlineData("small", "white", 0, 0)]
    [InlineData("small", "black", 0, 0)]
    [InlineData("intended", "transparent", 0, 0)]
    [InlineData("intended", "white", 0, 0)]
    [InlineData("intended", "black", 0, 0)]
    [InlineData("large", "transparent", 0, 0)]
    [InlineData("large", "white", 0, 0)]
    [InlineData("large", "black", 0, 0)]
    public void Selected_curved_mark_matches_every_original_reference(string size, string background, int changedPixels, int maximumAlpha)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Resolve("tests/References/inventory/corporate-cohort-v1.json")));
        JsonElement document = manifest.RootElement.GetProperty("documents").EnumerateArray().Single(item => Required(item, "id") == "inv-027");
        JsonElement variant = document.GetProperty("variants").EnumerateArray().Single(item => Required(item, "size") == size && Required(item, "background") == background);
        // ViewBox-bearing variants render the unmodified source, not an injected path or CTM.
        Assert.Equal(Required(document, "sourcePath"), Required(variant, "renderInput"));
        byte[] input = File.ReadAllBytes(Resolve(Required(document, "sourcePath")));
        Assert.Equal(Required(document, "inputSha256"), Hash(input));
        int pixels = checked(variant.GetProperty("width").GetInt32() * variant.GetProperty("height").GetInt32());
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, Options(variant), CancellationToken.None), pixels);
        RgbaImage oracle = PngRgbaDecoder.Decode(File.ReadAllBytes(Resolve("tests/References/inventory/" + Required(variant, "id") + ".png")), pixels);
        PixelComparison diff = PixelComparer.Compare(oracle, candidate);
        Assert.True(diff.DimensionsMatch);
        Assert.True(diff.IsExact);
        Assert.Equal(changedPixels, diff.ChangedPixelCount);
        Assert.Equal(maximumAlpha, diff.MaximumAlphaError);
    }

    [Fact]
    public void Actual_browser_picture_retains_raw_convexity_and_both_failed_dispatch_models()
    {
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Resolve("tests/References/inventory/inv-027-native-route.json")));
        JsonElement record = native.RootElement;
        byte[] original = File.ReadAllBytes(Resolve("tests/Fixtures/candidates/inv-027.svg"));
        Assert.Equal(Hash(original), Required(record, "inputSha256"));
        Assert.Equal(Hash(File.ReadAllBytes(Resolve("tests/References/inventory/cohort-inputs/inv-027-large.svg"))), Required(record, "oracleInputSha256"));
        Assert.Equal(Hash(File.ReadAllBytes(Resolve("tests/References/inventory/inv-027-large-transparent.png"))), Required(record, "tracedPngSha256"));
        Assert.Equal("healthy", Required(record, "traceStartupHealth"));
        Assert.True(record.GetProperty("traceProfileCleaned").GetBoolean());
        Assert.Equal(877, record.GetProperty("pictureBytes").GetInt32());
        JsonElement[] paths = record.GetProperty("commandLog").EnumerateArray().Where(command => Required(command, "method") == "drawPath").Select(command => command.GetProperty("params").GetProperty("path")).ToArray();
        Assert.Equal(2, paths.Length);
        Assert.False(paths[0].GetProperty("convex").GetBoolean());
        Assert.True(paths[1].GetProperty("convex").GetBoolean());
        Assert.Equal(37, paths[0].GetProperty("pathPoints").EnumerateArray().Sum(verb => verb.GetProperty("points").GetArrayLength()));
        Assert.Equal(12, paths[0].GetProperty("pathPoints").EnumerateArray().Count(verb => Required(verb, "verb") == "Cubic"));
        Assert.True(record.GetProperty("diagnosticDeclaration").GetProperty("declaredBeforePrototypes").GetBoolean());
        JsonElement[] models = record.GetProperty("isolatedModels").EnumerateArray().ToArray();
        Assert.Equal(new[] { "baseline", "interval-live", "flattened" }, models.Select(model => Required(model, "model")));
        foreach (JsonElement model in models)
        {
            Assert.Equal(9, model.GetProperty("cases").GetArrayLength());
            Assert.All(model.GetProperty("cases").EnumerateArray(), item => Assert.False(item.GetProperty("comparison").GetProperty("diff").GetProperty("IsExact").GetBoolean()));
        }
        long[][] alphaMasses = models.Select(model => model.GetProperty("cases").EnumerateArray().Where(item => Required(item, "id").EndsWith("transparent", StringComparison.Ordinal)).Select(item => item.GetProperty("comparison").GetProperty("alphaMass").GetInt64()).ToArray()).ToArray();
        Assert.Equal(new long[] { 7026, 14622, 28474 }, alphaMasses[0]);
        Assert.Equal(new long[] { 356, 787, 1634 }, alphaMasses[1]);
        Assert.Equal(new long[] { 11624, 31962, 63257 }, alphaMasses[2]);
    }

    private static SvgRenderOptions Options(JsonElement variant)
    {
        SvgColor background = Required(variant, "background") switch { "transparent" => SvgColor.Transparent, "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => throw new InvalidDataException("Invalid background.") };
        return new(new SvgViewport(variant.GetProperty("logicalViewport").GetProperty("width").GetDouble(), variant.GetProperty("logicalViewport").GetProperty("height").GetDouble()), variant.GetProperty("outputScale").GetDouble(), background, SvgRenderLimits.Default);
    }
    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static string Hash(ReadOnlySpan<byte> input) => Convert.ToHexStringLower(SHA256.HashData(input));
    private static string Resolve(string path) => Path.Combine(AppContext.BaseDirectory, path.StartsWith("tests/Fixtures/", StringComparison.Ordinal) ? path[6..] : path);
}
