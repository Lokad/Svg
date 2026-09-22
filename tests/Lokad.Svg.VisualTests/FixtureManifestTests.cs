using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class FixtureManifestTests
{
    [Fact]
    public void Every_declared_exact_fixture_has_a_valid_admitted_reference_and_matches_it()
    {
        string testOutput = AppContext.BaseDirectory;
        string fixtures = Path.Combine(testOutput, "Fixtures");
        string manifestPath = Path.Combine(fixtures, "manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        HashSet<string> ids = new(StringComparer.Ordinal);
        int exactFixtureCount = 0;

        foreach (JsonElement fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string id = RequiredString(fixture, "id");
            Assert.True(ids.Add(id), "Duplicate fixture ID: " + id);

            string expectedSupport = RequiredString(fixture, "expectedSupport");
            string expectedFidelity = RequiredString(fixture, "expectedFidelity");

            string assetPath = Path.Combine(fixtures, RequiredString(fixture, "path"));
            Assert.True(File.Exists(assetPath), "Missing fixture: " + assetPath);
            byte[] svg = File.ReadAllBytes(assetPath);
            Assert.True(svg.AsSpan().StartsWith("<!-- SPDX-License-Identifier: CC0-1.0 -->"u8), "Missing CC0 SPDX declaration: " + id);
            Assert.Matches("^[0-9a-f]{64}$", RequiredString(fixture, "originalSha256"));
            Assert.Equal(RequiredString(fixture, "materializedSha256"), Hash(svg));
            Assert.Equal("CC0-1.0", RequiredString(fixture, "license"));

            int width = fixture.GetProperty("width").GetInt32();
            int height = fixture.GetProperty("height").GetInt32();
            int expectedPixels = checked(width * height);
            double scale = fixture.GetProperty("scale").GetDouble();
            string background = RequiredString(fixture, "background");
            Assert.True(width > 0, "Fixture width must be positive: " + id);
            Assert.True(height > 0, "Fixture height must be positive: " + id);
            Assert.Equal("transparent", background);

            if (expectedSupport == "exact")
            {
                Assert.Equal("exact", expectedFidelity);
            string metadataPath = Path.Combine(testOutput, RequiredString(fixture, "oracleIdentity").Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(metadataPath), "Missing reference metadata: " + metadataPath);
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement metadataRoot = metadata.RootElement;
            Assert.Equal(id, RequiredString(metadataRoot, "fixtureId"));
            Assert.Equal(3, metadataRoot.GetProperty("freshProcessCaptures").GetInt32());
            Assert.Equal("identical", RequiredString(metadataRoot, "decodedPixelStability"));
            Assert.True(metadataRoot.GetProperty("comparison").GetProperty("exact").GetBoolean());
            Assert.Equal(width, metadataRoot.GetProperty("viewport").GetProperty("width").GetInt32());
            Assert.Equal(height, metadataRoot.GetProperty("viewport").GetProperty("height").GetInt32());
            Assert.Equal(scale, metadataRoot.GetProperty("viewport").GetProperty("deviceScaleFactor").GetDouble());
            Assert.Equal(background, RequiredString(metadataRoot, "background"));

            string metadataDirectory = Path.GetDirectoryName(metadataPath) ?? throw new InvalidDataException("Reference metadata has no directory.");
            string referencePngPath = Path.Combine(metadataDirectory, RequiredString(metadataRoot, "referencePng"));
            Assert.True(File.Exists(referencePngPath), "Missing reference PNG: " + referencePngPath);
            byte[] referenceBytes = File.ReadAllBytes(referencePngPath);
            string referenceHash = Hash(referenceBytes);
            Assert.Equal(RequiredString(metadataRoot, "referenceSha256"), referenceHash);
            Assert.Equal(RequiredString(fixture, "referenceSha256"), referenceHash);

            RgbaImage reference = PngRgbaDecoder.Decode(referenceBytes, expectedPixels);
            Assert.Equal(width, reference.Width);
            Assert.Equal(height, reference.Height);
            SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default);
            RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, options, CancellationToken.None), SvgRenderLimits.Default.MaximumPixels);
            PixelComparison comparison = PixelComparer.Compare(reference, candidate);
            Assert.True(comparison.IsExact, id + " differs from its admitted reference: " + JsonSerializer.Serialize(comparison));
            exactFixtureCount++;
            }
            else
            {
                Assert.Equal("unsupported", expectedSupport);
                Assert.Equal("unsupported", expectedFidelity);
                Assert.False(string.IsNullOrEmpty(fixture.GetProperty("unsupportedReason").GetString()), "Missing unsupported reason: " + id);
                Assert.False(fixture.TryGetProperty("oracleIdentity", out _), "Unsupported fixture must not claim a reference: " + id);
                Assert.False(fixture.TryGetProperty("referenceSha256", out _), "Unsupported fixture must not claim a reference hash: " + id);
            }
        }

        Assert.True(exactFixtureCount > 0, "The fixture inventory contains no exact cases.");

        static string RequiredString(JsonElement element, string name)
        {
            return element.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
        }

        static string Hash(ReadOnlySpan<byte> bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }
}
