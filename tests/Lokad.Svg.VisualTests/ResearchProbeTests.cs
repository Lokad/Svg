using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class ResearchProbeTests
{
    [Fact]
    public void Stable_research_probes_retain_their_oracle_evidence_and_stay_categorized()
    {
        string research = Path.Combine(AppContext.BaseDirectory, "Research");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(research, "manifest.json")));
        int probeCount = 0;

        foreach (JsonElement probe in manifest.RootElement.GetProperty("probes").EnumerateArray())
        {
            string id = RequiredString(probe, "id");
            byte[] svg = File.ReadAllBytes(Path.Combine(research, RequiredString(probe, "path")));
            Assert.True(svg.AsSpan().IndexOf("<!-- SPDX-License-Identifier: CC0-1.0 -->"u8) >= 0, "Missing CC0 SPDX declaration: " + id);
            Assert.Equal("CC0-1.0", RequiredString(probe, "license"));
            string sourceHash = RequiredString(probe, "sourceSha256");
            Assert.Equal(sourceHash, Hash(svg));
            if (probe.TryGetProperty("originalSha256", out JsonElement originalHash))
            {
                string originalSourceHash = originalHash.GetString() ?? throw new InvalidDataException("originalSha256 is missing.");
                Assert.Matches("^[0-9a-f]{64}$", originalSourceHash);
                Assert.NotEqual(originalSourceHash, sourceHash);
                Assert.False(string.IsNullOrWhiteSpace(RequiredString(probe, "sourceUrl")));
                Assert.False(string.IsNullOrWhiteSpace(RequiredString(probe, "materialization")));
            }

            JsonElement oracle = probe.GetProperty("oracle");
            Assert.Equal(3, oracle.GetProperty("freshProcessCaptures").GetInt32());
            Assert.Equal("identical", RequiredString(oracle, "decodedPixelStability"));
            Assert.Contains(RequiredString(oracle, "background"), new[] { "transparent", "white", "black" });
            Assert.Matches("^[0-9a-f]{64}$", RequiredString(oracle, "chromeExecutableSha256"));
            Assert.Equal(1, oracle.GetProperty("deviceScaleFactor").GetInt32());
            string reference = Path.Combine(research, RequiredString(oracle, "referencePng").Replace('/', Path.DirectorySeparatorChar));
            byte[] referenceBytes = File.ReadAllBytes(reference);
            Assert.Equal(RequiredString(oracle, "referenceSha256"), Hash(referenceBytes));
            int width = probe.GetProperty("width").GetInt32();
            int height = probe.GetProperty("height").GetInt32();
            RgbaImage referenceImage = PngRgbaDecoder.Decode(referenceBytes, checked(width * height));
            Assert.Equal(width, referenceImage.Width);
            Assert.Equal(height, referenceImage.Height);

            if (probe.TryGetProperty("admittedRendering", out JsonElement admitted))
            {
                string background = RequiredString(oracle, "background");
                SvgColor optionBackground = background switch { "white" => new SvgColor(255, 255, 255, 255), "black" => new SvgColor(0, 0, 0, 255), _ => SvgColor.Transparent };
                SvgViewport? viewport = probe.TryGetProperty("viewport", out JsonElement declaredViewport)
                    ? new SvgViewport(declaredViewport.GetProperty("width").GetInt32(), declaredViewport.GetProperty("height").GetInt32()) : null;
                ReadOnlyMemory<byte> png = SvgConverter.ConvertToPng(svg, new SvgRenderOptions(viewport, 1, optionBackground, SvgRenderLimits.Default, null), CancellationToken.None);
                RgbaImage rendered = PngRgbaDecoder.Decode(png.ToArray(), checked(width * height));
                PixelComparison comparison = PixelComparer.Compare(referenceImage, rendered);
                Assert.True(comparison.DimensionsMatch);
                Assert.Equal(admitted.GetProperty("changedPixelCount").GetInt32() == 0, comparison.IsExact);
                Assert.Equal(admitted.GetProperty("changedPixelCount").GetInt32(), comparison.ChangedPixelCount);
                Assert.Equal((byte)admitted.GetProperty("maximumChannelError").GetInt32(), comparison.MaximumChannelError);
                Assert.Equal((byte)admitted.GetProperty("maximumAlphaError").GetInt32(), comparison.MaximumAlphaError);
                Assert.Equal(admitted.GetProperty("alphaChangedPixelCount").GetInt32(), comparison.AlphaChangedPixelCount);
                Assert.Equal(admitted.GetProperty("minimumMismatchX").GetInt32(), comparison.MinimumMismatchX);
                Assert.Equal(admitted.GetProperty("minimumMismatchY").GetInt32(), comparison.MinimumMismatchY);
                Assert.Equal(admitted.GetProperty("maximumMismatchX").GetInt32(), comparison.MaximumMismatchX);
                Assert.Equal(admitted.GetProperty("maximumMismatchY").GetInt32(), comparison.MaximumMismatchY);
            }
            else
            {
                SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None));
                Assert.Equal(Enum.Parse<SvgErrorCode>(RequiredString(probe, "candidateError")), exception.ErrorCode);
            }

            probeCount++;
        }

        Assert.True(probeCount > 0, "The research inventory contains no probes.");
    }

    private static string RequiredString(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
