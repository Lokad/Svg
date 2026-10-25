using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgSolidPaintOwnershipTests
{
    [Theory]
    [InlineData("consumers-source-100-candidate")]
    [InlineData("consumers-source-260-candidate")]
    [InlineData("consumers-source-292-candidate")]
    [InlineData("edges-edge-396-candidate")]
    [InlineData("opacity-scope-100-candidate")]
    [InlineData("opacity-scope-196-candidate")]
    [InlineData("contexts-context-64-candidate")]
    [InlineData("shaders-shader-opacity-1-control")]
    public async Task Source_paints_and_text_layers_release_storage_after_denial_cancellation_and_retry(string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "solid-paint-alpha");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        string path = row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.");
        byte[] input = File.ReadAllBytes(Path.Combine(folder, path));
        byte[] unchanged = input.ToArray();
        JsonElement platform = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        string expected = platform.GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException("Missing native pixels.");
        byte[] fontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
        using SvgFontSnapshot? fonts = row.GetProperty("font").GetBoolean()
            ? await SvgFontSnapshot.CreateAsync(input, SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(folder, "font-pack.json")), "public-noto-sans"), new FontProvider(fontBytes), CancellationToken.None)
            : null;
        SvgRenderOptions options = new(null, row.GetProperty("scale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget parser = new(2_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, fonts, CancellationToken.None))
        {
            RenderBudget measured = new(2_000_000);
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
                    limits.MaximumXmlDepth, limits.MaximumAttributesPerElement), fonts);
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

    private sealed class FontProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        /// <summary>Returns immutable borrowed font bytes through snapshot disposal, observes cancellation and permits concurrent requests.</summary>
        /// <remarks>The test owns the byte array; this provider owns no storage.</remarks>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}
