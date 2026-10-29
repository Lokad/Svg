using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects native intermediate contours separately from unfinished font pixel acceptance.</summary>
public sealed class SvgLatinLightOutlineTests
{
    private static string DirectoryPath => Path.Combine(ResearchCorpus.Root, "supplied-font-labels");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "latin-light-contours.json")));
        foreach (JsonElement item in witnesses.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native geometry identity.")];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Managed_device_contours_match_pinned_native_glyph_geometry(string identity)
    {
        using JsonDocument witnesses = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "latin-light-contours.json")));
        JsonElement item = witnesses.RootElement.GetProperty("cases").EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == identity);
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSans-Regular.ttf"));
        byte[] original = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(bytes, item.GetProperty("glyphIndex").GetUInt16(), metrics, item.GetProperty("deviceSize").GetDouble(), budget, CancellationToken.None))
        {
            JsonElement.ArrayEnumerator commands = item.GetProperty("commands").EnumerateArray();
            int first = 0;
            foreach (int end in contours.ContourEnds)
            {
                OpenTypeOutlinePoint origin = contours.Segments[first].Start;
                AssertCommand("M", [origin.X, origin.Y]);
                for (int index = first; index < end; index++)
                {
                    OpenTypeQuadraticSegment segment = contours.Segments[index];
                    // A closed native outline omits the redundant closing line.
                    if (index == end - 1 && segment.Control == segment.End && segment.End == origin) continue;
                    if (segment.Control == segment.End) AssertCommand("L", [segment.End.X, segment.End.Y]);
                    else AssertCommand("Q", [segment.Control.X, segment.Control.Y, segment.End.X, segment.End.Y]);
                }
                AssertCommand("Z", []);
                first = end;
            }
            Assert.False(commands.MoveNext());
            Assert.True(budget.Snapshot().CurrentBytes > 0);

            void AssertCommand(string verb, double[] coordinates)
            {
                Assert.True(commands.MoveNext());
                JsonElement expected = commands.Current;
                Assert.Equal(verb, expected[0].GetString());
                Assert.Equal(coordinates.Length + 1, expected.GetArrayLength());
                for (int index = 0; index < coordinates.Length; index++) Assert.Equal(expected[index + 1].GetDouble(), coordinates[index]);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Denied_metric_discovery_releases_decoded_storage_and_preserves_font_bytes(long maximumBytes)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSans-Regular.ttf"));
        byte[] original = bytes.ToArray();
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        var budget = new RenderBudget(new HostMemoryBudget(maximumBytes));

        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => OpenTypeFont.GetLatinLightContours(bytes, 36, metrics, 48, budget, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, bytes);
    }
}
