using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgViewportRecordingScopeTests
{
    [Theory]
    [InlineData("linear", "outer", 1, 223, 0, 30, 128)]
    [InlineData("linear", "outer", 1.5, 249, 0, 4, 128)]
    [InlineData("linear", "own", 1, 223, 0, 30, 128)]
    [InlineData("linear", "own", 1.5, 249, 0, 4, 128)]
    [InlineData("linear", "inner", 1, 221, 0, 32, 128)]
    [InlineData("linear", "inner", 1.5, 249, 0, 4, 128)]
    [InlineData("radial", "outer", 1, 0, 0, 255, 128)]
    [InlineData("radial", "outer", 1.5, 0, 0, 255, 128)]
    [InlineData("radial", "own", 1, 0, 0, 255, 128)]
    [InlineData("radial", "own", 1.5, 0, 0, 255, 128)]
    [InlineData("radial", "inner", 1, 0, 0, 253, 128)]
    [InlineData("radial", "inner", 1.5, 0, 0, 253, 128)]
    public void Viewport_boundary_preserves_native_opacity_placement(string shader, string opacity, double scale, byte red, byte green, byte blue, byte alpha)
    {
        string server = shader == "linear" ? "linearGradient" : "radialGradient";
        string definitions = "<defs><" + server + " id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + server + "></defs>";
        string inner = "<g" + (opacity == "inner" ? " opacity='.5'" : "") + "><rect x='4' y='4' width='20' height='20' fill='url(#p)'/></g>";
        string viewport = "<svg width='32' height='32' viewBox='0 0 32 32'" + (opacity == "own" ? " opacity='.5'" : "") + ">" + inner + "</svg>";
        string body = "<g" + (opacity == "outer" ? " opacity='.5'" : "") + ">" + viewport + "</g>";
        foreach (bool forward in new[] { false, true })
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>");
            RenderBudget budget = new(2_000_000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, CancellationToken.None))
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default), budget, CancellationToken.None))
                Assert.Equal(new byte[] { red, green, blue, alpha }, surface.Pixels.AsSpan((6 * surface.Width + 6) * 4, 4).ToArray());
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Isolated_viewport_opacity_releases_storage_on_denial_cancellation_and_retry(bool forward)
    {
        const string definitions = "<defs><linearGradient id='p'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs>";
        const string body = "<g opacity='.5'><svg width='32' height='32'><rect x='4' y='4' width='20' height='20' fill='url(#p)'/></svg></g>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + (forward ? body + definitions : definitions + body) + "</svg>");
        RenderBudget scene = new(2_000_000), raster = new(2_000_000);
        using (RenderBudget.BudgetReservation borrowed = scene.Reserve(73, SvgMemoryCategory.Scene))
        {
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, scene, CancellationToken.None))
            {
                byte[] expected;
                using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None)) expected = surface.Pixels.ToArray();
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                RenderBudget denied = new(raster.Snapshot().PeakBytes - 1);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.Rasterize(root, SvgRenderOptions.Default, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                using (BackgroundInitializationProbe.Arm(cancellation.Cancel, () => { }))
                    Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.Rasterize(root,
                        new(null, 1, new SvgColor(255, 255, 255, 255), SvgRenderLimits.Default), raster, cancellation.Token)).CancellationToken);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
                using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, raster, CancellationToken.None)) Assert.Equal(expected, retry.Pixels);
                Assert.Equal(0, raster.Snapshot().CurrentBytes);
            }
            Assert.Equal(73, scene.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, scene.Snapshot().CurrentBytes);
    }
}
