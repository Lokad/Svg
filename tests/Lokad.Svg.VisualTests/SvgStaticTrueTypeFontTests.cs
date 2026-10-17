using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgStaticTrueTypeFontTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "static-truetype-fonts");

    [Theory]
    [InlineData("liberation")]
    [InlineData("dejavu")]
    public async Task Public_static_program_selection_and_layout_match_independent_metrics(string id)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == id);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "font")));
        Assert.Equal(Required(item, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "fontPack"))), Required(item, "packId"));
        FontProvider provider = new(Required(item, "relativeFontFile"), bytes);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        Assert.Equal(1, provider.Acquisitions);
        Assert.Equal(Required(item, "family"), font.Resolution.Resolution.Face.ResolvedFamily);
        Assert.Equal(Required(item, "relativeFontFile"), font.Resolution.Resolution.Face.File.RelativePath);
        Assert.False(font.Resolution.Resolution.IsFallback);

        using JsonDocument tableDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-tables.json")));
        JsonElement tables = tableDocument.RootElement.EnumerateArray().Single(c => Required(c, "id") == id);
        Assert.Equal(tables.GetProperty("unitsPerEm").GetInt32(), (int)font.UnitsPerEm);
        foreach (JsonElement sample in tables.GetProperty("runs").EnumerateArray())
        {
            uint scalar = Convert.ToUInt32(Required(sample, "scalar")[2..], 16);
            ushort glyph = OpenTypeFont.GetGlyphIndex(font.Bytes, scalar, font.Metrics, CancellationToken.None);
            Assert.Equal(sample.GetProperty("advance").GetInt32(), (int)OpenTypeFont.GetAdvanceWidth(font.Bytes, glyph, font.Metrics, CancellationToken.None));
        }
        using JsonDocument metricsDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "metrics.json")));
        JsonElement metrics = metricsDocument.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == id);
        JsonElement expected = metrics.GetProperty("glyphs");
        foreach ((double size, double y) in new[] { (24d, 44.5), (48d, 112.25) })
        {
            SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, Required(metrics, "text"), size, 12.25, y, CancellationToken.None);
            Assert.Equal(expected.GetArrayLength(), glyphs.Length);
            double x = 12.25;
            for (int index = 0; index < glyphs.Length; index++)
            {
                Assert.Equal(expected[index].GetProperty("glyphIndex").GetUInt16(), glyphs[index].GlyphIndex);
                Assert.Equal(x + expected[index].GetProperty("offsetX").GetDouble() * size / font.UnitsPerEm, glyphs[index].X, 10);
                Assert.Equal(y - expected[index].GetProperty("offsetY").GetDouble() * size / font.UnitsPerEm, glyphs[index].Y, 10);
                x += expected[index].GetProperty("advance").GetDouble() * size / font.UnitsPerEm;
            }
            Assert.Equal(x, glyphs[^1].X + glyphs[^1].Advance, 10);
        }
    }

    [Theory]
    [InlineData("liberation")]
    [InlineData("dejavu")]
    public async Task Public_static_document_matches_frozen_pixels_through_direct_and_prepared_output(string id)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == id);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "font")));
        byte[] original = input.ToArray();
        Assert.Equal(Required(item, "inputSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "fontPack"))), Required(item, "packId"));
        FontProvider provider = new(Required(item, "relativeFontFile"), bytes);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        using MemoryStream stream = new();
        Assert.Equal(png.Length, await prepared.ConvertToPngAsync(stream, CancellationToken.None));
        Assert.Equal(png, stream.ToArray());
        Assert.Equal(2, provider.Acquisitions);
        Assert.Equal(original, input);
        RgbaImage actual = PngRgbaDecoder.Decode(png, 640 * 144);
        Assert.Equal(640, actual.Width); Assert.Equal(144, actual.Height);
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement proof = item.GetProperty("platforms").GetProperty(platform);
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(proof, "reference")));
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(reference));
            JsonElement captures = proof.GetProperty("captures");
            Assert.Equal(3, captures.GetArrayLength());
            foreach (JsonElement capture in captures.EnumerateArray())
            {
                Assert.Equal(sha256, Required(capture, "pngSha256"));
                Assert.Equal("healthy", Required(capture, "startupHealth"));
                Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            }
            if (platform == (OperatingSystem.IsWindows() ? "windows" : "linux"))
                Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 640 * 144), actual).IsExact);
        }
    }

    private static string Required(JsonElement item, string name) => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    /// <summary>Returns one explicitly named immutable borrowed program without I/O. Observes cancellation and permits concurrent requests; acquisition counting is atomic.</summary>
    private sealed class FontProvider(string filename, byte[] bytes) : ISvgFontProgramSourceProvider
    {
        private int _acquisitions;
        internal int Acquisitions => Volatile.Read(ref _acquisitions);

        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(filename, file.RelativePath);
            Interlocked.Increment(ref _acquisitions);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(filename, bytes));
        }
    }
}
