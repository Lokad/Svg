using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgSymbolLocationReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "symbol-locations", "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { RequiredString(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Symbol_instances_match_stable_browser_references_through_all_six_outputs(string identity)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "symbol-locations");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(14, root.GetProperty("cases").GetArrayLength());
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
        if (RequiredString(item, "outcome") == "UnsupportedFeature")
        {
            byte[] rejectedDestination = new byte[65_536];
            using MemoryStream rejectedStream = new();
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejectedDestination, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejectedStream, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => { await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None); })).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => { await SvgConverter.ConvertToPngAsync(input, rejectedDestination, SvgRenderOptions.Default, CancellationToken.None); })).ErrorCode);
            Assert.Equal(SvgErrorCode.UnsupportedFeature, (await Assert.ThrowsAsync<SvgException>(async () => { await SvgConverter.ConvertToPngAsync(input, rejectedStream, SvgRenderOptions.Default, CancellationToken.None); })).ErrorCode);
            Assert.Equal(0, rejectedStream.Length);
            return;
        }
        Assert.Equal("renders", RequiredString(item, "outcome"));
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(expected, checked(width * height)));
        Assert.True(comparison.IsExact, identity + ": " + comparison);
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    private static string RequiredString(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
}
