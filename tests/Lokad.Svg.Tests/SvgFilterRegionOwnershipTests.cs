using System.Text.Json;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgFilterRegionOwnershipTests
{
    [Fact]
    public void Retained_object_boxes_and_padded_regions_release_after_refusal_and_retry()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "filter-region-coordinates");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "object-region-object-primitives");
        byte[] input = File.ReadAllBytes(Path.Combine(folder, row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        string expected = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException();
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 2_000_000, 1, expected);
    }
}
