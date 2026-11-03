using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>Checks raster scratch lifetime while borrowing an already parsed scene and its font/options owners.</summary>
internal static class SvgRasterOwnershipAssertions
{
    internal static void ParsesBankCaseAndReleasesAfterFailures(string bank, string inputName, SvgRenderOptions options, long maximumBytes, int expectedGeometryCount)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", bank);
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "ownership.json")));
        byte[] input = File.ReadAllBytes(Path.Combine(folder, inputName));
        Assert.Equal(data.RootElement.GetProperty("inputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        string expected = data.RootElement.GetProperty("rgbaSha256").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetString() ?? throw new InvalidDataException();
        ParsesAndReleasesAfterFailures(input, options, maximumBytes, expectedGeometryCount, expected);
    }

    internal static void ParsesAndReleasesAfterFailures(byte[] input, SvgRenderOptions options, long maximumBytes, int expectedGeometryCount, string expectedRgbaSha256)
    {
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(maximumBytes));
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, options.FontSnapshot, CancellationToken.None))
        {
            ReleasesAfterFailures(scene, options, maximumBytes, expectedRgbaSha256);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        RenderBudget deniedMemory = new RenderBudget(new HostMemoryBudget(parser.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
        {
            using SvgRoot attempt = SvgRootParser.Parse(input, options.Limits, deniedMemory, options.FontSnapshot, CancellationToken.None);
        }).ErrorCode);
        Assert.Equal(0, deniedMemory.Snapshot().CurrentBytes);
        using (SvgRoot retry = SvgRootParser.Parse(input, options.Limits, parser, options.FontSnapshot, CancellationToken.None))
            Assert.Equal(expectedGeometryCount, retry.Geometries.Length);
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    internal static void ReleasesAfterFailures(SvgRoot scene, SvgRenderOptions options, long maximumBytes, string expectedRgbaSha256)
    {
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(maximumBytes));
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
        {
            Assert.Equal(expectedRgbaSha256, Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        RenderBudget denied = new RenderBudget(new HostMemoryBudget(measured.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, options, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(scene, options, measured, cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            Assert.Equal(expectedRgbaSha256, Convert.ToHexStringLower(SHA256.HashData(retry.Pixels)));
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }
}
