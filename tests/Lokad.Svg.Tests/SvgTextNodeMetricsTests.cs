using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contract")]
public sealed class SvgTextNodeMetricsTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestData", "text-node-boundaries");
    private static readonly Lazy<byte[]> FontBytes = new(static () => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf")));
    private static readonly Lazy<SvgFontPackCatalog> Catalog = new(static () => SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(Folder, "font-pack.json")), "public-noto-sans"));

    private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> Rows = new(static () =>
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, "capture.json")));
        return capture.RootElement.GetProperty("cases").EnumerateArray()
            .Concat(capture.RootElement.GetProperty("knownDifferences").EnumerateArray())
            .ToDictionary(row => row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case ID."), row => row.Clone());
    });

    // Every capture remains a canonical rendering regression in
    // SvgPaintInputReferenceTests. These inputs exercise distinct replay paths.
    public static IEnumerable<object[]> ReplayCases()
    {
        foreach (string id in new[]
        {
            "node-comment-x2_5", "node-cdata-x2_5", "node-empty-span-x2",
            "shape-span-AV", "shape-span-fi", "shape-long-chunk-middle-middle",
            "element-scale150-span-fi", "element-reflection-span-AVA", "element-quarter-turn-span-AVA",
            "element-direct-fit-spacing-fi-span", "element-path-fit-scale-fi-span",
            "replay-use-visibility-return", "replay-symbol-fill-return",
            "replay-pattern-visibility-return", "replay-mask-fill-return",
            "cluster-fiA-8-unit-start-rotate-candidate",
            "source-items-span-ffi-16-start-gradient-candidate",
            "source-prefixes-relative-span-fiA-neg16-start-fit-scale-candidate",
            "anchored-rectangle-subgrid-70-control", "path-unpainted-original-0",
            "path-unpainted-symbol-63", "path-position-y-only-1-candidate",
            "path-position-activation-41-candidate", "path-position-activation-62-candidate"
        }) yield return [id];
    }

    // Keep allocation paths: reader growth, shaping/rotation arrays, fitting,
    // deferred glyphs, shaders, and each retained resource owner.
    public static IEnumerable<object[]> OwnershipCases()
    {
        foreach (string id in new[]
        {
            "shape-long-chunk-middle-middle", "shape-span-fi",
            "replay-use-visibility-return", "replay-symbol-fill-return",
            "replay-pattern-visibility-return", "replay-mask-fill-return",
            "element-path-fit-scale-fi-span", "cluster-fiA-8-unit-start-rotate-candidate",
            "source-items-span-ffi-16-start-gradient-candidate",
            "source-prefixes-relative-span-fiA-neg16-start-fit-scale-candidate",
            "path-unpainted-original-0", "path-position-y-only-1-candidate",
            "path-position-activation-41-candidate", "path-position-activation-62-candidate"
        }) yield return [id];
    }

    [Theory]
    [MemberData(nameof(ReplayCases))]
    public async Task Node_metrics_preserve_platform_pixels_and_prepared_replay(string id)
    {
        JsonElement row = Rows.Value[id];
        byte[] input = ReadInput(row), unchanged = input.ToArray();
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(row.GetProperty("nativeFontSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(FontBytes.Value)));
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, Catalog.Value, provider, CancellationToken.None);
        Assert.Equal(1, provider.Requests);
        double outputScale = row.GetProperty("scale").GetDouble();
        SvgRenderOptions options = new(null, outputScale, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(8_000_000)), raster = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(input, options.Limits, parser, CancellationToken.None))
        {
            SvgRenderOptions rasterOptions = preparation.ResolveRasterOptions(options);
            using SvgRoot scene = SvgRootParser.ParsePrepared(input, options.Limits, parser, fonts, CancellationToken.None,
                preparation.GetOutputMap(rasterOptions), rasterOptions.Viewport, rasterOptions.OutputScale, preparation);
            using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, rasterOptions, raster, CancellationToken.None);
            Assert.Equal(row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
                .GetProperty("rgbaSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(0, raster.Snapshot().CurrentBytes);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        FontProvider preparedProvider = new();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog.Value, preparedProvider,
            new(null, outputScale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
        Assert.Equal(1, preparedProvider.Requests);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [MemberData(nameof(OwnershipCases))]
    public async Task Node_metadata_releases_owned_storage_after_limits_cancellation_and_retry(string id)
    {
        byte[] input = ReadInput(Rows.Value[id]);
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, Catalog.Value, provider, CancellationToken.None);
        RenderBudget measured = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, CancellationToken.None)) { }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        foreach (long limit in new[] { 1, measured.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(Math.Max(1, limit)));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Assert.Equal(canceled.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, measured, fonts, canceled.Token)).CancellationToken);
        Assert.Equal(0, measured.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using RenderBudget.BudgetReservation held = measured.Reserve(73, SvgMemoryCategory.Scene);
        using (SvgRoot retry = SvgRootParser.Parse(input, options.Limits, measured, fonts, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(retry, options, measured, CancellationToken.None))
            Assert.NotEmpty(surface.Pixels);
        Assert.Equal(73, measured.Snapshot().CurrentBytes);
        Assert.Equal(expected, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Glyph_pattern_texture_resize_preserves_refusal_cleanup_and_prepared_retry()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(Folder, "pattern-texture-resize.svg")), unchanged = input.ToArray();
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, Catalog.Value, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, fonts, CancellationToken.None))
        {
            RenderBudget raster = new RenderBudget(new HostMemoryBudget(8_000_000));
            byte[] pixels;
            using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, raster, CancellationToken.None))
                pixels = surface.Pixels.ToArray();
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(raster.Snapshot().PeakBytes - 1));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgConverter.Rasterize(scene, options, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(scene, options, raster, CancellationToken.None))
                Assert.Equal(pixels, retry.Pixels);
            Assert.Equal(0, raster.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog.Value, provider,
            new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Assert.Equal(canceled.Token, Assert.ThrowsAny<OperationCanceledException>(() => prepared.ConvertToPng(canceled.Token)).CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => prepared.ConvertToPng(CancellationToken.None));
        using SvgPreparedDocument preparedRetry = await SvgPreparedDocument.CreateAsync(input, Catalog.Value, provider,
            new(null, 1.5, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
        Assert.Equal(expected, preparedRetry.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
    }

    private static byte[] ReadInput(JsonElement row)
        => File.ReadAllBytes(Path.Combine(Folder, row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing input.")));

    private sealed class FontProvider : ISvgFontProgramSourceProvider
    {
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);

        /// <summary>Returns immutable borrowed OFL bytes through snapshot disposal, observes cancellation and permits concurrent requests.</summary>
        /// <remarks>The test retains the shared byte array; each returned source owns no storage.</remarks>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requests);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", FontBytes.Value));
        }
    }
}
