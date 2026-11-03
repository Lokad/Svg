using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterSourceOriginOwnershipTests
{
    [Fact]
    public void Tiled_source_capture_releases_storage_after_refusal_cancellation_and_retry()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "filter-source-origins");
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "ownership.json")));
        byte[] input = File.ReadAllBytes(Path.Combine(folder, "wide-solid.svg"));
        Assert.Equal(data.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        string expected = data.RootElement.GetProperty("rgbaSha256").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetString() ?? throw new InvalidDataException();
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 8_000_000, 1, expected);
    }
}
