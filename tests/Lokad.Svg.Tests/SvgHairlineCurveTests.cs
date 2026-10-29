using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgHairlineCurveTests
{
    [Theory]
    [InlineData(0, 0, 0, 255)]
    [InlineData(235, 28, 35, 255)]
    [InlineData(235, 28, 35, 127)]
    public void Native_curve_and_cap_lines_preserve_coverage_order(byte red, byte green, byte blue, byte alpha)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/hairline-curves-native.csv.gz"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/hairline-curves-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        using MemoryStream compressed = new(data);
        using GZipStream expanded = new(compressed, CompressionMode.Decompress);
        using StreamReader reader = new(expanded);
        Assert.Equal("id,pointCount,cap,first,last,coordinates,lines", reader.ReadLine());
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(new(red, green, blue, alpha));
        int count = 0;
        while (reader.ReadLine() is string row)
        {
            string[] fields = row.Split(',');
            float[] coordinates = fields[5].Split(':').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            SvgDevicePoint[] points = Enumerable.Range(0, coordinates.Length / 2).Select(i => new SvgDevicePoint(coordinates[2 * i], coordinates[2 * i + 1])).ToArray();
            byte[] expected = new byte[64 * 64 * 4], actual = new byte[expected.Length];
            SvgHairlineCoverage reference = new(expected, 64, 64, paint, null, SvgClipAntialiasing.None, CancellationToken.None);
            foreach (string segment in fields[6].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                float[] p = segment.Split(':').Select(v => BitConverter.UInt32BitsToSingle(uint.Parse(v, CultureInfo.InvariantCulture))).ToArray();
                reference.DrawLine(new(p[0], p[1]), new(p[2], p[3]));
            }
            SvgHairlineCoverage candidate = new(actual, 64, 64, paint, null, SvgClipAntialiasing.None, CancellationToken.None);
            SvgStrokeCap cap = fields[2] == "0" ? SvgStrokeCap.Butt : fields[2] == "1" ? SvgStrokeCap.Round : SvgStrokeCap.Square;
            SvgHairlineCurves.ExtendCaps(points, cap, fields[3] == "1", fields[4] == "1", CancellationToken.None);
            if (points.Length == 3) SvgHairlineCurves.DrawQuadratic(ref candidate, points, CancellationToken.None);
            else SvgHairlineCurves.DrawCubic(ref candidate, points, CancellationToken.None);
            Assert.True(expected.AsSpan().SequenceEqual(actual), fields[0]);
            count++;
        }
        Assert.Equal(648, count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Curve_cancellation_precedes_publication_and_allows_retry(int kind)
    {
        byte[] pixels = new byte[64 * 64 * 4];
        Assert.All(pixels, v => Assert.Equal(0, v));
        using CancellationTokenSource cancelled = new(); cancelled.Cancel();
        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => Draw(pixels, kind, cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
        Array.Clear(pixels);
        Draw(pixels, kind, CancellationToken.None);
        Assert.Contains(pixels, v => v != 0);

        static void Draw(byte[] pixels, int kind, CancellationToken token)
        {
            SvgHairlineCoverage raster = new(pixels, 64, 64, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), null, SvgClipAntialiasing.None, token);
            SvgDevicePoint[] points = [new(6, 40), new(-12, -12), new(76, 80), new(58, 12)];
            if (kind == 0) SvgHairlineCurves.DrawQuadratic(ref raster, points.AsSpan(0, 3), token);
            else if (kind == 1) SvgHairlineCurves.DrawCubic(ref raster, points, token);
            else SvgHairlineCurves.DrawConic(ref raster, points.AsSpan(0, 3), SvgRationalQuadratic.QuarterCircleWeight, token);
        }
    }

    [Fact]
    public void Nonfinite_interior_polynomial_drops_every_line()
    {
        byte[] pixels = new byte[64 * 64 * 4];
        SvgHairlineCoverage raster = new(pixels, 64, 64, SvgAnalyticPaint.Solid(new(0, 0, 0, 255)), null, SvgClipAntialiasing.None, CancellationToken.None);
        SvgDevicePoint[] points = [new(4, 4), new(float.MaxValue, 8), new(60, 60)];
        SvgHairlineCurves.DrawQuadratic(ref raster, points, CancellationToken.None);
        Assert.All(pixels, v => Assert.Equal(0, v));
    }

    [Theory]
    [InlineData("M6 40 Q30 -4 58 38", "butt")]
    [InlineData("M6 40 C-12 -12 76 80 58 12", "round")]
    [InlineData("M6 36 A25 17 25 1 1 56 32", "square")]
    public async Task Retained_curve_strokes_agree_through_six_outputs(string path, string cap)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path transform='translate(.25 .25)' fill='none' stroke='#eb1c23' stroke-width='.5' stroke-linecap='" + cap + "' d='" + path + "'/></svg>");
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

    [Fact]
    public void Raster_memory_and_cancellation_failures_release_surface_and_preserve_retained_curve()
    {
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path fill='none' stroke='#eb1c23' stroke-width='.5' d='M6 40 C-12 -12 76 80 58 12'/></svg>"u8.ToArray();
        SvgRenderLimits defaults = SvgRenderLimits.Default;
        RenderBudget budget = new(defaults.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, budget, null, CancellationToken.None))
        {
            SvgAuthoredPath path = Assert.IsType<SvgAuthoredPath>(Assert.Single(root.Geometries).AuthoredPath);
            SvgAuthoredPathCommand[] commands = path.Commands.ToArray();
            long retained = budget.Snapshot().CurrentBytes;
            using (budget.Reserve(defaults.MaximumAccountedBytes - retained - 1024, SvgMemoryCategory.Surface))
            {
                long padded = budget.Snapshot().CurrentBytes;
                SvgException memory = Assert.Throws<SvgException>(() => { using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None); });
                Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => { using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancelled.Token); });
            Assert.Equal(cancelled.Token, error.CancellationToken);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
            Assert.Equal(commands, path.Commands.ToArray());
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Contains(retry.Pixels, v => v != 0);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
