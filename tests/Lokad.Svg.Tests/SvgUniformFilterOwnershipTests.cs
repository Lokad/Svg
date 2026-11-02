using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgUniformFilterOwnershipTests
{
    [Fact]
    public void Recursive_affine_capture_releases_source_replay_after_refusal_cancellation_and_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "nested-filter-placement", "fractional-nested-identity.svg"));
        SvgRasterOwnershipAssertions.ParsesAndReleasesAfterFailures(input, SvgRenderOptions.Default, 1_000_000, 4,
            "6ddfba1f66afb91737658738283bbf9269d4d138728a51b5d24c66090ee7e8b6");
    }

    [Fact]
    public void Borrowed_filter_recordings_survive_raster_refusal_cancellation_and_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "filter-source-opacity", "isolated-source-bitmap.svg"));
        RenderBudget parser = new(new HostMemoryBudget(64_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, SvgRenderOptions.Default, 64_000_000,
                "f0f9e01e74d4ecd4769ef238b5fe14c97f81be50afec7462c31cffceb0fe329f");
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
    }

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
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, CancellationToken.None))
        {
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, options, 1_000_000, expected);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
