using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contracts")]
public sealed class SvgUniformFilterOwnershipTests
{
    [Theory]
    [InlineData("controls-graphs-srgb-1-candidate-1p5")]
    [InlineData("controls-graphs-linear-9-candidate-1p5")]
    [InlineData("placements-affine-linearrgb-37-candidate-1p5")]
    [InlineData("placements-affine-srgb-37-candidate-1p5")]
    [InlineData("placements-displaced-linearrgb-360-candidate-1p5")]
    [InlineData("aliases-aliases-linear-76-candidate-1p5")]
    public void Uniform_filter_images_release_storage_and_preserve_native_pixels_after_denial_and_retry(string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "filter-uniform-precision");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        string inputPath = row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.");
        string expected = row.GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException("Missing native pixel hash.");
        byte[] input = File.ReadAllBytes(Path.Combine(folder, inputPath));
        byte[] unchanged = input.ToArray();
        SvgRenderOptions options = new(null, row.GetProperty("scale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, CancellationToken.None))
        {
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, options, 1_000_000, expected);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
