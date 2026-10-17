using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgMarkerReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string family in new[] { "marker-content", "path-marker-content", "marker-instance-content" })
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "Research", family);
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
                yield return new object[] { family, Required(item, "id") };
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Marker_cases_preserve_frozen_pixels_and_explicit_boundaries_through_six_apis(string family, string identity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", family);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(family switch { "marker-content" => 20, "path-marker-content" => 23, "marker-instance-content" => 15, _ => throw new InvalidDataException("Unknown marker family.") }, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", Required(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        Assert.True(root.GetProperty("linuxReferenceQualification").GetProperty("byteIdenticalToFrozenReferences").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(item, "reference")));
        Assert.Equal("CC0-1.0", Required(item, "license"));
        Assert.Contains("SPDX-License-Identifier: CC0-1.0", System.Text.Encoding.UTF8.GetString(input));
        Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(Required(item, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(reference)));
        JsonElement[] captures = item.GetProperty("captures").EnumerateArray().ToArray();
        Assert.Equal(3, captures.Length);
        Assert.All(captures, capture =>
        {
            Assert.Equal(Required(item, "referenceSha256"), Required(capture, "pngSha256"));
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        });
        SvgRenderOptions options = new(null, item.GetProperty("scale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        if (item.TryGetProperty("expectedError", out JsonElement expected))
        {
            SvgErrorCode code = Enum.Parse<SvgErrorCode>(expected.GetString() ?? throw new InvalidDataException());
            Assert.Equal("boundary", Required(item, "role"));
            byte[] target = new byte[130 * 130 * 4];
            using MemoryStream stream = new();
            Check(Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)));
            Check(Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, target, options, CancellationToken.None)));
            Check(Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None)));
            Check(await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)));
            Check(await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, target, options, CancellationToken.None)));
            Check(await Assert.ThrowsAsync<SvgException>(async () => await SvgConverter.ConvertToPngAsync(input, stream, options, CancellationToken.None)));
            Assert.Equal(0, stream.Length);
            return;

            void Check(SvgException error)
            {
                Assert.Equal(code, error.ErrorCode);
                Assert.Equal(Required(item, "expectedMessage"), error.Message);
            }
        }
        Assert.Equal(item.TryGetProperty("document", out _) ? "document-output" : "fidelity", Required(item, "role"));
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 130 * 130), PngRgbaDecoder.Decode(png, 130 * 130));
        Assert.True(comparison.IsExact, identity + ": " + comparison);
        byte[] destination = new byte[png.Length];
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream output = new();
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, output, options, CancellationToken.None));
        Assert.Equal(png, output.ToArray());
        Assert.Equal(png, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream asyncOutput = new();
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, asyncOutput, options, CancellationToken.None));
        Assert.Equal(png, asyncOutput.ToArray());
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
}
