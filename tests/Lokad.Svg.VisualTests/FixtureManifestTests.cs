using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class FixtureManifestTests
{
    [Fact]
    public void Exact_solid_red_fixture_matches_its_admitted_chrome_reference()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        byte[] svg = File.ReadAllBytes(Path.Combine(fixtures, "original", "solid-red.svg"));
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "tests", "References", "original-solid-red.png")), 4);
        RgbaImage candidate = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None), 4);

        PixelComparison result = PixelComparer.Compare(reference, candidate);

        Assert.True(result.IsExact, "Changed pixels: " + result.ChangedPixelCount);
    }

    [Fact]
    public void Fractional_overlap_fixture_remains_an_explicit_unsupported_failure()
    {
        byte[] svg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "fractional-overlap.svg"));

        SvgException exception = Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(svg, SvgRenderOptions.Default, CancellationToken.None));

        Assert.Equal(SvgErrorCode.UnsupportedFeature, exception.ErrorCode);
    }

    [Fact]
    public void Manifest_has_unique_ids_hash_matched_assets_and_explicit_fidelity_status()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "manifest.json")));
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            string id = fixture.GetProperty("id").GetString() ?? throw new InvalidDataException("Fixture ID is missing.");
            Assert.True(ids.Add(id), "Duplicate fixture ID: " + id);
            string asset = Path.Combine(fixtures, fixture.GetProperty("path").GetString() ?? throw new InvalidDataException("Fixture path is missing."));
            Assert.True(File.Exists(asset), "Missing fixture: " + asset);
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asset))).ToLowerInvariant();
            Assert.Equal(fixture.GetProperty("originalSha256").GetString(), actualHash);
            Assert.Equal(fixture.GetProperty("materializedSha256").GetString(), actualHash);
            Assert.Equal("CC0-1.0", fixture.GetProperty("license").GetString());
            Assert.Contains(fixture.GetProperty("expectedFidelity").GetString(), new[] { "exact", "known-difference", "unsupported", "oracle-failure" });
            if (fixture.TryGetProperty("oracleIdentity", out JsonElement oracleIdentity) && oracleIdentity.GetString() is string referenceMetadataPath)
            {
                string metadataPath = Path.Combine(AppContext.BaseDirectory, referenceMetadataPath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(metadataPath), "Missing reference metadata: " + metadataPath);
                using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(metadataPath));
                string pngPath = Path.Combine(Path.GetDirectoryName(metadataPath) ?? throw new InvalidDataException("Reference metadata has no directory."), reference.RootElement.GetProperty("referencePng").GetString() ?? throw new InvalidDataException("Reference PNG is missing."));
                string referenceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pngPath))).ToLowerInvariant();
                Assert.Equal(reference.RootElement.GetProperty("referenceSha256").GetString(), referenceHash);
                Assert.Equal(fixture.GetProperty("referenceSha256").GetString(), referenceHash);
            }
        }
    }
}
