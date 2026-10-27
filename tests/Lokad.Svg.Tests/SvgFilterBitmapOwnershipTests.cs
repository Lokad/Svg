using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

[Trait("Layer", "Contracts")]
public sealed class SvgFilterBitmapOwnershipTests
{
    [Theory]
    [InlineData("filter-affine-restoration", "model-source-alpha")]
    [InlineData("filter-affine-restoration", "interaction-fractional-chain")]
    [InlineData("filter-alpha-stages", "interaction-pattern-alpha")]
    [InlineData("filter-image-placement", "clip-near-integer-aa-parent")]
    [InlineData("filter-stored-images", "interaction-stored-two-branch-merge")]
    [InlineData("filter-stage-boundaries", "alias-materialized-copy-blur")]
    [InlineData("filter-opacity-sampling", "cropped-placements-affine-srgb-91-control-1")]
    [InlineData("filter-opacity-sampling", "contracts-sprite-near-zero-control")]
    public void Filter_scratch_releases_after_denial_cancellation_and_retry(string family, string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "TestData", family);
        byte[] input = File.ReadAllBytes(Path.Combine(folder, "inputs", id + ".svg"));
        byte[] unchanged = input.ToArray();
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        string expected = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException("Missing native pixel hash.");
        RenderBudget parser = new(1_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, parser, CancellationToken.None))
            SvgRasterOwnershipAssertions.ReleasesAfterFailures(scene, SvgRenderOptions.Default,
                SvgRenderLimits.Default.MaximumAccountedBytes, expected);
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Theory]
    [InlineData("filter-affine-restoration", "model-source-alpha")]
    [InlineData("filter-affine-restoration", "interaction-fractional-chain")]
    [InlineData("filter-alpha-stages", "interaction-pattern-alpha")]
    [InlineData("filter-image-placement", "clip-near-integer-aa-parent")]
    [InlineData("filter-stored-images", "interaction-stored-two-branch-merge")]
    [InlineData("filter-stage-boundaries", "alias-materialized-copy-blur")]
    [InlineData("filter-opacity-sampling", "cropped-placements-affine-srgb-91-control-1")]
    [InlineData("filter-opacity-sampling", "contracts-sprite-near-zero-control")]
    public async Task Prepared_bitmap_replay_preserves_input_and_borrowed_destination_after_cancellation(string family, string id)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", family, "inputs", id + ".svg"));
        byte[] unchanged = input.ToArray();
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "text-node-boundaries", "font-pack.json")), "public-noto-sans");
        RejectFonts provider = new();
        byte[] destination = Enumerable.Repeat((byte)0xA5, expected.Length + 17).ToArray();
        using (SvgPreparedDocument canceled = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None))
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => canceled.ConvertToPngAsync(destination, cancellation.Token))).CancellationToken);
            Assert.All(destination, value => Assert.Equal((byte)0xA5, value));
            Assert.Throws<ObjectDisposedException>(() => canceled.ConvertToPng(CancellationToken.None));
        }
        using SvgPreparedDocument retry = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        int count = await retry.ConvertToPngAsync(destination, CancellationToken.None);
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, destination[..count]);
        Assert.All(destination.AsSpan(count).ToArray(), value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(unchanged, input);
    }

    private sealed class RejectFonts : ISvgFontProgramSourceProvider
    {
        /// <summary>Rejects unexpected font requests without owning storage or chargeable source work;
        /// observes cancellation and permits concurrent calls.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("These filter inputs require no fonts.");
        }
    }
}
