using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPaintRecordingReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "paint-recording", "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { RequiredString(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Recorded_paints_preserve_browser_results_through_all_six_outputs(string identity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "paint-recording");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", RequiredString(root, "decodedPixelStability"));
        Assert.Equal("healthy", RequiredString(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(value => RequiredString(value, "id") == identity);
        Assert.Contains(RequiredString(item, "license"), new[] { "MIT", "CC0-1.0" });
        Assert.NotEmpty(RequiredString(item, "licenseEvidence"));
        byte[] input = File.ReadAllBytes(Path.Combine(directory, RequiredString(item, "input")));
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(directory, RequiredString(item, "reference")));
        Assert.Equal(RequiredString(item, "sourceSha256"), Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());
        Assert.Equal(RequiredString(item, "referenceSha256"), Convert.ToHexString(SHA256.HashData(referenceBytes)).ToLowerInvariant());
        int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, checked(width * height));
        Assert.Equal(width, reference.Width);
        Assert.Equal(height, reference.Height);
        SvgColor background = RequiredString(item, "background") switch
        {
            "transparent" => SvgColor.Transparent,
            "white" => new(255, 255, 255, 255),
            "black" => new(0, 0, 0, 255),
            _ => throw new InvalidDataException("Unexpected background."),
        };
        SvgRenderOptions options = new(null, 1, background, SvgRenderLimits.Default);
        if (RequiredString(item, "outcome") == "UnsupportedFeature")
        {
            byte[] rejectedDestination = new byte[65_536];
            using MemoryStream rejectedStream = new();
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejectedDestination, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejectedStream, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => { await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None); })).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => { await SvgConverter.ConvertToPngAsync(input, rejectedDestination, options, CancellationToken.None); })).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => { await SvgConverter.ConvertToPngAsync(input, rejectedStream, options, CancellationToken.None); })).ErrorCode);
            Assert.Equal(0, rejectedStream.Length);
            return;
        }
        Assert.Equal("renders", RequiredString(item, "outcome"));
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, checked(width * height)));
        JsonElement recorded = item.GetProperty("comparison");
        Assert.True(comparison.DimensionsMatch);
        Assert.Equal(recorded.GetProperty("IsExact").GetBoolean(), comparison.IsExact);
        Assert.Equal(recorded.GetProperty("ChangedPixelCount").GetInt32(), comparison.ChangedPixelCount);
        Assert.Equal(recorded.GetProperty("MaximumChannelError").GetInt32(), comparison.MaximumChannelError);
        Assert.Equal(recorded.GetProperty("MaximumAlphaError").GetInt32(), comparison.MaximumAlphaError);
        Assert.Equal(recorded.GetProperty("AlphaChangedPixelCount").GetInt32(), comparison.AlphaChangedPixelCount);
        Assert.Equal(recorded.GetProperty("MinimumMismatchX").GetInt32(), comparison.MinimumMismatchX);
        Assert.Equal(recorded.GetProperty("MinimumMismatchY").GetInt32(), comparison.MinimumMismatchY);
        Assert.Equal(recorded.GetProperty("MaximumMismatchX").GetInt32(), comparison.MaximumMismatchX);
        Assert.Equal(recorded.GetProperty("MaximumMismatchY").GetInt32(), comparison.MaximumMismatchY);
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    private static string RequiredString(JsonElement item, string property)
        => item.GetProperty(property).GetString() ?? throw new InvalidDataException(property + " is missing.");
}
