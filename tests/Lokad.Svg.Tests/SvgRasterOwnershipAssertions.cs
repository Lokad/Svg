using System.Security.Cryptography;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

/// <summary>Checks raster scratch lifetime while borrowing an already parsed scene and its font/options owners.</summary>
internal static class SvgRasterOwnershipAssertions
{
    internal static void ReleasesAfterFailures(SvgRoot scene, SvgRenderOptions options, long maximumBytes, string expectedRgbaSha256)
    {
        RenderBudget measured = new(maximumBytes);
        long work;
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
        {
            Assert.Equal(expectedRgbaSha256, Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
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
                limits.MaximumXmlDepth, limits.MaximumAttributesPerElement), options.FontSnapshot);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(scene, shortWork, measured, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(scene, options, measured, cancellation.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);

        using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, measured, CancellationToken.None))
            Assert.Equal(expectedRgbaSha256, Convert.ToHexStringLower(SHA256.HashData(retry.Pixels)));
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
    }
}
