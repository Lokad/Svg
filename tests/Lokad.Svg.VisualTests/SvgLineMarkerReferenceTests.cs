using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgLineMarkerReferenceTests
{
    [Theory]
    [InlineData("r84-line-0")]
    [InlineData("r84-line-1")]
    [InlineData("r84-expanded-2")]
    [InlineData("r84-expanded-3")]
    [InlineData("r84-effects-4")]
    [InlineData("r84-effects-5")]
    [InlineData("r84-effects-6")]
    [InlineData("r84-effects-7")]
    [InlineData("r84-encoded-8")]
    [InlineData("r84-encoded-9")]
    public async Task Line_marker_inputs_match_their_own_browser_images(string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Research", "line-marker-instances");
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
