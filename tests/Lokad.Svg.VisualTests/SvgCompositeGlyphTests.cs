using System.Buffers.Binary;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects independently fitted component/parent geometry, font scale semantics and bounded cleanup; whole-document pixels remain a separate acceptance gate.</summary>
public sealed class SvgCompositeGlyphTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents");
    private static TrueTypeHintTarget Target => new(35, TrueTypeHintQueryFlags.Grayscale, TrueTypeHintControlScale.FractionalFontUnits, TrueTypeHintPreparation.DefaultThenTarget);

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument witnesses = ReadWitnesses();
        foreach (JsonElement item in witnesses.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native composite identity.")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Child_and_mirrored_parent_match_independent_native_contours(string identity)
    {
        using JsonDocument witnesses = ReadWitnesses();
        JsonElement item = witnesses.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        byte[] bytes = Font(), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = Fit(bytes, metrics, item, budget, CancellationToken.None))
        {
            Assert.Equal(item.GetProperty("contourEnds").EnumerateArray().Select(c => c.GetInt32()), contours.ContourEnds);
            JsonElement expected = item.GetProperty("segments");
            Assert.Equal(expected.GetArrayLength(), contours.Segments.Length);
            for (int index = 0; index < contours.Segments.Length; index++)
            {
                OpenTypeQuadraticSegment s = contours.Segments[index];
                Assert.Equal(expected[index].EnumerateArray().Select(v => v.GetDouble()), new[] { s.Start.X, s.Start.Y, s.Control.X, s.Control.Y, s.End.X, s.End.Y });
            }
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 1000)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 1000)]
    [InlineData(true, 0)]
    public void Denied_point_assembly_and_interpreter_storage_release_prior_owners(bool light, long maximumBytes)
    {
        byte[] bytes = Font(), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        if (maximumBytes == 0)
        {
            RenderBudget baseline = new(long.MaxValue);
            using (OpenTypeGlyphContourStorage contours = FitParent(bytes, metrics, light, baseline, CancellationToken.None)) Assert.NotEmpty(contours.Segments);
            Assert.Equal(0, baseline.Snapshot().CurrentBytes);
            maximumBytes = baseline.Snapshot().PeakBytes - 1;
        }
        RenderBudget budget = new(maximumBytes);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => FitParent(bytes, metrics, light, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancelled_composite_fitting_releases_all_storage(bool light)
    {
        byte[] bytes = Font();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => FitParent(bytes, metrics, light, budget, cancellation.Token));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void The_declared_integer_hinting_flag_controls_fitting_without_changing_linear_advances()
    {
        byte[] bytes = Font(), fractional = bytes.ToArray();
        int count = BinaryPrimitives.ReadUInt16BigEndian(fractional.AsSpan(4));
        int head = Enumerable.Range(0, count).Select(i => 12 + i * 16).Single(i => BinaryPrimitives.ReadUInt32BigEndian(fractional.AsSpan(i)) == 0x68656164);
        int offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(fractional.AsSpan(head + 8))) + 16;
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(fractional.AsSpan(offset));
        BinaryPrimitives.WriteUInt16BigEndian(fractional.AsSpan(offset), (ushort)(flags & ~8));
        OpenTypeFontMetrics integerMetrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), fractionalMetrics = OpenTypeFont.ParseMetrics(fractional, CancellationToken.None);
        Assert.True(integerMetrics.ForceIntegerHintingSize);
        Assert.False(fractionalMetrics.ForceIntegerHintingSize);
        Assert.Equal(integerMetrics.UnitsPerEm, fractionalMetrics.UnitsPerEm);
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage a = OpenTypeFont.GetTrueTypeContours(bytes, 11, integerMetrics, 18.25, Target, budget, CancellationToken.None))
        using (OpenTypeGlyphContourStorage b = OpenTypeFont.GetTrueTypeContours(bytes, 11, integerMetrics, 18, Target, budget, CancellationToken.None))
        using (OpenTypeGlyphContourStorage c = OpenTypeFont.GetTrueTypeContours(fractional, 11, fractionalMetrics, 18.25, Target, budget, CancellationToken.None))
        {
            Assert.Equal(a.Segments, b.Segments);
            Assert.False(a.Segments.SequenceEqual(c.Segments));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(OpenTypeFont.GetAdvanceWidth(bytes, 11, integerMetrics, CancellationToken.None), OpenTypeFont.GetAdvanceWidth(fractional, 11, fractionalMetrics, CancellationToken.None));
    }

    [Fact]
    public void Integer_hinting_size_collapse_returns_an_empty_glyph()
    {
        byte[] bytes = Font(); RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, 12, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), .25, Target, budget, CancellationToken.None)) Assert.Empty(contours.Segments);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static JsonDocument ReadWitnesses() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "composite-contours.json")));
    private static byte[] Font() => File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSansMono-Bold.ttf"));
    private static OpenTypeGlyphContourStorage Fit(byte[] bytes, OpenTypeFontMetrics metrics, JsonElement item, RenderBudget budget, CancellationToken token)
        => item.GetProperty("stage").GetString() == "LatinLight"
            ? OpenTypeFont.GetLatinLightContours(bytes, item.GetProperty("glyphIndex").GetUInt16(), metrics, item.GetProperty("deviceSize").GetDouble(), budget, token)
            : OpenTypeFont.GetTrueTypeContours(bytes, item.GetProperty("glyphIndex").GetUInt16(), metrics, item.GetProperty("deviceSize").GetDouble(), Target with { Version = item.GetProperty("interpreterVersion").GetInt32() }, budget, token);
    private static OpenTypeGlyphContourStorage FitParent(byte[] bytes, OpenTypeFontMetrics metrics, bool light, RenderBudget budget, CancellationToken token)
        => light ? OpenTypeFont.GetLatinLightContours(bytes, 12, metrics, 18, budget, token) : OpenTypeFont.GetTrueTypeContours(bytes, 12, metrics, 18, Target, budget, token);
}
