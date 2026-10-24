using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgInheritedClipReferenceTests
{
    [Theory]
    [InlineData("rectangles-fractional-p5-0")]
    [InlineData("solid-stroke-fractional-p5-1")]
    [InlineData("shader-stroke-fractional-p5-2")]
    [InlineData("paths-fractional-p5-3")]
    [InlineData("rectangles-fractional-p75-4")]
    [InlineData("shader-stroke-integer-p5-5")]
    [InlineData("paths-half-p5-6")]
    [InlineData("rectangles-contained-p5-7")]
    [InlineData("rectangles-plain-direct-8")]
    [InlineData("rectangles-mask-direct-9")]
    [InlineData("rectangles-nested-opacity-use-10")]
    [InlineData("paths-own-clip-direct-11")]
    [InlineData("paths-plain-nested-svg-12")]
    [InlineData("solid-stroke-own-clip-use-13")]
    [InlineData("shader-stroke-own-clip-symbol-14")]
    [InlineData("shader-stroke-plain-direct-15")]
    [InlineData("rectangles-mask-symbol-16")]
    [InlineData("paths-nested-opacity-symbol-17")]
    [InlineData("solid-stroke-mask-direct-18")]
    [InlineData("shader-stroke-nested-opacity-direct-19")]
    public async Task Inherited_clips_match_each_actual_browser_image(string id)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Research", "inherited-clip-isolation");
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
            => throw new InvalidOperationException("Font-free clip composition must not request a font program.");
    }
}
