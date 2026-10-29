using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgInertTypographyReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "inert-typography", "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray()) yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Inert_geometry_typography_matches_frozen_browser_controls_through_six_outputs(string identity)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "inert-typography");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(6, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", Required(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        Assert.True(root.GetProperty("linuxReferenceQualification").GetProperty("byteIdenticalToFrozenReferences").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        Assert.Equal("inert-typography-control", Required(item, "role"));
        byte[] source = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
        Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(source)));
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(item, "reference")));
        Assert.Equal(Required(item, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(reference)));
        int pixels = checked(item.GetProperty("width").GetInt32() * item.GetProperty("height").GetInt32());
        byte[] actual = SvgConverter.ConvertToPng(source, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), PngRgbaDecoder.Decode(actual, pixels));
        Assert.True(comparison.IsExact, identity + ": " + comparison);
        byte[] target = new byte[actual.Length];
        Assert.Equal(actual.Length, SvgConverter.ConvertToPng(source, target, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(actual, target);
        using MemoryStream stream = new();
        Assert.Equal(actual.Length, SvgConverter.ConvertToPng(source, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(actual, stream.ToArray());
        Assert.Equal(actual, (await SvgConverter.ConvertToPngAsync(source, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(actual.Length, await SvgConverter.ConvertToPngAsync(source, target, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(actual, target);
        using MemoryStream asyncStream = new();
        Assert.Equal(actual.Length, await SvgConverter.ConvertToPngAsync(source, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(actual, asyncStream.ToArray());
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
}
