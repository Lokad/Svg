using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>Checks separate or shared parsing/raster lifetimes while borrowing input, scene and font/options owners.</summary>
internal static class SvgRasterOwnershipAssertions
{
    /// <summary>Checks separate parser/raster allowances while a sibling scene reservation stays borrowed.</summary>
    /// <remarks>Each call owns its scenes, surfaces, allowances, pixel copy and cancellation source.
    /// Input stays borrowed and unchanged. Parser refusal excludes the sibling charge; raster refusal
    /// uses its independent peak. Exact cancellation tokens and retry pixels retain the original contracts.</remarks>
    internal static void SeparateBudgetsPreserveBorrowedSceneAfterFailures(byte[] input, SvgRenderOptions options, long maximumBytes)
    {
        const long borrowedBytes = 73;
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new(new HostMemoryBudget(maximumBytes));
        using (RenderBudget.BudgetReservation borrower = parser.Reserve(borrowedBytes, SvgMemoryCategory.Scene))
        {
            using (SvgRootParser.Parse(input, options.Limits, parser, null, CancellationToken.None)) { }
            Assert.Equal(borrowedBytes, parser.Snapshot().CurrentBytes);
            RenderBudget denied = new(new HostMemoryBudget(parser.Snapshot().PeakBytes - borrowedBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, options.Limits, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using SvgRoot root = SvgRootParser.Parse(input, options.Limits, parser, CancellationToken.None);
            RenderBudget raster = new(new HostMemoryBudget(maximumBytes));
            byte[] expected;
            using (SvgConverter.RasterizedSurface first = SvgConverter.Rasterize(root, options, raster, CancellationToken.None)) expected = first.Pixels.ToArray();
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            RenderBudget rasterDenied = new(new HostMemoryBudget(raster.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, options, rasterDenied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, rasterDenied.Snapshot().CurrentBytes);
            using CancellationTokenSource canceled = new(); canceled.Cancel();
            Assert.Equal(canceled.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(root, options, raster, canceled.Token)).CancellationToken);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, options, raster, CancellationToken.None)) Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    /// <summary>Checks parsing and rasterization under one allowance, retaining the full refusal sweep and retry path.</summary>
    /// <remarks>Input/options/fonts stay borrowed. Each scene and surface is disposed before checking
    /// the shared allowance. Parsing keeps identity construction scale and the explicit viewport;
    /// rasterization uses the caller's options. The synchronous case owns its cancellation source,
    /// budgets and expected pixel copy; independent calls share no mutable state.</remarks>
    internal static void SharedBudgetReleasesAfterFailures(byte[] input, SvgRenderOptions options, long maximumBytes)
    {
        RenderBudget measured = new(new HostMemoryBudget(maximumBytes));
        byte[] expected;
        using (SvgRoot scene = Parse(measured, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            expected = surface.Pixels.ToArray();
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        long peak = measured.Snapshot().PeakBytes;
        for (long limit = 1; limit < peak; limit += 4096) DenyMemory(limit);
        DenyMemory(peak - 1);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RenderBudget canceled = new(new HostMemoryBudget(maximumBytes));
        Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => Parse(canceled, cancellation.Token)).CancellationToken);
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);
        using (SvgRoot scene = Parse(canceled, CancellationToken.None))
        {
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() => SvgConverter.Rasterize(scene, options, canceled, cancellation.Token)).CancellationToken);
            using SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, canceled, CancellationToken.None);
            Assert.Equal(expected, retry.Pixels);
        }
        Assert.Equal(0, canceled.Snapshot().CurrentBytes);

        SvgRoot Parse(RenderBudget budget, CancellationToken token)
            => SvgRootParser.Parse(input, options.Limits, budget, options.FontSnapshot, token, 1, 1, options.Viewport);

        void DenyMemory(long limit)
        {
            RenderBudget budget = new(new HostMemoryBudget(limit));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgRoot scene = Parse(budget, CancellationToken.None);
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, budget, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

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
