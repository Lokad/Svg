using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class FordVariantTests
{
    [Theory]
    [InlineData(250, 100, "transparent")]
    [InlineData(250, 100, "white")]
    [InlineData(250, 100, "black")]
    [InlineData(500, 200, "transparent")]
    [InlineData(500, 200, "white")]
    [InlineData(500, 200, "black")]
    [InlineData(1000, 400, "transparent")]
    [InlineData(1000, 400, "white")]
    [InlineData(1000, 400, "black")]
    public void Every_frozen_Ford_variant_retains_its_original_oracle_and_current_disposition(int width, int height, string background)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "tests", "References", "inventory");
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "inv-002-variants.json")));
        JsonElement root = metadata.RootElement;
        Assert.Equal(9, root.GetProperty("variants").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", root.GetProperty("decodedPixelStability").GetString());
        string id = $"inv-002-{width}x{height}-{background}";
        JsonElement variant = Assert.Single(root.GetProperty("variants").EnumerateArray(), value => value.GetProperty("id").GetString() == id);
        Assert.Equal(width, variant.GetProperty("width").GetInt32());
        Assert.Equal(height, variant.GetProperty("height").GetInt32());
        Assert.Equal(background, variant.GetProperty("background").GetString());
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "candidates", "inv-002.svg"));
        Assert.Equal(root.GetProperty("inputSha256").GetString(), Hash(input));
        string referenceName = variant.GetProperty("referencePng").GetString() ?? throw new InvalidDataException("Ford reference filename is missing.");
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(directory, referenceName));
        Assert.Equal(variant.GetProperty("referenceSha256").GetString(), Hash(referenceBytes));
        Assert.All(variant.GetProperty("captureSha256").EnumerateArray(), capture => Assert.Equal(Hash(referenceBytes), capture.GetString()));
        Assert.All(variant.GetProperty("captureStartupHealth").EnumerateArray(), health => Assert.Equal("healthy", health.GetString()));
        Assert.All(variant.GetProperty("captureProfilesCleaned").EnumerateArray(), cleaned => Assert.True(cleaned.GetBoolean()));
        SvgColor color = background switch { "transparent" => SvgColor.Transparent, "white" => new(255, 255, 255, 255), "black" => new(0, 0, 0, 255), _ => throw new ArgumentOutOfRangeException(nameof(background)) };
        SvgRenderOptions options = new(new SvgViewport(width, height), 1, color, SvgRenderLimits.Default);
        ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(input, options, CancellationToken.None);
        Assert.Equal(variant.GetProperty("candidateSha256").GetString(), Hash(png.Span));
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, width * height);
        RgbaImage candidate = PngRgbaDecoder.Decode(png, width * height);
        PixelComparison comparison = PixelComparer.Compare(reference, candidate);
        JsonElement expected = variant.GetProperty("comparison");
        Assert.True(comparison.DimensionsMatch);
        Assert.Equal(expected.GetProperty("exact").GetBoolean(), comparison.IsExact);
        Assert.Equal(expected.GetProperty("changedPixelCount").GetInt64(), comparison.ChangedPixelCount);
        Assert.Equal(expected.GetProperty("maximumChannelError").GetByte(), comparison.MaximumChannelError);
        Assert.Equal(expected.GetProperty("maximumAlphaError").GetByte(), comparison.MaximumAlphaError);

        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
