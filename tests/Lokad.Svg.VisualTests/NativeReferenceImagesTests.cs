using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Integrity")]
public sealed class NativeReferenceImagesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shared_path_cannot_hide_an_incorrect_later_hash_and_valid_reads_recover(bool warm)
    {
        string folder = Path.Combine(ResearchCorpus.Root, "ordered-clip-coverage");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "capture.json")));
        JsonElement row = capture.RootElement.GetProperty("cases")[0];
        JsonElement native = row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        string path = Path.Combine(folder, native.GetProperty("reference").GetString() ?? throw new InvalidDataException());
        byte[] bytes = File.ReadAllBytes(path);
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        NativeReferenceImages images = new();
        if (warm) images.Get(path, hash);

        Assert.Throws<InvalidDataException>(() => images.Get(path, new string('0', 64)));
        RgbaImage actual = images.Get(path, hash);
        RgbaImage expected = PngRgbaDecoder.Decode(bytes, 16_384);
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        Assert.Equal(expected.Pixels, actual.Pixels);
    }
}
