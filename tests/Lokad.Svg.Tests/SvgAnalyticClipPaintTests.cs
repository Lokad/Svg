using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgAnalyticClipPaintTests
{
    [Fact]
    public void Native_horizontal_vertical_and_contained_routes_preserve_all_byte_coverages_and_backgrounds()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData");
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "analytic-clip-pair-native.csv"));
        using JsonDocument metadata = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "analytic-clip-pair-native.json")));
        Assert.Equal(metadata.RootElement.GetProperty("dataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("f8b66b7597c4cc859d3ed190e9c6872241e6721c", metadata.RootElement.GetProperty("sourceRevision").GetString());
        string[] rows = File.ReadAllLines(Path.Combine(directory, "analytic-clip-pair-native.csv"));
        Assert.Equal(3072, rows.Length);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        SvgAnalyticPaint paint = SvgAnalyticPaint.Solid(new(0, 52, 120, 255));
        foreach (JsonElement nativeCase in metadata.RootElement.GetProperty("cases").EnumerateArray())
        {
            int id = nativeCase.GetProperty("id").GetInt32();
            JsonElement bounds = nativeCase.GetProperty("bounds");
            int left = bounds[0].GetInt32();
            int top = bounds[1].GetInt32();
            int right = bounds[2].GetInt32();
            int bottom = bounds[3].GetInt32();
            // A concave contour retains the independently supplied raw bounds;
            // clipping changes paint routing before any row is walked.
            SvgDevicePoint[] points = [new(left, top), new(right, top), new(right, bottom), new(left + 12, bottom), new(left + 6, top + 12), new(left, bottom)];
            using SvgAnalyticCoverage walker = SvgAnalyticCoverage.Prepare(points, [6], [], false, null, SvgPolygonMapping.FlattenedCurves, [], [], 508, 764, Math.Max(0, top), Math.Min(400, bottom), 1000, 400, new(508, 764), new(new(508, 0, 764, 256)), budget, out bool eligible, CancellationToken.None);
            Assert.True(eligible);
            for (int index = id * 768; index < (id + 1) * 768; index++)
            {
                string[] fields = rows[index].Split(',');
                Assert.Equal(id, int.Parse(fields[0], CultureInfo.InvariantCulture));
                int background = int.Parse(fields[1], CultureInfo.InvariantCulture);
                byte coverage = byte.Parse(fields[2], CultureInfo.InvariantCulture);
                Assert.Equal(fields[3], walker.PairPaintKind.ToString());
                byte[] pixel = background switch { 0 => [0, 0, 0, 0], 1 => [255, 255, 255, 255], 2 => [0, 0, 0, 255], _ => throw new InvalidDataException("Invalid native background.") };
                paint.Blend(pixel, coverage, walker.PairPaintKind);
                uint expected = uint.Parse(fields[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                Assert.Equal(new byte[] { (byte)(expected >> 16), (byte)(expected >> 8), (byte)expected, (byte)(expected >> 24) }, pixel);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
