using System.Text.Json;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contracts")]
public sealed class SvgPointListPaintOwnershipTests
{
    [Theory]
    [InlineData("acceptance-gradient-direct-source")]
    [InlineData("acceptance-symbol-nested-viewport")]
    public void Source_paint_and_viewport_owners_release_after_denial_and_retry(string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "point-list-source-maps");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        byte[] input = File.ReadAllBytes(Path.Combine(folder, row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        string expected = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException();
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 2_000_000, 1, expected);
    }
}
