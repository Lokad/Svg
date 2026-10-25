using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

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
            RenderBudget measured = new(1_000_000);
            long work;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            {
                Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
                work = surface.WorkUnits;
            }
            Assert.Equal(0, measured.Snapshot().CurrentBytes);

            RenderBudget denied = new(measured.Snapshot().PeakBytes - 1);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, options, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);

            SvgRenderLimits limits = options.Limits;
            SvgRenderOptions shortWork = new(options.Viewport, options.OutputScale, options.BackgroundColor,
                new(limits.MaximumAccountedBytes, limits.MaximumInputBytes, limits.MaximumDimension, limits.MaximumPixels,
                    limits.MaximumEncodedPngBytes, limits.MaximumNodes, limits.MaximumPathCommands, checked((int)work - 1),
                    limits.MaximumXmlDepth, limits.MaximumAttributesPerElement));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, shortWork, measured, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);

            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(scene, options, measured, cancellation.Token)).CancellationToken);
            Assert.Equal(0, measured.Snapshot().CurrentBytes);

            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
                Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(retry.Pixels)));
            Assert.Equal(0, measured.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
