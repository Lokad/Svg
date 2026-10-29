using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgGlyphFallbackTests
{
    private static string DirectoryPath => Path.Combine(ResearchCorpus.Root, "glyph-fallback");

    [Fact]
    public async Task Mixed_coverage_preserves_primary_glyphs_advances_and_complete_document_pixels()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, "mixed-coverage.svg"));
        Assert.Equal(Required(manifest.RootElement, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(new[] { "fonts/Primary.ttf", "fonts/Secondary.ttf" }, provider.Acquisitions);
        Assert.Equal(2, snapshot.Fonts.Count);
        Assert.All(snapshot.Fonts, font => Assert.False(font.Resolution.Resolution.IsFallback));
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgTextStyle style = new(SvgFontFaceRequest.FromCss("FallbackPrimary, FallbackSecondary", 400, false), 40, 16, 56, 0, 0, 0, SvgTextAnchor.Start, "ordinary");
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "AéA", snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(3, run.Glyphs.Length);
            JsonElement faces = manifest.RootElement.GetProperty("expectedFaces"), metrics = manifest.RootElement.GetProperty("independentGlyphs");
            double x = 16;
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                Assert.Equal(faces[index].GetString(), run.GetGlyphFont(index).Font.Resolution.Resolution.Face.ResolvedFamily);
                Assert.Equal(metrics[index].GetProperty("glyphIndex").GetUInt16(), run.Glyphs[index].GlyphIndex);
                double advance = metrics[index].GetProperty("advance").GetDouble() * 40 / 1000;
                Assert.Equal(x, run.Glyphs[index].X, 10); Assert.Equal(advance, run.Glyphs[index].Advance, 10);
                x += advance;
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        using MemoryStream output = new();
        await prepared.ConvertToPngAsync(output, CancellationToken.None);
        Assert.Equal(png, output.ToArray());
        Assert.Equal(4, provider.Acquisitions.Count);
        RgbaImage actual = PngRgbaDecoder.Decode(png, 360 * 260);
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement proof = manifest.RootElement.GetProperty("platforms").GetProperty(platform);
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(proof, "reference")));
            string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
            Assert.Equal(Required(proof, "referenceSha256"), digest);
            Assert.Equal(3, proof.GetProperty("captures").GetArrayLength());
            foreach (JsonElement capture in proof.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal(digest, Required(capture, "pngSha256"));
                Assert.Equal("healthy", Required(capture, "startupHealth")); Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            }
            foreach (JsonProperty control in proof.GetProperty("differentProgramChangedPixels").EnumerateObject()) Assert.True(control.Value.GetInt32() > 0);
            if (platform == (OperatingSystem.IsWindows() ? "windows" : "linux"))
            {
                PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 360 * 260), actual);
                Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
            }
        }
    }

    [Fact]
    public async Task Covered_text_does_not_acquire_unused_fallback_programs()
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        byte[] svg = """<svg xmlns="http://www.w3.org/2000/svg"><text font-family="FallbackPrimary,FallbackSecondary">AAA</text></svg>"""u8.ToArray();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(svg, catalog, provider, CancellationToken.None);
        Assert.Single(snapshot.Fonts);
        Assert.Equal(new[] { "fonts/Primary.ttf" }, provider.Acquisitions);
    }

    [Fact]
    public async Task Missing_everywhere_retains_original_scalar_and_source_diagnostics()
    {
        (SvgFontPackCatalog catalog, FontProvider provider) = Pack();
        byte[] svg = """<svg xmlns="http://www.w3.org/2000/svg"><text id="missing" font-family="FallbackPrimary,FallbackSecondary">AñA</text></svg>"""u8.ToArray();
        SvgException failure = await Assert.ThrowsAsync<SvgException>(async () => await SvgPreparedDocument.CreateAsync(svg, catalog, provider, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(SvgErrorCode.UnresolvedResource, failure.ErrorCode);
        Assert.Equal(SvgFontFailureStage.Glyph, failure.FontDiagnostic?.Stage);
        Assert.Equal(241, failure.FontDiagnostic?.UnicodeScalar); Assert.Equal(1, failure.FontDiagnostic?.ScalarIndex);
        Assert.Equal("missing", failure.SourceDiagnostic?.ElementId);
        Assert.Equal(new[] { "fonts/Primary.ttf", "fonts/Secondary.ttf" }, provider.Acquisitions);
    }

    private static (SvgFontPackCatalog Catalog, FontProvider Provider) Pack()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-glyph-fallback");
        var programs = new Dictionary<string, byte[]>();
        foreach (SvgFontPackFile file in catalog.Files) programs.Add(file.RelativePath, File.ReadAllBytes(Path.Combine(DirectoryPath, file.RelativePath)));
        return (catalog, new(programs));
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    private sealed class FontProvider(Dictionary<string, byte[]> programs) : ISvgFontProgramSourceProvider
    {
        internal List<string> Acquisitions { get; } = [];
        internal Dictionary<string, byte[]> Programs => programs;
        internal Action<string, CancellationToken>? BeforeAcquire { get; set; }
        /// <summary>Borrows original immutable test fonts for this serial operation, observes cancellation and retains no shared cache.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Acquisitions.Add(file.RelativePath);
            BeforeAcquire?.Invoke(file.RelativePath, token);
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(programs.TryGetValue(file.RelativePath, out byte[]? bytes) ? new SvgMemoryFontProgramSource(file.RelativePath, bytes) : null);
        }
    }
}
