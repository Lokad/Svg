using System.Text.Json;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgMarkerCanvasOwnershipTests
{
    [Theory]
    [InlineData("use-source-opacity")]
    [InlineData("use-gradient-content")]
    public void Native_canvas_stages_release_after_refusal_cancellation_and_retry(string identity)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "marker-canvas-stages");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(folder, row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        string expected = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException();
        // One host and five authored marker positions, including the closing vertex.
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 2_000_000, 6, expected);
    }
}
