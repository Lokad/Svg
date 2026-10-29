using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgMaskClipReferenceTests
{
    [Theory]
    [InlineData("inherited-single-partial-0")]
    [InlineData("inherited-single-shader-1")]
    [InlineData("inherited-single-shader-2")]
    [InlineData("inherited-overlap-3")]
    [InlineData("inherited-solid-stroke-4")]
    [InlineData("inherited-shader-stroke-5")]
    [InlineData("inherited-single-opaque-6")]
    [InlineData("inherited-single-shader-7")]
    [InlineData("own-single-partial-8")]
    [InlineData("own-single-shader-9")]
    [InlineData("own-single-shader-10")]
    [InlineData("own-overlap-11")]
    [InlineData("own-solid-stroke-12")]
    [InlineData("own-shader-stroke-13")]
    [InlineData("own-single-opaque-14")]
    [InlineData("own-single-shader-15")]
    public async Task Mask_clips_match_each_actual_browser_image(string id)
    {
        string folder = Path.Combine(ResearchCorpus.Root, "mask-clip-isolation");
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
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), PngRgbaDecoder.Decode(raw, 1_048_576)).IsExact);
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new NoFonts(), SvgRenderOptions.Default, CancellationToken.None);
            Assert.Equal(raw, prepared.ConvertToPng(CancellationToken.None).ToArray());
        }
    }

    private sealed class NoFonts : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Font-free mask clip groups must not request a font program.");
    }
}
