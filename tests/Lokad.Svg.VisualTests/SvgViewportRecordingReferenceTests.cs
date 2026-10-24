using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgViewportRecordingReferenceTests
{
    [Theory]
    [InlineData("linear-outer")]
    [InlineData("linear-inner")]
    [InlineData("linear-own")]
    [InlineData("radial-outer")]
    [InlineData("pattern-scaled")]
    [InlineData("leaf-linear")]
    [InlineData("nested-linear")]
    [InlineData("affine-pattern")]
    public async Task Viewport_recording_matches_each_native_image(string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Research", "viewport-recording");
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes("{\"packId\":\"empty\",\"files\":[{\"relativePath\":\"unused.ttf\",\"byteSize\":1,\"sha256\":\"" + new string('0', 64) + "\"}],\"families\":[{\"requestedFamily\":\"Unused\",\"resolvedFamily\":\"Unused\",\"relativeFontFile\":\"unused.ttf\"}]}"), "empty");
        foreach (string kind in new[] { "candidate", "control" })
        {
            JsonElement item = evidence.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id + "-" + kind);
            string source = item.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.");
            string target = item.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.");
            byte[] input = File.ReadAllBytes(Path.Combine(folder, source));
            byte[] reference = File.ReadAllBytes(Path.Combine(folder, target));
            Assert.Equal(item.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
            Assert.Equal(item.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
            SvgRenderOptions options = new(null, item.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default);
            byte[] raw = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), PngRgbaDecoder.Decode(raw, 1_048_576)).IsExact, id + " " + kind);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), options, CancellationToken.None);
            Assert.Equal(raw, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    private sealed class NoFonts : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Font-free viewport references must not request a font program.");
    }
}
