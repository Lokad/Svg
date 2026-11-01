using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Checks one explicit bank case through canonical conversion, retaining independent capture integrity.</summary>
internal static class SvgNativeControlAssertions
{
    internal static void MatchesCanonicalConversion(string bank, string identity, int expectedBankCases)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "capture.json")));
        JsonElement cases = manifest.RootElement.GetProperty("cases");
        Assert.Equal(expectedBankCases, cases.GetArrayLength());
        JsonElement row = cases.EnumerateArray().Single(item => Required(item, "id") == identity);
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
        RgbaImage expected = new NativeReferenceImages().Get(Path.Combine(directory, Required(native, "reference")), digest);
        byte[] png = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        int pixels = checked(expected.Width * expected.Height);
        PixelComparison result = PixelComparer.Compare(expected, PngRgbaDecoder.Decode(png, pixels));
        Assert.True(result.IsExact, JsonSerializer.Serialize(result));

        static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }
}
