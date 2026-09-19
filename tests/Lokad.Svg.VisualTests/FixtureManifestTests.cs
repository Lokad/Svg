using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.VisualTests;

public sealed class FixtureManifestTests
{
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
        }
    }
}
