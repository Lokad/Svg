using System.Globalization;
using System.Runtime.CompilerServices;
using TextEncoding = System.Text.Encoding;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgConicTests
{
    [Theory]
    [InlineData("circle-r2")]
    [InlineData("ellipse-wide")]
    [InlineData("large-r409")]
    [InlineData("quarter-reversed")]
    [InlineData("offset-collapse")]
    [InlineData("tiny-quarter")]
    [InlineData("weight-one")]
    [InlineData("semicircle-zero-weight")]
    [InlineData("depth-five")]
    [InlineData("near-monotonic")]
    [InlineData("half-circle-44-q0")]
    [InlineData("half-circle-44-q1")]
    [InlineData("half-circle-44-q2")]
    [InlineData("half-circle-44-q3")]
    [InlineData("half-circle-100-q0")]
    [InlineData("half-circle-100-q1")]
    [InlineData("half-circle-100-q2")]
    [InlineData("half-circle-100-q3")]
    [InlineData("half-circle-156-q0")]
    [InlineData("half-circle-156-q1")]
    [InlineData("half-circle-156-q2")]
    [InlineData("half-circle-156-q3")]
    [InlineData("finite-overflow-positive")]
    [InlineData("finite-overflow-negative")]
    [InlineData("finite-overflow-asymmetric")]
    public void Original_compiled_legacy_conic_subdivision_matches_every_float_bit(string id)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "conic-native.json")));
        JsonElement data = metadata.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        double[] p = data.GetProperty("parameters").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        string[][] rows = File.ReadAllLines(Path.Combine(directory, "conic-native.csv")).Skip(1)
            .Select(line => line.Split(',')).Where(row => row[0] == id).ToArray();
        Span<SvgDevicePoint> output = stackalloc SvgDevicePoint[SvgRationalQuadratic.MaximumPointCount];
        int count = SvgRationalQuadratic.Convert(new(p[0], p[1]), new(p[2], p[3]), new(p[4], p[5]), (float)p[6], (float)p[7], output, out int work, CancellationToken.None);
        Assert.Equal(2 * count + 1, rows.Length);
        Assert.InRange(work, 1, SvgRationalQuadratic.MaximumConversionWork);
        for (int index = 0; index < rows.Length; index++)
        {
            Assert.Equal(count, int.Parse(rows[index][2], CultureInfo.InvariantCulture));
            Assert.Equal(uint.Parse(rows[index][4], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)output[index].X));
            Assert.Equal(uint.Parse(rows[index][5], CultureInfo.InvariantCulture), BitConverter.SingleToUInt32Bits((float)output[index].Y));
        }
    }

    [Theory]
    [InlineData("<ellipse cx='32' cy='32' rx='24' ry='16' fill='red'/>")]
    [InlineData("<g transform='translate(.5 .25)'><circle cx='32' cy='32' r='16' fill='red'/></g>")]
    [InlineData("<defs><symbol id='s' viewBox='0 0 64 64'><ellipse cx='32' cy='32' rx='24' ry='16' fill='red'/></symbol></defs><use href='#s' width='64' height='64'/>")]
    public void Ovals_retain_four_rational_verbs_through_placement(string body)
    {
        using SvgRoot root = SvgRootParser.Parse(TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + body + "</svg>"), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        Assert.Equal(4, polygon.QuadSpans.Length);
        Assert.All(polygon.QuadSpans, span => Assert.Equal(SvgRationalQuadratic.QuarterCircleWeight, span.ConicWeight));
        Assert.Equal(polygon.Points[0], polygon.Points[^1]);
        Assert.Equal(32, Unsafe.SizeOf<SvgQuadSpan>());
    }

    [Theory]
    [InlineData("<circle cx='15.375' cy='16.625' r='11.125' fill='#d3264a'/>")]
    [InlineData("<ellipse cx='.125' cy='15.875' rx='19.625' ry='11.125' fill='red'/>")]
    [InlineData("<defs><radialGradient id='g'><stop stop-color='yellow'/><stop offset='1' stop-color='blue'/></radialGradient></defs><circle cx='16' cy='16' r='12' fill='url(#g)' fill-opacity='.61'/>")]
    public async Task Retained_conics_have_six_output_parity(string body)
    {
        byte[] input = TextEncoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='32' height='32'>" + body + "</svg>");
        byte[] expected = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        byte[] destination = new byte[expected.Length];
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream stream = new();
        Assert.Equal(expected.Length, SvgConverter.ConvertToPng(input, stream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, stream.ToArray());
        Assert.Equal(expected, (await SvgConverter.ConvertToPngAsync(input, SvgRenderOptions.Default, CancellationToken.None)).ToArray());
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, destination, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(expected.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, SvgRenderOptions.Default, CancellationToken.None));
        Assert.Equal(expected, asyncStream.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Live_convex_conics_cache_classification_and_release_on_denial_or_cancellation(bool clipped)
    {
        using SvgRoot root = SvgRootParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><ellipse cx='32' cy='32' rx='24' ry='16' fill='red'/></svg>"u8.ToArray(), SvgRenderLimits.Default, CancellationToken.None);
        SvgPolygonFill polygon = Assert.Single(root.Geometries).Polygon;
        SvgAnalyticContourPreparation preparation = new(polygon.Points, polygon.ContourEnds, [], false, null, polygon.Mapping, polygon.QuadSpans, [], new(8, 16, 56, 48), 64, 64);
        RenderBudget budget = new(1_000_000);
        SvgAnalyticClipContext? clip = clipped ? new(new(0, 0, 64, 64)) : null;
        long firstWork;
        using (SvgAnalyticCoverage first = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), clip, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out firstWork, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            Assert.True(preparation.Convex);
            Assert.Equal(9L, preparation.NativePointCount);
            first.PrepareRow(31, out long rowWork, CancellationToken.None);
            Assert.True(rowWork > 0);
        }
        long setup;
        using (SvgAnalyticCoverage cached = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), clip, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out setup, out bool eligible, CancellationToken.None))
        { Assert.True(eligible); Assert.True(setup < firstWork); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), clip, SvgAnalyticPaintClip.Binary, setup - 1, budget, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Conic setup must respect its remaining work allowance.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        RenderBudget tight = new(1);
        try
        {
            using SvgAnalyticCoverage denied = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), clip, SvgAnalyticPaintClip.Binary, 1_000_000, tight, out long _, out bool _, CancellationToken.None);
            Assert.Fail("Conic edge storage must be reserved before allocation.");
        }
        catch (SvgException exception) { Assert.Equal(SvgErrorCode.LimitExceeded, exception.ErrorCode); }
        Assert.Equal(0, tight.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            using SvgAnalyticCoverage canceled = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), clip, SvgAnalyticPaintClip.Binary, 1_000_000, budget, out long _, out bool _, cancellation.Token);
            Assert.Fail("Cached conics must still observe cancellation.");
        }
        catch (OperationCanceledException exception) { Assert.Equal(cancellation.Token, exception.CancellationToken); }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Conversion_observes_the_token_and_the_caller_buffer()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgRationalQuadratic.Convert(new(1, 0), new(1, 1), new(0, 1), SvgRationalQuadratic.QuarterCircleWeight, .25f, new SvgDevicePoint[65], out int _, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        SvgException denied = Assert.Throws<SvgException>(() => SvgRationalQuadratic.Convert(new(1, 0), new(1, 1), new(0, 1), SvgRationalQuadratic.QuarterCircleWeight, .25f, new SvgDevicePoint[2], out int _, CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, denied.ErrorCode);
    }

    [Fact]
    public void Float_collapsed_oval_bounds_enclose_no_area()
    {
        SvgContourBuilder builder = new(64, 1);
        builder.EnableQuadSpanRetention();
        builder.AddEllipse(new(100_000_000, 100_000_000), 1, 1, new(.25, 12, 64), CancellationToken.None);
        Assert.Equal(0, builder.ContourCount);
        Assert.Empty(builder.GetQuadSpans());
    }

    [Fact]
    public void Overflowed_oval_bounds_report_a_renderer_limit()
    {
        SvgContourBuilder builder = new(64, 1);
        builder.EnableQuadSpanRetention();
        SvgException error = Assert.Throws<SvgException>(() => builder.AddEllipse(new(2e38, 0), 2e38, 2e38, new(.25, 12, 64), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
    }
}
