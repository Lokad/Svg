using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects the native contours that exposed near-point and contour-start defects in SVG Translate.</summary>
public sealed class SvgLatinLightNearPointTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument witnesses = ReadWitnesses();
        foreach (JsonElement item in witnesses.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native contour identity.")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Document_strikes_match_independent_native_contours(string identity)
    {
        using JsonDocument witnesses = ReadWitnesses();
        JsonElement item = witnesses.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSansMono-Bold.ttf")), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(bytes, item.GetProperty("glyphIndex").GetUInt16(), metrics, item.GetProperty("deviceSize").GetDouble(), budget, CancellationToken.None))
        {
            Assert.Equal(item.GetProperty("contourEnds").EnumerateArray().Select(c => c.GetInt32()), contours.ContourEnds);
            JsonElement expected = item.GetProperty("segments");
            Assert.Equal(expected.GetArrayLength(), contours.Segments.Length);
            for (int i = 0; i < contours.Segments.Length; i++)
            {
                OpenTypeQuadraticSegment s = contours.Segments[i];
                Assert.Equal(expected[i].EnumerateArray().Select(v => v.GetDouble()), new[] { s.Start.X, s.Start.Y, s.Control.X, s.Control.Y, s.End.X, s.End.Y });
            }
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }

    private static JsonDocument ReadWitnesses() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "latin-light-near-contours.json")));
}
