using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgCubicRouteReferenceTests
{
    [Fact]
    public void Original_cubic_source_keeps_its_frozen_intended_frame()
    {
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Resolve("tests/References/inventory/inv-027-reference.json")));
        JsonElement document = metadata.RootElement, variant = document.GetProperty("variant");
        // The original source and root-only capture materialization keep independent identities.
        Assert.Equal(Required(document, "sourcePath"), Required(variant, "renderInput"));
        byte[] input = File.ReadAllBytes(Resolve(Required(document, "sourcePath")));
        Assert.Equal(Required(document, "inputSha256"), Hash(input));
        Assert.Equal(Required(variant, "oracleInputSha256"), Hash(File.ReadAllBytes(Resolve(Required(variant, "oracleInput")))));
        byte[] reference = File.ReadAllBytes(Resolve("tests/References/inventory/" + Required(document, "referencePng")));
        Assert.Equal(Required(document, "referenceSha256"), Hash(reference));
        JsonElement[] captures = document.GetProperty("captureProvenance").GetProperty("captures").EnumerateArray().ToArray();
        Assert.Equal(3, captures.Length);
        Assert.All(captures, capture =>
        {
            Assert.Equal(Required(document, "referenceSha256"), Required(capture, "pngSha256"));
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        });
        Assert.Equal("transparent", Required(variant, "background"));
        JsonElement viewport = variant.GetProperty("logicalViewport");
        SvgRenderOptions options = new(new SvgViewport(viewport.GetProperty("width").GetDouble(), viewport.GetProperty("height").GetDouble()), variant.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        int pixels = checked(variant.GetProperty("width").GetInt32() * variant.GetProperty("height").GetInt32());
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None), pixels);
        RgbaImage oracle = PngRgbaDecoder.Decode(reference, pixels);
        PixelComparison diff = PixelComparer.Compare(oracle, candidate);
        Assert.True(diff.DimensionsMatch);
        Assert.True(diff.IsExact);
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

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing " + name);
    private static string Hash(ReadOnlySpan<byte> input) => Convert.ToHexStringLower(SHA256.HashData(input));
    private static string Resolve(string path) => Path.Combine(AppContext.BaseDirectory, path.StartsWith("tests/Fixtures/", StringComparison.Ordinal) ? path[6..] : path);
}
