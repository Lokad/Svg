using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFilterBilinearSamplingTests
{
    public static IEnumerable<object[]> FilterCases() => Cases(false);
    public static IEnumerable<object[]> ViewCases() => Cases(true);

    private static IEnumerable<object[]> Cases(bool view)
    {
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "TestData", "highp-bilinear.csv")))
        {
            string[] fields = line.Split(',');
            int edge = int.Parse(fields[1], CultureInfo.InvariantCulture);
            if ((edge >= 16) != view) continue;
            yield return [fields[0], edge, BitConverter.UInt32BitsToSingle(uint.Parse(fields[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                byte.Parse(fields[4], CultureInfo.InvariantCulture)];
        }
    }

    [Theory]
    [MemberData(nameof(FilterCases))]
    [Trait("Layer", "Mechanism")]
    public void Filter_coordinates_retain_native_weight_rounding(string axis, int edge, float coordinate, byte expectedAlpha)
    {
        int width = axis == "x" ? edge + 3 : 4, height = axis == "y" ? edge + 3 : 4;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if ((axis == "x" ? x : y) == edge) pixels.AsSpan((y * width + x) * 4, 4).Fill(255);
        SvgShaderPaint sample = SvgShaderPaint.SampleAffine(pixels, width, height,
            axis == "x" ? coordinate : 1.5f, axis == "y" ? coordinate : 1.5f, new(0, 0), 1);
        byte[] actual = new byte[4];
        sample.Blend(actual, 255);
        Assert.Equal(new byte[] { expectedAlpha, expectedAlpha, expectedAlpha, expectedAlpha }, actual);
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    [Trait("Layer", "Mechanism")]
    public void Image_views_share_native_weights_without_losing_parent_stride(string axis, int edge, float coordinate, byte expectedAlpha)
    {
        int width = axis == "x" ? edge + 3 : 4, height = axis == "y" ? edge + 3 : 4, stride = width + 2;
        byte[] pixels = new byte[stride * (height + 2) * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if ((axis == "x" ? x : y) == edge) pixels.AsSpan(((y + 1) * stride + x + 1) * 4, 4).Fill(255);
        SvgShaderPaint sample = SvgShaderPaint.SampleView(pixels, stride, new(1, 1, width + 1, height + 1),
            axis == "x" ? coordinate : 1.5f, axis == "y" ? coordinate : 1.5f, 1);
        byte[] actual = new byte[4];
        sample.Blend(actual, 255);
        Assert.Equal(new byte[] { expectedAlpha, expectedAlpha, expectedAlpha, expectedAlpha }, actual);
    }

    [Fact]
    [Trait("Layer", "Mechanism")]
    public void Clamped_views_keep_edge_pixels_inside_the_source_rectangle()
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 4 * 4 * 4).ToArray();
        for (int y = 1; y < 3; y++)
            for (int x = 1; x < 3; x++)
                new byte[] { 32, 64, 96, 128 }.CopyTo(pixels, (y * 4 + x) * 4);
        SvgShaderPaint sample = SvgShaderPaint.SampleView(pixels, 4, new(1, 1, 3, 3), -2, 200, 1);
        Assert.Equal(new SvgConstantShaderPaint(32, 64, 96, 128), sample.AsConstant(1));
    }

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Sampling_vectors_retain_independent_native_capture_identity()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "highp-bilinear.csv"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "highp-bilinear.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(16, File.ReadLines(Path.Combine(directory, "highp-bilinear.csv")).Count());
        JsonElement captures = metadata.RootElement.GetProperty("captures");
        Assert.Equal(6, captures.GetArrayLength());
        Assert.All(captures.EnumerateArray(), capture =>
        {
            Assert.Equal("healthy", capture.GetProperty("startupHealth").GetString());
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            Assert.Equal(metadata.RootElement.GetProperty("vectorsSha256").GetString(), capture.GetProperty("vectorsSha256").GetString());
        });
    }
}
