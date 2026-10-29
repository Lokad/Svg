using System.Text.Json;
using System.Security.Cryptography;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgInstancePaintOrderReferenceTests
{
    [Theory]
    [InlineData("pattern-fill")]
    [InlineData("pattern-stroke")]
    [InlineData("linear-fill")]
    [InlineData("linear-stroke")]
    [InlineData("radial-fill")]
    [InlineData("radial-stroke")]
    public void Each_resource_order_matches_its_independent_browser_image(string id)
    {
        string folder = Path.Combine(ResearchCorpus.Root, "forward-instance-paints");
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        foreach (string kind in new[] { "candidate", "control" })
        {
            JsonElement item = evidence.RootElement.GetProperty("cases").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id + "-" + kind);
            string inputPath = item.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing input path.");
            string referencePath = item.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.");
            byte[] source = File.ReadAllBytes(Path.Combine(folder, inputPath));
            byte[] reference = File.ReadAllBytes(Path.Combine(folder, referencePath));
            Assert.Equal(item.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(source)));
            Assert.Equal(item.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
            byte[] actual = SvgConverter.ConvertToPng(source, SvgRenderOptions.Default, CancellationToken.None).ToArray();
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1024), PngRgbaDecoder.Decode(actual, 1024)).IsExact);
        }
    }
}
