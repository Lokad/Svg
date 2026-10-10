using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgGaussianBlurReferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "filter-blur-kernel", "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Premultiplied_blur_kernel_matches_frozen_browser_control(string identity)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "filter-blur-kernel");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(3, root.GetProperty("freshProcessCaptures").GetInt32());
        Assert.Equal("healthy", Required(root, "captureStartupHealth"));
        Assert.True(root.GetProperty("captureProfilesCleaned").GetBoolean());
        Assert.True(root.GetProperty("linuxReferenceQualification").GetProperty("byteIdenticalToFrozenReferences").GetBoolean());
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        Assert.Equal("blur-kernel-control", Required(item, "role"));
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
        Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        byte[] png = File.ReadAllBytes(Path.Combine(directory, Required(item, "reference")));
        Assert.Equal(Required(item, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(png)));
        int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
        RgbaImage reference = PngRgbaDecoder.Decode(png, checked(width * height));
        Assert.Equal((width, height), (reference.Width, reference.Height));
        byte[] expected = (byte[])reference.Pixels.Clone();
        for (int offset = 0; offset < expected.Length; offset += 4)
            for (int channel = 0; channel < 3; channel++)
                expected[offset + channel] = (byte)((expected[offset + channel] * expected[offset + 3] + 127) / 255);

        int[] rectangle = item.GetProperty("sourceRectangle").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        byte[] color = item.GetProperty("sourceRgba").EnumerateArray().Select(value => value.GetByte()).ToArray();
        byte[] source = new byte[checked(width * height * 4)];
        for (int y = rectangle[1]; y < rectangle[1] + rectangle[3]; y++)
            for (int x = rectangle[0]; x < rectangle[0] + rectangle[2]; x++)
                color.CopyTo(source.AsSpan((y * width + x) * 4, 4));
        float[] deviation = item.GetProperty("deviation").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        byte[] destination = new byte[source.Length];
        RenderBudget budget = new(1_000_000);
        long work = SvgGaussianBlur.Apply(source, destination, width, height, new(deviation[0], deviation[1]), budget, 1_000_000, CancellationToken.None);
        Assert.True(work > 0);
        Assert.Equal(expected, destination);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static string Required(JsonElement item, string name)
        => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
}
