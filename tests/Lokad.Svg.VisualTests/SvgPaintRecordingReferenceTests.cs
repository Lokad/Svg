using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPaintRecordingReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "paint-recording", "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { RequiredString(item, "id") };
    }

    public static IEnumerable<object[]> RenderingCases() => SelectedCases(CaseKind.Rendering);
    public static IEnumerable<object[]> OutputCases() => SelectedCases(CaseKind.RoutineOutputs);
    public static IEnumerable<object[]> ExtendedOutputCases() => SelectedCases(CaseKind.ExtendedOutputs);

    [Theory]
    [MemberData(nameof(RenderingCases))]
    [Trait("Layer", "Rendering")]
    public Task Recorded_paints_preserve_browser_results(string identity) => VerifyCase(identity, false);

    [Theory]
    [MemberData(nameof(OutputCases))]
    [Trait("Layer", "Contract")]
    public Task Recorded_paints_preserve_browser_results_through_all_six_outputs(string identity) => VerifyCase(identity, true);

    [Theory]
    [MemberData(nameof(ExtendedOutputCases))]
    [Trait("Layer", "Contract")]
    [Trait("Category", "Research")]
    public Task Extended_recorded_paints_preserve_all_six_outputs(string identity) => VerifyCase(identity, true);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Case_collections_partition_native_rendering_and_all_original_output_matrices()
    {
        string[] all = Cases().Select(static row => (string)row[0]).ToArray();
        string[] routine = OutputCases().Select(static row => (string)row[0]).ToArray();
        string[] extended = ExtendedOutputCases().Select(static row => (string)row[0]).ToArray();
        string[] rendering = RenderingCases().Select(static row => (string)row[0]).ToArray();
        Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Order(StringComparer.Ordinal).ToArray(), routine.Concat(extended).Order(StringComparer.Ordinal).ToArray());
        using JsonDocument selection = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "paint-recording", "api-research-cases.json")));
        string[] declared = selection.RootElement.GetProperty("research").EnumerateArray()
            .Select(static value => value.GetString() ?? throw new InvalidDataException("Missing research identity.")).ToArray();
        Assert.Equal(declared.Length, declared.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(declared.Order(StringComparer.Ordinal).ToArray(), extended.Order(StringComparer.Ordinal).ToArray());
        foreach (JsonElement interaction in selection.RootElement.GetProperty("routineInteractions").EnumerateArray())
            Assert.Contains(RequiredString(interaction, "id"), routine);
        List<string> positive = [];
        foreach (JsonElement source in selection.RootElement.GetProperty("manifests").EnumerateArray())
        {
            string family = RequiredString(source, "bank");
            byte[] bytes = File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, family, "manifest.json"));
            using JsonDocument manifest = JsonDocument.Parse(bytes);
            byte[] normalized = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Equal(RequiredString(source, "lfSha256"), Convert.ToHexStringLower(SHA256.HashData(normalized)));
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            {
                string identity = RequiredString(item, "id");
                if (RequiredString(item, "outcome") == "renders") positive.Add(identity);
                else Assert.Contains(identity, routine);
            }
        }
        Assert.Equal(positive.Order(StringComparer.Ordinal).ToArray(), rendering.Order(StringComparer.Ordinal).ToArray());
    }

    private enum CaseKind { Rendering, RoutineOutputs, ExtendedOutputs }

    private static IEnumerable<object[]> SelectedCases(CaseKind kind)
    {
        using JsonDocument selection = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "paint-recording", "api-research-cases.json")));
        HashSet<string> extended = selection.RootElement.GetProperty("research").EnumerateArray()
            .Select(static value => value.GetString() ?? throw new InvalidDataException("Missing research identity.")).ToHashSet(StringComparer.Ordinal);
        foreach (string family in new[] { "paint-recording" })
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, family, "manifest.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            {
                string identity = RequiredString(item, "id");
                bool include = kind switch
                {
                    CaseKind.Rendering => RequiredString(item, "outcome") == "renders",
                    CaseKind.RoutineOutputs => !extended.Contains(identity),
                    CaseKind.ExtendedOutputs => extended.Contains(identity),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                if (include) yield return new object[] { identity };
            }
        }
    }

    private static async Task VerifyCase(string identity, bool allOutputs)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "paint-recording");
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
        if (!allOutputs) return;
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
