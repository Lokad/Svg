using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Checks one explicit bank case through canonical conversion, retaining independent capture integrity.</summary>
internal static class SvgNativeControlAssertions
{
    internal static void MatchesCanonicalConversion(string bank, string identity, int expectedBankCases)
        => MatchesCanonicalConversion(bank, identity, expectedBankCases, SvgRenderOptions.Default);

    internal static void MatchesCanonicalConversion(string bank, string identity, int expectedBankCases, SvgRenderOptions options)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement cases = manifest.RootElement.GetProperty("cases");
        Assert.Equal(expectedBankCases, cases.GetArrayLength());
        JsonElement row = cases.EnumerateArray().Single(item => Required(item, "id") == identity);
        if (row.TryGetProperty("outputScale", out JsonElement scale)) Assert.Equal(scale.GetDouble(), options.OutputScale);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(row, "input")));
        Assert.Equal(Required(row, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        JsonElement native = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        string digest = Required(native, "referenceSha256");
        Assert.Equal(3, native.GetProperty("captures").GetArrayLength());
        foreach (JsonElement capture in native.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(capture, "pngSha256"));
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
        }
        int width = row.TryGetProperty("width", out JsonElement caseWidth)
            ? caseWidth.GetInt32() : manifest.RootElement.GetProperty("width").GetInt32();
        int height = row.TryGetProperty("height", out JsonElement caseHeight)
            ? caseHeight.GetInt32() : manifest.RootElement.GetProperty("height").GetInt32();
        int pixels = checked(width * height);
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(native, "reference")));
        Assert.Equal(digest, Convert.ToHexStringLower(SHA256.HashData(reference)));
        RgbaImage expected = PngRgbaDecoder.Decode(reference, pixels);
        Assert.Equal(width, expected.Width);
        Assert.Equal(height, expected.Height);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        PixelComparison result = PixelComparer.Compare(expected, PngRgbaDecoder.Decode(png, pixels));
        Assert.True(result.IsExact, JsonSerializer.Serialize(result));

        static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }
}
