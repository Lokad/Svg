using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSymbolInstanceReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string family in new[] { "symbol-instances", "symbol-presentation", "symbol-content", "symbol-content-diagnostics", "symbol-viewport-opacity", "use-paint-properties", "rectangle-frames", "rectangle-path-strokes", "use-stroke-opacity", "use-stroke-style", "rendered-group-styles", "instance-shader-opacity", "container-shader-inheritance", "viewport-percentage-bases", "transformed-rectangle-fills", "iching-library", "pattern-vector-content", "pattern-inheritance" })
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", family, "manifest.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
                yield return new object[] { family, RequiredString(item, "id") };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Symbol_instances_retain_measured_browser_outcomes_through_all_six_outputs(string family, string identity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", family);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(family switch { "symbol-content-diagnostics" => 3, "symbol-viewport-opacity" => 18, "use-paint-properties" => 31, "rectangle-frames" => 15, "rectangle-path-strokes" => 30, "use-stroke-opacity" => 81, "use-stroke-style" => 99, "rendered-group-styles" => 123, "instance-shader-opacity" => 150, "container-shader-inheritance" => 192, "viewport-percentage-bases" => 81, "transformed-rectangle-fills" => 15, "iching-library" => 25, "pattern-vector-content" => 18, "pattern-inheritance" => 17, _ => 16 }, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("identical", RequiredString(root, "decodedPixelStability"));
        Assert.Equal("healthy", RequiredString(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(value => RequiredString(value, "id") == identity);
        Assert.Equal("CC0-1.0", RequiredString(item, "license"));
        Assert.NotEmpty(RequiredString(item, "licenseEvidence"));
        Assert.NotEmpty(RequiredString(item, "selection"));
        Assert.Equal("transparent", RequiredString(item, "background"));
        byte[] input = File.ReadAllBytes(Path.Combine(directory, RequiredString(item, "input")));
        byte[] referenceBytes = File.ReadAllBytes(Path.Combine(directory, RequiredString(item, "reference")));
        Assert.True(input.AsSpan().IndexOf("SPDX-License-Identifier: CC0-1.0"u8) >= 0);
        Assert.Equal(RequiredString(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(RequiredString(item, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(referenceBytes)));
        int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
        RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, checked(width * height));
        Assert.Equal(width, reference.Width);
        Assert.Equal(height, reference.Height);
        SvgRenderOptions options = item.TryGetProperty("viewport", out JsonElement viewport)
            ? new(new SvgViewport(viewport.GetProperty("width").GetInt32(), viewport.GetProperty("height").GetInt32()), 1, SvgColor.Transparent, SvgRenderLimits.Default)
            : SvgRenderOptions.Default;
        if (item.TryGetProperty("expectedError", out JsonElement error))
        {
            SvgErrorCode expectedError = Enum.Parse<SvgErrorCode>(error.GetString() ?? throw new InvalidDataException());
            Assert.Equal(expectedError, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
            byte[] rejectedDestination = new byte[width * height * 4];
            Assert.Equal(expectedError, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejectedDestination, options, CancellationToken.None)).ErrorCode);
            using MemoryStream rejectedStream = new();
            Assert.Equal(expectedError, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejectedStream, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(expectedError, (await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None))).ErrorCode);
            Assert.Equal(expectedError, (await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, rejectedDestination, options, CancellationToken.None))).ErrorCode);
            Assert.Equal(expectedError, (await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, rejectedStream, options, CancellationToken.None))).ErrorCode);
            Assert.Equal(0, rejectedStream.Length);
            return;
        }
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, checked(width * height)));
        if (item.TryGetProperty("knownDifference", out JsonElement difference)) Assert.Equal(difference.Deserialize<PixelComparison>(), comparison);
        else Assert.True(comparison.IsExact, identity + ": " + comparison);
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

    private static string RequiredString(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
}
