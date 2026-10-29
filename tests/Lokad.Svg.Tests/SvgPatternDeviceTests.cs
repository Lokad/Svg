using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternDeviceTests
{
    [Fact]
    [Trait("Layer", "Contract")]
    public void Wide_pattern_device_releases_memory_after_refusal_cancellation_and_retry()
    {
        byte[] input = Input();
        byte[] unchanged = input.ToArray();
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(64_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
        {
            RenderBudget budget = new RenderBudget(new HostMemoryBudget(64_000_000));
            byte[] expected;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
                expected = surface.Pixels.ToArray();
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            RenderBudget tight = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, tight, CancellationToken.None);
            }).ErrorCode);
            Assert.Equal(0, tight.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.Throws<OperationCanceledException>(() =>
            {
                using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, cancellation.Token);
            }).CancellationToken);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Equal(expected, retry.Pixels);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    [Trait("Layer", "Contract")]
    public async Task Prepared_replay_preserves_the_scaled_pattern_device()
    {
        byte[] input = Input();
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        string catalogText = "{\"packId\":\"empty\",\"files\":[{\"relativePath\":\"unused.ttf\",\"byteSize\":1,\"sha256\":\"" + new string('0', 64)
            + "\"}],\"families\":[{\"requestedFamily\":\"Unused\",\"resolvedFamily\":\"Unused\",\"relativeFontFile\":\"unused.ttf\"}]}";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(catalogText), "empty");
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    private static byte[] Input() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "raster-capture-profiles", "pattern-viewbox-filter.svg"));

    /// <summary>Rejects unexpected font acquisition in this font-free pattern contract.</summary>
    /// <remarks>Owns no storage or I/O; observes cancellation and supports independent concurrent calls.</remarks>
    private sealed class NoFonts : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The pattern fixture must not request a font program.");
        }
    }
}
