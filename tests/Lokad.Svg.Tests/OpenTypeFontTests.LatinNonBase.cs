using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> LatinNonBaseNativeCases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "latin-nonbase", "native-vectors.json")));
        foreach (JsonElement item in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("font").GetString() ?? throw new InvalidDataException(), item.GetProperty("glyph").GetUInt16(), item.GetProperty("size").GetDouble(), item.GetProperty("left").GetDouble(), item.GetProperty("bottom").GetDouble(), item.GetProperty("right").GetDouble(), item.GetProperty("top").GetDouble()];
    }

    [Theory]
    [MemberData(nameof(LatinNonBaseNativeCases))]
    public void Latin_glyph_styles_preserve_native_scale_stems_and_nonbase_alignment(string file, ushort glyph, double size, double left, double bottom, double right, double top)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "latin-nonbase", file)), unchanged = bytes.ToArray();
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(bytes, glyph, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), size, budget, new(long.MaxValue), CancellationToken.None))
        {
            OpenTypeOutlinePoint[] points = [new(left, bottom), new(left, top), new(right, top), new(right, bottom)];
            Assert.Equal([4], contours.ContourEnds.ToArray());
            Assert.Equal(4, contours.Segments.Length);
            for (int i = 0; i < points.Length; i++) Assert.Equal(new OpenTypeQuadraticSegment(points[i], points[(i + 1) % points.Length], points[(i + 1) % points.Length]), contours.Segments[i]);
        }
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Nonbase_cmap_classification_preserves_cancellation_during_its_scan()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "latin-nonbase", "Base.ttf")), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using CancellationTokenSource cancellation = new();
        int lookups = 0;
        using IDisposable probe = CmapLookupProbe.Arm(() => { if (++lookups == 5) cancellation.Cancel(); });

        OperationCanceledException error = Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetLatinLightContours(bytes, 1, metrics, 16, budget, new(long.MaxValue), cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Nonbase_cmap_classification_charges_its_bounded_lookup_work()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "latin-nonbase", "Base.ttf")), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);

        SvgException error = Assert.Throws<SvgException>(() => OpenTypeFont.GetLatinLightContours(bytes, 1, metrics, 16, budget, new(40), CancellationToken.None));

        Assert.Equal(SvgErrorCode.LimitExceeded, error.ErrorCode);
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
