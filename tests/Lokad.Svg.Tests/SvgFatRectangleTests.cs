using System.Globalization;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgFatRectangleTests
{
    [Fact]
    public void Float_fringe_bytes_match_the_unchanged_compiled_native_blitter()
    {
        string[] rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData/fat-rectangle-native.csv"));
        Assert.Equal(3366, rows.Length);
        foreach (string row in rows.Skip(1))
        {
            string[] fields = row.Split(',');
            SvgFatRectangleCoverage rectangle = new(Parse(fields[0]), Parse(fields[1]), Parse(fields[2]), Parse(fields[3]), new(254, 508));
            int x = int.Parse(fields[4], CultureInfo.InvariantCulture), y = int.Parse(fields[5], CultureInfo.InvariantCulture);
            Assert.Equal(byte.Parse(fields[6], CultureInfo.InvariantCulture), rectangle.GetAlpha(x + 254, y + 508));
        }
        static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Original_verbs_match_the_compiled_native_rectangle_recognizer()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        using JsonDocument input = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "rectangle-recognition-inputs.json")));
        Dictionary<string, bool> expected = File.ReadAllLines(Path.Combine(directory, "rectangle-recognition-native.csv")).Skip(1)
            .Select(row => row.Split(',')).ToDictionary(fields => fields[0], fields => bool.Parse(fields[1]), StringComparer.Ordinal);
        Assert.Equal(21, expected.Count);
        foreach (JsonElement testCase in input.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id = testCase.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case ID.");
            List<SvgAuthoredPathCommand> commands = new();
            foreach (JsonElement command in testCase.GetProperty("commands").EnumerateArray())
            {
                string verb = command.GetProperty("verb").GetString() ?? throw new InvalidDataException("Missing verb.");
                SvgDevicePoint[] points = command.GetProperty("points").EnumerateArray().Select(point => new SvgDevicePoint(point[0].GetSingle(), point[1].GetSingle())).ToArray();
                commands.Add(new(Enum.Parse<SvgAuthoredPathVerb>(verb), SvgAuthoredArcFlags.None,
                    points.Length > 0 ? points[0] : default, points.Length > 1 ? points[1] : default, points.Length > 2 ? points[2] : default));
            }
            SvgFatRectangleCoverage? rectangle = SvgFatRectangleCoverage.TryCreate(commands.ToArray(), [], SvgAffineTransform.Identity, new(0, 0), new(0, 0, 512, 512), CancellationToken.None);
            Assert.Equal(expected[id], rectangle.HasValue);
        }
    }

    [Fact]
    public void Shortcut_owns_no_scratch_and_observes_paint_cancellation()
    {
        SvgDevicePoint[] points = [new(2.125, 2.13), new(9.938, 2.13), new(9.938, 9.997), new(2.125, 9.997)];
        SvgAnalyticContourPreparation preparation = new(points, [4], [], false, null, SvgPolygonMapping.NativeFillEdges, [], [], new(2, 2, 10, 10), 32, 32);
        RenderBudget budget = new(1);
        using (SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(ref preparation, 2, 10, 2, 10, new(0, 32), null, SvgAnalyticPaintClip.Binary, budget, out bool eligible, CancellationToken.None))
        {
            Assert.True(eligible);
            Assert.Equal(0, walker.EdgeCount);
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            walker.PrepareRow(2, cancellation.Token);
            cancellation.Cancel();
            try { walker.PaintPixel(0, new byte[4], SvgAnalyticPaint.Solid(new(64, 96, 128, 255)), SvgAnalyticClipCoverage.Full); Assert.Fail("Canceled paint succeeded."); }
            catch (OperationCanceledException error) { Assert.Equal(cancellation.Token, error.CancellationToken); }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("fat-path-paints.svg")]
    [InlineData("fat-path-boundary-verbs.svg")]
    public async Task Six_outputs_agree_for_rectangle_paints_verbs_and_bitmap_boundaries(string source)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", source));
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
}
