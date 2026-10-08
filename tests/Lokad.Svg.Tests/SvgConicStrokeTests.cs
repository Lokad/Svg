using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgConicStrokeTests
{
    [Fact]
    public void Offset_conic_segments_match_compiled_native_float_bits()
    {
        int count = 0, segments = 0;
        foreach (string row in NativeRows())
        {
            string[] fields = row.Split(',');
            float[] p = fields[5].Split(':').Select(Number).ToArray();
            RenderBudget budget = new(16_777_216);
            using (BudgetedParserList<SvgStrokeCurveSegment> output = new(budget, SvgMemoryCategory.Geometry))
            {
                SvgCurveStroke.Append(new(p[0], p[1]), new(p[2], p[3]), new(p[4], p[5]), Number(fields[1]), Number(fields[2]), Number(fields[3]),
                    (SvgStrokeSide)int.Parse(fields[4], CultureInfo.InvariantCulture), output, new ParserWorkCounter(100_000), CancellationToken.None);
                string[] expected = fields[6].Split(';');
                Assert.Equal(expected.Length, output.Count);
                for (int index = 0; index < expected.Length; index++)
                {
                    uint[] bits = expected[index].Split(':').Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                    SvgStrokeCurveSegment actual = output[index];
                    Assert.Equal(bits[0] == 1, actual.Control.HasValue);
                    if (actual.Control is SvgDevicePoint control)
                    { EqualBits(fields[0], bits[1], control.X); EqualBits(fields[0], bits[2], control.Y); }
                    EqualBits(fields[0], bits[3], actual.End.X); EqualBits(fields[0], bits[4], actual.End.Y);
                }
                segments += output.Count;
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            count++;
        }
        Assert.Equal(960, count);
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/conic-stroke-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("segments").GetInt32(), segments);

        static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        static void EqualBits(string id, uint expected, double actual)
            => Assert.True(expected == BitConverter.SingleToUInt32Bits((float)actual), id + ": expected " + expected + ", actual " + BitConverter.SingleToUInt32Bits((float)actual));
    }

    [Fact]
    public void Work_denial_and_cancellation_release_private_output_before_retry()
    {
        RenderBudget budget = new(16_777_216);
        using (BudgetedParserList<SvgStrokeCurveSegment> output = new(budget, SvgMemoryCategory.Geometry))
        {
            SvgException error = Assert.Throws<SvgException>(() => Append(output, new ParserWorkCounter(2), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource source = new();
        source.Cancel();
        using (BudgetedParserList<SvgStrokeCurveSegment> output = new(budget, SvgMemoryCategory.Geometry))
        {
            OperationCanceledException error = Assert.Throws<OperationCanceledException>(() => Append(output, new ParserWorkCounter(100_000), source.Token));
            Assert.Equal(source.Token, error.CancellationToken);
            Assert.Equal(0, output.Count);
            Append(output, new ParserWorkCounter(100_000), CancellationToken.None);
            Assert.True(output.Count > 1);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);

        static void Append(BudgetedParserList<SvgStrokeCurveSegment> output, ParserWorkCounter work, CancellationToken token)
            => SvgCurveStroke.Append(new(80, 48), new(80, 65), new(48, 65), SvgRationalQuadratic.QuarterCircleWeight, 5, 4, SvgStrokeSide.Inner, output, work, token);
    }

    private static IEnumerable<string> NativeRows()
    {
        byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/conic-stroke-native.csv.gz"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData/conic-stroke-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        using MemoryStream bytes = new(data);
        using GZipStream gzip = new(bytes, CompressionMode.Decompress);
        using StreamReader reader = new(gzip);
        _ = reader.ReadLine();
        while (reader.ReadLine() is string line) yield return line;
    }

    [Fact]
    public void Output_growth_reserves_memory_before_allocation()
    {
        RenderBudget budget = new(1);
        using BudgetedParserList<SvgStrokeCurveSegment> output = new(budget, SvgMemoryCategory.Geometry);
        SvgException error = Assert.Throws<SvgException>(() => SvgCurveStroke.Append(new(80, 48), new(80, 65), new(48, 65),
            SvgRationalQuadratic.QuarterCircleWeight, 5, 1, SvgStrokeSide.Outer, output, new ParserWorkCounter(100_000), CancellationToken.None));
        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(0, output.Count);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(1, budget.Snapshot().DeniedReservationCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Native_stroke_quadratics_do_not_depend_on_fallback_chord_count(bool multipleContours, bool clipped)
    {
        Assert.Equal(Paint(false, multipleContours, clipped), Paint(true, multipleContours, clipped));

        static byte[] Paint(bool dense, bool multiple, bool clipped)
        {
            SvgDevicePoint[] outline = dense
                ? [new(8, 8), new(32, 13), new(56, 28), new(56, 48), new(8, 48)]
                : [new(8, 8), new(56, 28), new(56, 48), new(8, 48)];
            SvgDevicePoint[] points = multiple ? [.. outline, new(20, 30), new(20, 40), new(44, 40), new(44, 30)] : outline;
            int[] ends = multiple ? [outline.Length, points.Length] : [points.Length];
            SvgQuadSpan[] quads = [new(0, 0, dense ? 2 : 1, new(32, 8))];
            SvgAnalyticContourPreparation preparation = new(points, ends, [], false, null, SvgPolygonMapping.NativeStrokeEdges, quads, [], new(8, 8, 56, 48), 64, 64);
            RenderBudget budget = new(1_000_000);
            byte[] output = new byte[64 * 64 * 4];
            SvgAnalyticClipContext? clip = clipped ? new(new(12, 0, 52, 64)) : null;
            using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 0, 64, 0, 64, new(0, 64), clip,
                SvgAnalyticPaintClip.Binary, 1_000_000, budget, out _, out bool eligible, CancellationToken.None))
            {
                Assert.True(eligible);
                for (int y = 0; y < 64; y++)
                {
                    walker.PrepareRow(y, out _, CancellationToken.None);
                    for (int x = 0; x < 64; x++) walker.PaintPixel(x, output.AsSpan((y * 64 + x) * 4, 4), SvgAnalyticPaint.Solid(new(231, 28, 53, 255)), SvgAnalyticClipCoverage.Full);
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            return output;
        }
    }

    [Theory]
    [InlineData("<ellipse cx='48' cy='48' rx='32' ry='17' fill='none' stroke='red' stroke-width='2'/>")]
    [InlineData("<g transform='translate(6.25 7.75) scale(1.25)'><circle cx='32' cy='32' r='25' fill='none' stroke='red' stroke-width='4'/></g>")]
    [InlineData("<ellipse cx='48' cy='48' rx='4' ry='22' fill='none' stroke='red' stroke-width='10'/>")]
    public async Task Oval_stroke_outputs_agree_and_retained_verbs_survive_failed_rasterization(string shape)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'>" + shape + "</svg>");
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

        SvgRenderLimits defaults = SvgRenderLimits.Default;
        RenderBudget budget = new(defaults.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(input, defaults, budget, null, new(defaults.MaximumWorkUnits), CancellationToken.None))
        {
            SvgGeometry geometry = Assert.Single(root.Geometries.ToArray(), value => value.Kind == SvgGeometryKind.PolygonFill && value.Polygon.Mapping == SvgPolygonMapping.NativeStrokeEdges);
            Assert.Equal(SvgGeometryKind.PolygonFill, geometry.Kind);
            Assert.Equal(SvgPolygonMapping.NativeStrokeEdges, geometry.Polygon.Mapping);
            SvgQuadSpan[] original = geometry.Polygon.QuadSpans.ToArray();
            Assert.NotEmpty(original);
            SvgAuthoredPath source = geometry.AuthoredPath ?? throw new InvalidOperationException("Missing oval source stroke.");
            SvgAuthoredPathCommand[] originalCommands = source.Commands.ToArray();
            SvgAuthoredPath outline = source.StrokeOutline ?? throw new InvalidOperationException("Missing oval source outline.");
            SvgAuthoredPathCommand[] originalOutline = outline.Commands.ToArray();
            long owned = budget.Snapshot().CurrentBytes;
            SvgRenderLimits tight = new(defaults.MaximumAccountedBytes, defaults.MaximumInputBytes, defaults.MaximumDimension,
                defaults.MaximumPixels, defaults.MaximumEncodedPngBytes, defaults.MaximumNodes, defaults.MaximumPathCommands,
                checked((int)root.ParserWorkUnits + 1), defaults.MaximumXmlDepth, defaults.MaximumAttributesPerElement);
            SvgException work = Assert.Throws<SvgException>(() => { using var denied = SvgConverter.Rasterize(root, new(null, 1, SvgColor.Transparent, tight), budget, CancellationToken.None); });
            Assert.Equal(SvgErrorCode.LimitExceeded, work.ErrorCode);
            Assert.Equal(owned, budget.Snapshot().CurrentBytes);
            using (budget.Reserve(defaults.MaximumAccountedBytes - owned - 1024, SvgMemoryCategory.Surface))
            {
                long padded = budget.Snapshot().CurrentBytes;
                SvgException memory = Assert.Throws<SvgException>(() => { using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None); });
                Assert.Equal(SvgErrorCode.LimitExceeded, memory.ErrorCode);
                Assert.Equal(padded, budget.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancelled = new(); cancelled.Cancel();
            OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => { using var denied = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, cancelled.Token); });
            Assert.Equal(cancelled.Token, error.CancellationToken);
            Assert.Equal(original, geometry.Polygon.QuadSpans);
            Assert.Equal(originalCommands, source.Commands.ToArray());
            Assert.Equal(originalOutline, outline.Commands.ToArray());
            using (SvgConverter.RasterizedSurface retry = SvgConverter.Rasterize(root, SvgRenderOptions.Default, budget, CancellationToken.None))
                Assert.Contains(retry.Pixels, value => value != 0);
            Assert.Equal(owned, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("linear", false)]
    [InlineData("linear", true)]
    [InlineData("radial", false)]
    [InlineData("radial", true)]
    [InlineData("pattern", false)]
    [InlineData("pattern", true)]
    public void Painted_oval_stroke_instances_preserve_direct_curve_pixels_and_ownership(string paint, bool circle)
    {
        string server = paint switch
        {
            "linear" => "<linearGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>",
            "radial" => "<radialGradient id='g'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>",
            "pattern" => "<pattern id='g' patternUnits='userSpaceOnUse' width='8' height='8'><rect width='8' height='8' fill='blue'/></pattern>",
            _ => throw new ArgumentException("Unknown paint."),
        };
        string shape = circle ? "circle cx='32' cy='32' r='20'" : "ellipse cx='32' cy='32' rx='20' ry='12'";
        string prefix = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>" + server + "</defs>";
        byte[] direct = System.Text.Encoding.UTF8.GetBytes(prefix + "<" + shape + " fill='red' stroke='url(#g)' stroke-width='8'/></svg>");
        byte[] instance = System.Text.Encoding.UTF8.GetBytes(prefix + "<defs><" + shape + " id='s' fill='red' stroke='url(#g)' stroke-width='8'/></defs><use href='#s'/></svg>");
        Assert.Equal(SvgConverter.ConvertToPng(direct, SvgRenderOptions.Default, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(instance, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        SvgRenderLimits limits = SvgRenderLimits.Default;
        RenderBudget budget = new(limits.MaximumAccountedBytes);
        using (SvgRoot root = SvgRootParser.Parse(instance, limits, budget, null, new(limits.MaximumWorkUnits), CancellationToken.None))
            Assert.True(root.Geometries.Length > 0);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
