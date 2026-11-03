using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterColorMatrixOwnershipTests
{
    [Fact]
    public void Pending_color_operations_borrow_scene_coefficients_through_refusal_cancellation_and_retry()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "filter-color-matrix");
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "ownership.json")));
        byte[] input = File.ReadAllBytes(Path.Combine(folder, "affine-offset.svg"));
        Assert.Equal(data.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        string expected = data.RootElement.GetProperty("rgbaSha256").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetString() ?? throw new InvalidDataException();
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 4_000_000, 3, expected);
    }
}
