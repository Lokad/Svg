using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Checks actual accent contours and metric-dependent parent hints against independent native data; whole-document pixel acceptance remains separate.</summary>
public sealed class SvgCompositeMetricsTests
{
    private static string FontPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels", "fonts", "NotoSans-Regular.ttf");
    private static string WitnessPath => Path.Combine(AppContext.BaseDirectory, "Research", "curved-font-documents", "diagnostics", "composite-metrics-contours.json");
    private static TrueTypeHintTarget Target => new(35, TrueTypeHintQueryFlags.Grayscale, TrueTypeHintControlScale.FractionalFontUnits, TrueTypeHintPreparation.DefaultThenTarget);

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(WitnessPath));
        foreach (JsonElement item in witnesses.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native metric witness identity.")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Accents_and_metric_dependent_parents_match_independent_native_contours(string identity)
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(WitnessPath));
        JsonElement item = witnesses.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        byte[] original = File.ReadAllBytes(FontPath), unchanged = original.ToArray();
        byte[] bytes = item.TryGetProperty("syntheticVariant", out JsonElement variant) ? CompositeMetricsFont.Create(original, (CompositeMetricsCase)variant.GetInt32()) : original;
        byte[] supplied = bytes.ToArray();
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, item.GetProperty("glyphIndex").GetUInt16(), OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), item.GetProperty("deviceSize").GetDouble(), Target with { Version = item.GetProperty("interpreterVersion").GetInt32() }, budget, new(long.MaxValue), CancellationToken.None))
        {
            Assert.Equal(item.GetProperty("contourEnds").EnumerateArray().Select(c => c.GetInt32()), contours.ContourEnds);
            JsonElement expected = item.GetProperty("segments");
            Assert.Equal(expected.GetArrayLength(), contours.Segments.Length);
            for (int index = 0; index < contours.Segments.Length; index++)
            {
                OpenTypeQuadraticSegment s = contours.Segments[index];
                Assert.Equal(expected[index].EnumerateArray().Select(v => v.GetDouble()), new[] { s.Start.X, s.Start.Y, s.Control.X, s.Control.Y, s.End.X, s.End.Y });
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(supplied, bytes);
        Assert.Equal(unchanged, original);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 1000)]
    [InlineData(false, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1000)]
    public void Denied_nested_metric_fitting_releases_owners_and_allows_retry(bool limitWork, long maximum)
    {
        byte[] bytes = CompositeMetricsFont.Create(File.ReadAllBytes(FontPath), CompositeMetricsCase.NestedChildMetrics), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        if (maximum == -1)
        {
            RenderBudget baseline = new(long.MaxValue);
            using (OpenTypeGlyphContourStorage contours = Fit(baseline, new(long.MaxValue), CancellationToken.None)) Assert.NotEmpty(contours.Segments);
            Assert.Equal(0, baseline.Snapshot().CurrentBytes);
            maximum = baseline.Snapshot().PeakBytes - 1;
        }
        RenderBudget budget = new(limitWork ? long.MaxValue : maximum);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Fit(budget, new(limitWork ? maximum : long.MaxValue), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget retryBudget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage retry = Fit(retryBudget, new(long.MaxValue), CancellationToken.None)) Assert.NotEmpty(retry.Segments);
        Assert.Equal(0, retryBudget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);

        OpenTypeGlyphContourStorage Fit(RenderBudget owner, ParserWorkCounter work, CancellationToken token)
            => OpenTypeFont.GetTrueTypeContours(bytes, CompositeMetricsFont.ParentGlyph, metrics, 20, Target, owner, work, token);
    }

    [Fact]
    public void Cancelled_accent_fitting_releases_owners_and_keeps_supplied_bytes()
    {
        byte[] bytes = File.ReadAllBytes(FontPath), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => OpenTypeFont.GetTrueTypeContours(bytes, 190, metrics, 20, Target, budget, new(long.MaxValue), cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }
}
