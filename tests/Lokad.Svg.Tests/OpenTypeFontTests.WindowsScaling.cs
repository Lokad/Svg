using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> WindowsFontUnitNativeCases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-font-unit-scaling", "native-vectors.json")));
        foreach (JsonElement item in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("font").GetString() ?? throw new InvalidDataException(), item.GetProperty("glyph").GetUInt16(), item.GetProperty("size").GetDouble(), item.GetProperty("nativeValue").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(WindowsFontUnitNativeCases))]
    public void Windows_font_unit_scaling_matches_native_points_controls_writes_and_distances(string file, ushort glyph, double size, int expected)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-font-unit-scaling", file)), unchanged = bytes.ToArray();
        TrueTypeHintTarget target = new(42, TrueTypeHintQueryFlags.ClearType | TrueTypeHintQueryFlags.SubpixelPositioned | TrueTypeHintQueryFlags.Symmetric,
            TrueTypeHintControlScale.FullPrecision, TrueTypeHintPreparation.Single) { Grid = TrueTypeHintGrid.ClearType, WindowsArithmetic = true };
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, glyph, OpenTypeFont.ParseMetrics(bytes, CancellationToken.None), size, target, budget, CancellationToken.None))
        {
            // The original program amplifies each coordinate unit to a pixel
            // and draws a four-pixel band. Native bounds independently encode
            // the value before fitting, after CVT reads/writes, or from MD[1].
            Assert.NotEmpty(contours.Segments);
            Assert.Equal(expected * 64, contours.Segments.Min(segment => Math.Min(segment.Start.Y, segment.End.Y)));
            Assert.Equal((expected + 4) * 64, contours.Segments.Max(segment => Math.Max(segment.Start.Y, segment.End.Y)));
        }
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
