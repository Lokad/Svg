using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgMarkerPaintBindingReferenceTests
{
    [Theory]
    [InlineData("linear-fill-rect-source-direct-plain")]
    [InlineData("radial-fill-circle-marker-direct-plain")]
    [InlineData("pattern-stroke-path-group-alias-plain")]
    [InlineData("pattern-fill-path-marker-direct-opacity")]
    [InlineData("linear-fill-rect-marker-direct-filter")]
    [InlineData("radial-stroke-circle-source-direct-clip")]
    [InlineData("pattern-fill-path-source-direct-translate")]
    [InlineData("linear-fill-rect-marker-direct-override")]
    public async Task Marker_paint_orders_match_their_own_browser_images(string id)
    {
        string folder = Path.Combine(ResearchCorpus.Root, "marker-paint-binding");
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes("{\"packId\":\"empty\",\"files\":[{\"relativePath\":\"unused.ttf\",\"byteSize\":1,\"sha256\":\"" + new string('0', 64) + "\"}],\"families\":[{\"requestedFamily\":\"Unused\",\"resolvedFamily\":\"Unused\",\"relativeFontFile\":\"unused.ttf\"}]}"), "empty");
        foreach (string kind in new[] { "candidate", "control" })
        {
            JsonElement item = evidence.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id + "-" + kind);
            string inputPath = item.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing input path.");
            string referencePath = item.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.");
            byte[] input = File.ReadAllBytes(Path.Combine(folder, inputPath));
            byte[] reference = File.ReadAllBytes(Path.Combine(folder, referencePath));
            Assert.Equal(item.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
            Assert.Equal(item.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
            byte[] raw = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1024), PngRgbaDecoder.Decode(raw, 1024)).IsExact);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(raw, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    private sealed class NoFonts : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Font-free marker content must not request a font program.");
    }
}
