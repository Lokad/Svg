using System.Globalization;
using System.Text.Json;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgEndpointArcTests
{
    [Theory]
    [InlineData("quarter")]
    [InlineData("half")]
    [InlineData("ellipse-half")]
    [InlineData("large")]
    [InlineData("rotated")]
    [InlineData("rotated-large")]
    [InlineData("rotated-negative")]
    [InlineData("radius-correction")]
    [InlineData("negative-radius")]
    [InlineData("zero-radius")]
    [InlineData("same-end")]
    [InlineData("tiny-angle")]
    public void Endpoint_conics_match_original_native_float_vectors(string id)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "endpoint-arc-native.json")));
        JsonElement item = metadata.RootElement.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        double[] p = item.GetProperty("p").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        SvgEndpointConic[] output = new SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
        int count = SvgEndpointArc.Convert(new(p[0], p[1]), p[2], p[3], p[4], p[5] != 0, p[6] != 0, new(p[7], p[8]), output, CancellationToken.None);
        string[][] rows = File.ReadAllLines(Path.Combine(directory, "endpoint-arc-native.csv")).Skip(1).Select(line => line.Split(',')).Where(row => row[0] == id).ToArray();
        Assert.Equal(2 * count, rows.Length);
        for (int index = 0; index < count; index++)
        {
            AssertPoint(rows[2 * index], output[index].Control, output[index].Weight);
            AssertPoint(rows[2 * index + 1], output[index].End, output[index].Weight);
        }
        if (count > 0) Assert.Equal(new SvgDevicePoint((float)p[7], (float)p[8]), output[count - 1].End);

        static void AssertPoint(string[] row, SvgDevicePoint point, float weight)
        {
            Assert.Equal(uint.Parse(row[3], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)point.X));
            Assert.Equal(uint.Parse(row[4], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)point.Y));
            Assert.Equal(uint.Parse(row[5], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits(weight));
        }
    }

    [Fact]
    public void Cancellation_preserves_the_token_and_leaves_output_untouched()
    {
        SvgEndpointConic[] output = new SvgEndpointConic[3];
        using CancellationTokenSource source = new();
        source.Cancel();
        OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => SvgEndpointArc.Convert(new(2, 8), 6, 6, 0, true, false, new(14, 8), output, source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.All(output, item => Assert.Equal(default, item));
    }

    [Fact]
    public void Small_caller_buffer_is_rejected_before_construction()
    {
        SvgEndpointConic[] output = new SvgEndpointConic[2];
        Assert.Throws<ArgumentException>(() => SvgEndpointArc.Convert(new(2, 8), 6, 6, 0, true, false, new(14, 8), output, CancellationToken.None));
        Assert.All(output, item => Assert.Equal(default, item));
    }

    [Theory]
    [InlineData(double.PositiveInfinity, 6)]
    [InlineData(double.NaN, 6)]
    [InlineData(1e300, 6)]
    [InlineData(1e30, 6)]
    [InlineData(1e-30, 6)]
    public void Nonfinite_float_construction_is_a_limit_error(double radiusX, double radiusY)
    {
        SvgEndpointConic[] output = new SvgEndpointConic[3];
        SvgException error = Assert.Throws<SvgException>(() => SvgEndpointArc.Convert(new(2, 8), radiusX, radiusY, 0, true, false, new(14, 8), output, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.All(output, item => Assert.Equal(default, item));
    }
}
