using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgOffsetMergeInputReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "capture.json")));
        foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case id.")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Offset_merge_inputs_match_each_native_image(string id)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "capture.json")));
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.")));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.")));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(row.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        byte[] actual = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1024), PngRgbaDecoder.Decode(actual, 1024)).IsExact, id);
    }

    private static string Folder() => Path.Combine(AppContext.BaseDirectory, "Research", "offset-merge-inputs");
}
