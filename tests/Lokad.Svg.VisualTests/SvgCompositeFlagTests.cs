using System.Text.Json;
using System.Buffers.Binary;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects independent native component placement and material ownership; whole-document pixels are checked separately.</summary>
public sealed class SvgCompositeFlagTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "composite-flag-exit-sheet");
    private static TrueTypeHintTarget WindowsTarget => new(42, TrueTypeHintQueryFlags.FontVariations | TrueTypeHintQueryFlags.VerticalPhantoms | TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned | TrueTypeHintQueryFlags.Symmetric, TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single);

    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "native-contours.json")));
        foreach (JsonElement item in witnesses.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void Component_origins_strikes_and_alignment_zones_match_independent_native_contours(string identity)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "CompositeFlags.ttf"));
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "native-contours.json")));
        JsonElement item = witnesses.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        if (item.TryGetProperty("matrixVariant", out JsonElement matrix))
        {
            OpenTypeTableRange glyph = OpenTypeFont.GetGlyphDataRange(bytes, 6, CancellationToken.None);
            for (int index = 0; index < 4; index++)
                BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(glyph.Offset + 18 + index * 2, 2), checked((short)(matrix[index].GetDouble() * 16384)));
        }
        byte[] unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(16_000_000);
        using (OpenTypeGlyphContourStorage contours = Fit(bytes, metrics, item.GetProperty("glyph").GetUInt16(), item.GetProperty("size").GetDouble(), item.GetProperty("profile").GetString() == "windows", budget, new(100_000_000), CancellationToken.None))
        {
            Assert.Equal(item.GetProperty("contourEnds").EnumerateArray().Select(v => v.GetInt32()), contours.ContourEnds);
            JsonElement segments = item.GetProperty("segments");
            Assert.Equal(segments.GetArrayLength(), contours.Segments.Length);
            for (int index = 0; index < contours.Segments.Length; index++)
            {
                OpenTypeQuadraticSegment s = contours.Segments[index];
                Assert.Equal(segments[index].EnumerateArray().Select(v => v.GetDouble()), new[] { s.Start.X, s.Start.Y, s.Control.X, s.Control.Y, s.End.X, s.End.Y });
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Transformed_component_fitting_releases_owners_after_denial_and_supports_retry(bool windows)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "CompositeFlags.ttf")), unchanged = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget accepted = new(16_000_000); ParserWorkCounter completed = new(100_000_000);
        OpenTypeQuadraticSegment[] expected;
        using (OpenTypeGlyphContourStorage contours = Fit(bytes, metrics, 6, 64.5, windows, accepted, completed, CancellationToken.None)) expected = contours.Segments.ToArray();
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        foreach (long maximum in new[] { 1L, accepted.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new(maximum);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Fit(bytes, metrics, 6, 64.5, windows, denied, new(100_000_000), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Fit(bytes, metrics, 6, 64.5, windows, accepted, new(completed.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => Fit(bytes, metrics, 6, 64.5, windows, accepted, new(100_000_000), cancellation.Token)).CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using (OpenTypeGlyphContourStorage retry = Fit(bytes, metrics, 6, 64.5, windows, accepted, new(100_000_000), CancellationToken.None)) Assert.Equal(expected, retry.Segments);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, bytes);
    }

    [Fact]
    public void Instruction_free_component_strikes_can_collapse_below_the_device_grid()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "CompositeFlags.ttf"));
        RenderBudget budget = new(16_000_000);
        using (OpenTypeGlyphContourStorage contours = Fit(bytes, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), 6, .00001, true, budget, new(100_000_000), CancellationToken.None))
            Assert.All(contours.Segments, segment => { Assert.Equal(new OpenTypeOutlinePoint(0, 0), segment.Start); Assert.Equal(segment.Start, segment.Control); Assert.Equal(segment.Start, segment.End); });
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static OpenTypeGlyphContourStorage Fit(byte[] bytes, OpenTypeFontMetrics metrics, ushort glyph, double size, bool windows, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        => windows ? OpenTypeFont.GetTrueTypeContours(bytes, glyph, metrics, size, WindowsTarget, TrueTypeImpliedPointRounding.None, budget, work, token)
            : OpenTypeFont.GetLatinLightContours(bytes, glyph, metrics, size, budget, work, token);
}
