using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contracts")]
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
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(2_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, fonts, CancellationToken.None))
        {
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, options, 2_000_000, expected);
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
