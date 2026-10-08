using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgInventoryGapReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = ReadManifest();
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sourced_support_gaps_keep_their_originals_oracles_and_measured_outcomes(string identity)
    {
        using JsonDocument manifest = ReadManifest();
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(value => Required(value, "id") == identity);
        Assert.Contains(Required(item, "license"), new[] { "Public domain", "CC0-1.0" });
        Assert.NotEmpty(Required(item, "sourceUrl"));
        Assert.NotEmpty(Required(item, "creator"));
        Assert.NotEmpty(Required(item, "licenseEvidenceUrl"));
        Assert.NotEmpty(Required(item, "materialization"));
        byte[] original = File.ReadAllBytes(Resolve(Required(item, "original")));
        byte[] input = File.ReadAllBytes(Resolve(Required(item, "input")));
        byte[] referenceBytes = File.ReadAllBytes(Resolve(Required(item, "reference")));
        Assert.Equal(Required(item, "originalSha256"), Hash(original));
        Assert.Equal(Required(item, "inputSha256"), Hash(input));
        Assert.Equal(Required(item, "referenceSha256"), Hash(referenceBytes));
        int width = item.GetProperty("width").GetInt32();
        int height = item.GetProperty("height").GetInt32();
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, checked(width * height));
        Assert.Equal(width, reference.Width);
        Assert.Equal(height, reference.Height);
        JsonElement oracle = item.GetProperty("oracle");
        Assert.Equal(3, oracle.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", Required(oracle, "decodedPixelStability"));
        Assert.Equal("healthy", Required(oracle, "captureStartupHealth"));
        Assert.True(oracle.GetProperty("captureProfilesCleaned").GetBoolean());
        Assert.Equal("transparent", Required(item, "background"));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("viewport").ValueKind);
        SvgRenderOptions options = new(null, item.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        if (Required(item, "renderOutcome") == "UnsupportedFeature")
        {
            SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None));
            Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
            return;
        }
        Assert.Equal("renders", Required(item, "renderOutcome"));
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(png, checked(width * height)));
        Assert.True(comparison.IsExact, identity + " differs from its retained browser reference.");
    }

    private static JsonDocument ReadManifest() => JsonDocument.Parse(File.ReadAllBytes(Resolve("tests/References/inventory/svg-gap-cases.json")));
    private static string Resolve(string path) => Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
    private static string Required(JsonElement item, string property) => item.GetProperty(property).GetString() ?? throw new InvalidDataException(property + " is missing.");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
