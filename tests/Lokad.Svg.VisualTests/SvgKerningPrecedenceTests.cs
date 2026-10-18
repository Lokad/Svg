using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgKerningPrecedenceTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "kerning-precedence");

    [Fact]
    public async Task Gpos_and_legacy_document_matches_independent_metrics_and_frozen_pixels()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement recipe = manifest.RootElement;
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(recipe, "input")));
        Assert.Equal(Required(recipe, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-kerning-precedence");
        FontProvider provider = new();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(4, snapshot.Fonts.Count);
        Assert.Equal(4, provider.Acquisitions.Count);
        Assert.Equal(4, provider.Acquisitions.Distinct(StringComparer.Ordinal).Count());
        foreach (SvgPreparedFont font in snapshot.Fonts)
        {
            Assert.False(font.Resolution.Resolution.IsFallback);
            Assert.Equal(font.Resolution.Resolution.Request.Family, font.Resolution.Resolution.Face.ResolvedFamily);
            Assert.Equal("fonts/" + font.Resolution.Resolution.Request.Family + ".ttf", font.Resolution.Resolution.Face.File.RelativePath);
        }
        using JsonDocument metrics = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "metrics.json")));
        foreach (JsonElement row in metrics.RootElement.GetProperty("rows").EnumerateArray())
        {
            SvgPreparedFont font = snapshot.Fonts.Single(f => f.Resolution.Resolution.Request.Family == Required(row, "family"));
            Assert.Equal(Required(row, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(font.Bytes.Span)));
            SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, "AV To Wa", row.GetProperty("size").GetDouble(),
                row.GetProperty("x").GetDouble(), row.GetProperty("y").GetDouble(), CancellationToken.None);
            JsonElement expected = row.GetProperty("glyphs");
            Assert.Equal(expected.GetArrayLength(), glyphs.Length);
            for (int index = 0; index < glyphs.Length; index++)
            {
                Assert.Equal(expected[index].GetProperty("glyphIndex").GetUInt16(), glyphs[index].GlyphIndex);
                Assert.Equal(expected[index].GetProperty("x").GetDouble(), glyphs[index].X, 10);
                Assert.Equal(expected[index].GetProperty("y").GetDouble(), glyphs[index].Y, 10);
                Assert.Equal(expected[index].GetProperty("nominalAdvance").GetDouble(), glyphs[index].Advance, 10);
            }
        }
        byte[] png = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        using MemoryStream stream = new();
        await prepared.ConvertToPngAsync(stream, CancellationToken.None);
        Assert.Equal(png, stream.ToArray());
        RgbaImage actual = PngRgbaDecoder.Decode(png, 240 * 240);
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement proof = recipe.GetProperty("platforms").GetProperty(platform);
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(proof, "reference")));
            string digest = Convert.ToHexStringLower(SHA256.HashData(reference));
            Assert.Equal(Required(proof, "referenceSha256"), digest);
            Assert.True(proof.GetProperty("inlineExact").GetBoolean());
            Assert.Equal(3, proof.GetProperty("captures").GetArrayLength());
            foreach (JsonElement capture in proof.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal(digest, Required(capture, "pngSha256"));
                Assert.Equal("healthy", Required(capture, "startupHealth"));
                Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            }
            Assert.Equal(4, proof.GetProperty("differentProgramChangedPixels").EnumerateObject().Count());
            foreach (JsonProperty control in proof.GetProperty("differentProgramChangedPixels").EnumerateObject()) Assert.True(control.Value.GetInt32() > 0);
            if (platform == (OperatingSystem.IsWindows() ? "windows" : "linux"))
            {
                PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 240 * 240), actual);
                Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
            }
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    private sealed class FontProvider : ISvgFontProgramSourceProvider
    {
        internal List<string> Acquisitions { get; } = [];
        /// <summary>Owns immutable public program bytes through the serial operation; honors cancellation and has no shared cache or network access.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Acquisitions.Add(file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, File.ReadAllBytes(Path.Combine(DirectoryPath, file.RelativePath))));
        }
    }
}
