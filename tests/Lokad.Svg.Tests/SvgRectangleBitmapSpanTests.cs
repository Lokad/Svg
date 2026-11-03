using System.Text.Json;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgRectangleBitmapSpanTests
{
    public static IEnumerable<object[]> NativeSpans()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "image-fit-sampling", "native-spans.json")));
        foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            JsonElement edges = row.GetProperty("edges"), clip = row.GetProperty("clip");
            yield return [edges.EnumerateArray().Select(static value => value.GetInt64()).ToArray(),
                clip.EnumerateArray().Select(static value => value.GetInt32()).ToArray(),
                row.GetProperty("x").GetInt32(), row.GetProperty("y").GetInt32(), row.GetProperty("span")[1].GetInt32()];
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(NativeSpans))]
    public void Rectangle_dispatch_matches_compiled_native_calls(long[] edges, int[] clip,
        int x, int y, int expectedEnd)
    {
        SvgRectangleCoverage coverage = new(edges[0], edges[1], edges[2], edges[3]);
        SvgAnalyticClipBounds bounds = new(clip[0], clip[1], clip[2], clip[3]);
        Assert.Equal(expectedEnd, coverage.GetSpanEnd(x, y, bounds));
    }
}
