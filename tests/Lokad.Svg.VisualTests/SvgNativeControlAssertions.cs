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
        => MatchesCanonicalConversion(bank, "capture.json", identity, expectedBankCases, options);

    internal static void MatchesCanonicalConversion(string bank, string manifestFile, string identity,
        int expectedBankCases, SvgRenderOptions options)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bank);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, manifestFile)));
        JsonElement row = manifest.RootElement;
        if (row.TryGetProperty("cases", out JsonElement cases))
        {
            Assert.Equal(expectedBankCases, cases.GetArrayLength());
            row = cases.EnumerateArray().Single(item => Required(item, "id") == identity);
        }
        else
        {
            Assert.Equal(1, expectedBankCases);
            Assert.Equal(identity, Required(row, "id"));
        }
        if (row.TryGetProperty("outputScale", out JsonElement scale)) Assert.Equal(scale.GetDouble(), options.OutputScale);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(row, "input")));
        string inputHash = row.TryGetProperty("inputSha256", out JsonElement hash)
            ? hash.GetString() ?? throw new InvalidDataException() : Required(row, "sourceSha256");
        Assert.Equal(inputHash, Convert.ToHexStringLower(SHA256.HashData(input)));
        JsonElement native = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        string digest = Required(native, "referenceSha256");
        Assert.Equal(3, native.GetProperty("captures").GetArrayLength());
        foreach (JsonElement capture in native.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal(digest, Required(capture, "pngSha256"));
            JsonElement health = capture.TryGetProperty("health", out JsonElement nested) ? nested : capture;
            Assert.Equal("healthy", Required(health, "startupHealth"));
            Assert.True(health.GetProperty("profileCleaned").GetBoolean());
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
        RgbaImage actual = PngRgbaDecoder.Decode(png, pixels);
        if (row.TryGetProperty("comparisonWindow", out JsonElement window))
        {
            Assert.Equal(0, window.GetProperty("x").GetInt32());
            Assert.Equal(0, window.GetProperty("y").GetInt32());
            Assert.Equal(window.GetProperty("width").GetInt32(), actual.Width);
            Assert.Equal(window.GetProperty("height").GetInt32(), actual.Height);
            Assert.InRange(actual.Width, 1, expected.Width);
            Assert.InRange(actual.Height, 1, expected.Height);
            if (actual.Width != expected.Width || actual.Height != expected.Height)
            {
                // Only a declared native screenshot margin is excluded. Preserve
                // its hash and pixels; never rescale either image or align ink.
                byte[] comparison = new byte[checked(actual.Width * actual.Height * 4)];
                for (int y = 0; y < actual.Height; y++)
                    expected.Pixels.AsSpan(y * expected.Width * 4, actual.Width * 4).CopyTo(comparison.AsSpan(y * actual.Width * 4));
                expected = new(actual.Width, actual.Height, comparison);
            }
        }
        PixelComparison result = PixelComparer.Compare(expected, actual);
        Assert.True(result.IsExact, JsonSerializer.Serialize(result));

        static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    }
}
