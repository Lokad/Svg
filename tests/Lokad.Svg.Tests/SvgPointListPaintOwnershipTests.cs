using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

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
        byte[] unchanged = input.ToArray();
        string expected = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException();
        RenderBudget parser = new(2_000_000);
        long parserWork;
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            parserWork = scene.ParserWorkUnits;
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, SvgRenderOptions.Default, 2_000_000, expected);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new(parser.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot attempt = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedMemory, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        RenderBudget deniedWork = new(2_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot attempt = SvgRootParser.Parse(input, SvgRenderLimits.Default, deniedWork, null, new(parserWork - 1), CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedWork.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            Assert.Single(retry.Geometries);
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }
}
