using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgFontFamilyStackTests
{
    private static string DirectoryPath => Path.Combine(ResearchCorpus.Root, "font-family-stacks");

    [Fact]
    public async Task Ordered_document_selects_authored_programs_and_matches_frozen_Chrome_pixels()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases")[0];
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, "ordered-families.svg"));
        Assert.Equal(Required(item, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-family-stacks");
        var programs = new Dictionary<string, byte[]>();
        foreach (JsonElement program in manifest.RootElement.GetProperty("programs").EnumerateArray())
            programs.Add(Required(program, "relativePath"), File.ReadAllBytes(Path.Combine(DirectoryPath, Required(program, "source"))));
        FontProvider provider = new(programs);
        SvgFontSelectionPolicy policy = new(new Dictionary<string, string> { ["serif"] = "Example", ["monospace"] = "Mono" }, ["Mono"]);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, policy, SvgRenderLimits.Default, CancellationToken.None);
        Assert.Equal(new[] { Required(item, "expectedFile") }, provider.Acquisitions);
        Assert.Equal(2, snapshot.Fonts.Count);
        foreach (SvgPreparedFont font in snapshot.Fonts)
        {
            Assert.Equal(Required(item, "expectedFamily"), font.Resolution.Resolution.Face.ResolvedFamily);
            Assert.Equal(Required(item, "expectedFile"), font.Resolution.Resolution.Face.File.RelativePath);
            Assert.False(font.Resolution.Resolution.IsFallback);
        }
        // Reuse the qualified identical-program metrics instead of refreshing them.
        using JsonDocument metrics = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "../supplied-font-labels/metrics.json")));
        JsonElement expectedMetrics = metrics.RootElement.GetProperty("cases").EnumerateArray().Single(value => Required(value, "id") == "pair-and-space");
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(snapshot.Fonts[0], "AV A", 24, 8, 48, CancellationToken.None);
        JsonElement expectedGlyphs = expectedMetrics.GetProperty("glyphs");
        Assert.Equal(expectedGlyphs.GetArrayLength(), glyphs.Length);
        for (int index = 0; index < glyphs.Length; index++)
        {
            Assert.Equal(expectedGlyphs[index].GetProperty("glyphIndex").GetUInt16(), glyphs[index].GlyphIndex);
            Assert.Equal(expectedGlyphs[index].GetProperty("x").GetDouble(), glyphs[index].X, 10);
            Assert.Equal(expectedGlyphs[index].GetProperty("advance").GetDouble(), glyphs[index].Advance, 10);
        }
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default.WithFontSelectionPolicy(policy), CancellationToken.None);
        using MemoryStream output = new();
        await prepared.ConvertToPngAsync(output, CancellationToken.None);
        Assert.Equal(png, output.ToArray());
        Assert.Equal(new[] { Required(item, "expectedFile"), Required(item, "expectedFile") }, provider.Acquisitions);
        RgbaImage actual = PngRgbaDecoder.Decode(png, 420 * 96);
        Assert.Equal(420, actual.Width); Assert.Equal(96, actual.Height);
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement proof = item.GetProperty("platforms").GetProperty(platform);
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(proof, "reference")));
            string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
            Assert.Equal(Required(proof, "referenceSha256"), digest);
            Assert.Equal(3, proof.GetProperty("captures").GetArrayLength());
            foreach (JsonElement capture in proof.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal(digest, Required(capture, "pngSha256"));
                Assert.Equal("healthy", Required(capture, "startupHealth"));
                Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            }
            Assert.True(proof.GetProperty("differentProgramChangedPixels").GetInt32() > 0);
            if (platform == (OperatingSystem.IsWindows() ? "windows" : "linux"))
            {
                PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 420 * 96), actual);
                Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
            }
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    private sealed class FontProvider(Dictionary<string, byte[]> programs) : ISvgFontProgramSourceProvider
    {
        internal List<string> Acquisitions { get; } = [];
        /// <summary>Borrows existing immutable public programs for one serial operation; observes cancellation and owns no cross-operation cache.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Acquisitions.Add(file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, programs[file.RelativePath]));
        }
    }
}
