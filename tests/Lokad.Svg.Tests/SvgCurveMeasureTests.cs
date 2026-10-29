using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgCurveMeasureTests
{
    [Theory]
    [InlineData(0, 0, 3, 4)]
    [InlineData(1, 0, 3, 4)]
    [InlineData(1, .5f, 3, 4)]
    [InlineData(2, 0, 3, 4)]
    [InlineData(2, 1, 6, 8)]
    [InlineData(3, 0, 3, 4)]
    [InlineData(3, 1, 3, 4)]
    public void Tangents_preserve_endpoint_fallbacks_and_rational_direction(int kind, float parameter, double x, double y)
    {
        SvgDevicePoint[] points = [new(0, 0), new(0, 0), new(3, 4), new(6, 8)];
        if (kind == 0) points[1] = points[2];
        SvgDevicePoint tangent = SvgCurveMeasure.Tangent((SvgMeasuredCurveKind)kind, points, 2, parameter, CancellationToken.None);
        Assert.Equal(new(x, y), tangent);
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        Assert.Equal(cancelled.Token, Assert.Throws<OperationCanceledException>(() => SvgCurveMeasure.Tangent((SvgMeasuredCurveKind)kind, points, 2, parameter, cancelled.Token)).CancellationToken);
    }

    [Fact]
    public void Curve_distances_knots_and_parameters_match_compiled_native_float_bits()
    {
        Span<SvgCurveMeasureStep> steps = stackalloc SvgCurveMeasureStep[SvgCurveMeasure.MaximumStepCount];
        int count = 0, maximumSteps = 0;
        foreach (string[] fields in NativeRows())
        {
            SvgMeasuredCurveKind kind = (SvgMeasuredCurveKind)int.Parse(fields[1], CultureInfo.InvariantCulture);
            float weight = Number(fields[2]), scale = Number(fields[3]), initial = Number(fields[4]);
            SvgDevicePoint[] points = Points(fields[5]);
            int actualCount = SvgCurveMeasure.Measure(kind, points, weight, scale, initial, steps, out float distance,
CancellationToken.None);
            EqualBits(fields[0] + " distance", Bits(fields[6]), distance);
            string[] expected = fields[7].Split(';', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(expected.Length, actualCount);
            for (int at = 0; at < actualCount; at++)
            {
                string[] values = expected[at].Split(':');
                EqualBits(fields[0] + " step " + at, Bits(values[0]), steps[at].Distance);
                Assert.Equal(int.Parse(values[1], CultureInfo.InvariantCulture), steps[at].Parameter);
            }
            foreach (string lookup in fields[8].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] values = lookup.Split(':');
                float parameter = SvgCurveMeasure.ParameterAt(steps[..actualCount], initial,
                    BitConverter.UInt32BitsToSingle(Bits(values[0])), CancellationToken.None);
                EqualBits(fields[0] + " parameter " + values[0], Bits(values[1]), parameter);
            }
            maximumSteps = Math.Max(maximumSteps, actualCount);
            count++;
        }
        Assert.Equal(480, count);
        Assert.Equal(SvgCurveMeasure.MaximumStepCount, maximumSteps);
    }

    [Fact]
    public void Curve_cut_moves_controls_endpoints_and_weights_match_native_verbs()
    {
        Span<SvgDevicePoint> cut = stackalloc SvgDevicePoint[4];
        int count = 0;
        foreach (string[] fields in NativeRows())
        {
            SvgMeasuredCurveKind kind = (SvgMeasuredCurveKind)int.Parse(fields[1], CultureInfo.InvariantCulture);
            SvgDevicePoint[] points = Points(fields[5]);
            foreach (string expected in fields[9].Split(';'))
            {
                uint[] values = expected.Split(':').Select(Bits).ToArray();
                bool valid = SvgCurveMeasure.Cut(kind, points, Number(fields[2]), BitConverter.UInt32BitsToSingle(values[0]),
                    BitConverter.UInt32BitsToSingle(values[1]), cut, out SvgMeasuredCurveKind resultKind, out float resultWeight,
CancellationToken.None);
                Assert.Equal(values[2] != 0, valid);
                if (valid)
                {
                    Assert.Equal((SvgMeasuredCurveKind)values[3], resultKind);
                    if (resultKind == SvgMeasuredCurveKind.Conic) EqualBits(fields[0] + " weight", values[4], resultWeight);
                    int pointCount = (values.Length - 5) / 2;
                    for (int at = 0; at < pointCount; at++)
                    {
                        EqualBits(fields[0] + " cut " + expected + " X " + at, values[5 + at * 2], (float)cut[at].X);
                        EqualBits(fields[0] + " cut " + expected + " Y " + at, values[6 + at * 2], (float)cut[at].Y);
                    }
                }
                count++;
            }
        }
        Assert.Equal(2880, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Measurement_and_cut_preserve_cancellation_and_allow_retry(int curveKind)
    {
        SvgMeasuredCurveKind kind = (SvgMeasuredCurveKind)curveKind;
        SvgDevicePoint[] points = [new(6, 40), new(-12, -12), new(76, 80), new(58, 12)];
        SvgCurveMeasureStep[] steps = new SvgCurveMeasureStep[SvgCurveMeasure.MaximumStepCount];
        SvgDevicePoint[] cut = [new(-1, -1), new(-1, -1), new(-1, -1), new(-1, -1)];
        Assert.All(steps, v => Assert.Equal(default, v));
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        OperationCanceledException measured = Assert.ThrowsAny<OperationCanceledException>(() => Measure(cancelled.Token));
        Assert.Equal(cancelled.Token, measured.CancellationToken);
        Assert.All(cut, v => Assert.Equal(new SvgDevicePoint(-1, -1), v));
        OperationCanceledException chopped = Assert.ThrowsAny<OperationCanceledException>(() => Cut(cancelled.Token));
        Assert.Equal(cancelled.Token, chopped.CancellationToken);
        Assert.True(Measure(CancellationToken.None) > 0);
        Assert.True(Cut(CancellationToken.None));
        Assert.NotEqual(new SvgDevicePoint(-1, -1), cut[0]);

        int Measure(CancellationToken token) => SvgCurveMeasure.Measure(kind, points, .75f, 1, 0, steps, out _, token);
        bool Cut(CancellationToken token) => SvgCurveMeasure.Cut(kind, points, .75f, .25f, .75f, cut, out _, out _, token);
    }

    [Fact]
    public void Short_distance_buffer_denies_before_overwriting_and_zero_lengths_have_no_knots()
    {
        SvgDevicePoint[] points = [new(6, 40), new(-12, -12), new(76, 80), new(58, 12)];
        SvgCurveMeasureStep[] steps = [default, new(-1, -1)];
        SvgException denied = Assert.Throws<SvgException>(() => SvgCurveMeasure.Measure(SvgMeasuredCurveKind.Cubic, points, 1, 4, 0,
            steps.AsSpan(0, 1), out _, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
        Assert.Equal(new SvgCurveMeasureStep(-1, -1), steps[1]);
        SvgDevicePoint[] degenerate = [new(8, 8), new(8, 8), new(8, 8), new(8, 8)];
        foreach (SvgMeasuredCurveKind kind in Enum.GetValues<SvgMeasuredCurveKind>())
        {
            Assert.Equal(0, SvgCurveMeasure.Measure(kind, degenerate, 1, 1, 0, steps, out float distance, CancellationToken.None));
            Assert.Equal(0, distance);
        }
        // A short curve cannot advance a large float accumulator; retain the native no-record rule.
        SvgDevicePoint[] tiny = [new(0, 0), new(.0001f, 0)];
        Assert.Equal(0, SvgCurveMeasure.Measure(SvgMeasuredCurveKind.Line, tiny, 1, 1, 100_000, steps, out float unchanged, CancellationToken.None));
        Assert.Equal(100_000, unchanged);
    }

    private static IEnumerable<string[]> NativeRows()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/curve-measure-native.csv.gz"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/curve-measure-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        using MemoryStream compressed = new(data);
        using GZipStream expanded = new(compressed, CompressionMode.Decompress);
        using StreamReader reader = new(expanded);
        Assert.Equal("id,kind,weight,resolutionScale,initialDistance,coordinates,distanceBits,steps,parameters,cuts", reader.ReadLine());
        while (reader.ReadLine() is string row) yield return row.Split(',');
    }

    private static SvgDevicePoint[] Points(string encoded)
    {
        float[] values = encoded.Split(':').Select(Number).ToArray();
        return Enumerable.Range(0, values.Length / 2).Select(i => new SvgDevicePoint(values[2 * i], values[2 * i + 1])).ToArray();
    }

    private static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    private static uint Bits(string value) => uint.Parse(value, CultureInfo.InvariantCulture);
    private static void EqualBits(string identity, uint expected, float actual) =>
        Assert.True(expected == BitConverter.SingleToUInt32Bits(actual), identity + ": expected " + expected + ", actual " + BitConverter.SingleToUInt32Bits(actual));
}
