using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgKerningPrecedenceTests
{
    [Theory]
    [InlineData("kerning-precedence", "public-kerning-precedence", 4)]
    [InlineData("kerning-flags", "public-kerning-flags", 7)]
    [InlineData("windows-font-span-extension", "public-windows-font-span-extension", 2)]
    public async Task Font_document_matches_independent_metrics_and_frozen_pixels(string directory, string packId, int fontCount)
    {
        string directoryPath = Path.Combine(ResearchCorpus.Root, directory);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directoryPath, "manifest.json")));
        JsonElement recipe = manifest.RootElement;
        int pixels = checked(recipe.GetProperty("width").GetInt32() * recipe.GetProperty("height").GetInt32());
        byte[] input = File.ReadAllBytes(Path.Combine(directoryPath, Required(recipe, "input")));
        Assert.Equal(Required(recipe, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directoryPath, "font-pack.json")), packId);
        FontProvider provider = new(directoryPath);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        Assert.Equal(fontCount, snapshot.Fonts.Count);
        Assert.Equal(fontCount, provider.Acquisitions.Count);
        Assert.Equal(fontCount, provider.Acquisitions.Distinct(StringComparer.Ordinal).Count());
        foreach (SvgPreparedFont font in snapshot.Fonts)
        {
            Assert.False(font.Resolution.Resolution.IsFallback);
            Assert.Equal(font.Resolution.Resolution.Request.Family, font.Resolution.Resolution.Face.ResolvedFamily);
            Assert.Equal("fonts/" + font.Resolution.Resolution.Request.Family + ".ttf", font.Resolution.Resolution.Face.File.RelativePath);
        }
        using JsonDocument metrics = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directoryPath, "metrics.json")));
        foreach (JsonElement row in metrics.RootElement.GetProperty("rows").EnumerateArray())
        {
            SvgPreparedFont font = snapshot.Fonts.Single(f => f.Resolution.Resolution.Request.Family == Required(row, "family"));
            Assert.Equal(Required(row, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(font.Bytes.Span)));
            string text = row.TryGetProperty("text", out JsonElement rowText) ? rowText.GetString() ?? throw new InvalidDataException("text") : "AV To Wa";
            SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, text, row.GetProperty("size").GetDouble(),
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
        RgbaImage actual = PngRgbaDecoder.Decode(png, pixels);
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement proof = recipe.GetProperty("platforms").GetProperty(platform);
            byte[] reference = File.ReadAllBytes(Path.Combine(directoryPath, Required(proof, "reference")));
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
            Assert.Equal(fontCount, proof.GetProperty("differentProgramChangedPixels").EnumerateObject().Count());
            foreach (JsonProperty control in proof.GetProperty("differentProgramChangedPixels").EnumerateObject()) Assert.InRange(control.Value.GetInt32(), 1, pixels);
            if (platform == (OperatingSystem.IsWindows() ? "windows" : "linux"))
            {
                PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, pixels), actual);
                Assert.True(comparison.IsExact, JsonSerializer.Serialize(comparison));
            }
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    private sealed class FontProvider(string directoryPath) : ISvgFontProgramSourceProvider
    {
        internal List<string> Acquisitions { get; } = [];
        /// <summary>Owns immutable public program bytes through the serial operation; honors cancellation and has no shared cache or network access.</summary>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Acquisitions.Add(file.RelativePath);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, File.ReadAllBytes(Path.Combine(directoryPath, file.RelativePath))));
        }
    }
}
