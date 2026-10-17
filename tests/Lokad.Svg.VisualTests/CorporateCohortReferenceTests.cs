using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class CorporateCohortReferenceTests
{
    [Theory]
    [InlineData("intended", "transparent", 0, 0)]
    public void Original_cubic_source_keeps_its_frozen_intended_frame(string size, string background, int changedPixels, int maximumAlpha)
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
