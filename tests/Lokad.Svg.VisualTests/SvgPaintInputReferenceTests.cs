using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPaintInputReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string folder in new[] { "offset-merge-inputs", "linear-shape-source-maps", "radial-axis-source-maps" })
        {
            using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(folder), "capture.json")));
            foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
                yield return [folder, row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case id.")];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Paint_inputs_match_each_native_image(string folder, string id)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(folder), "capture.json")));
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(folder), row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.")));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(folder), row.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.")));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(row.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        SvgRenderOptions options = row.TryGetProperty("scale", out JsonElement scale)
            ? new(null, scale.GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, null)
            : SvgRenderOptions.Default;
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 4096), PngRgbaDecoder.Decode(actual, 4096)).IsExact, id);
    }

    private static string Folder(string name) => Path.Combine(AppContext.BaseDirectory, "Research", name);
}
