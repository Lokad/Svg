using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> WindowsLegacyDeltaTargetCases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "windows-conditional-targets", "native-targets.json")));
        foreach (JsonElement font in data.RootElement.GetProperty("fonts").EnumerateArray())
        {
            string file = Path.GetFileName(font.GetProperty("font").GetString() ?? throw new InvalidDataException("Missing font identity."));
            bool symmetric = font.GetProperty("profile").GetString() switch
            {
                "natural" => false,
                "symmetric" => true,
                _ => throw new InvalidDataException("Unknown native LCD profile.")
            };
            foreach (JsonElement item in font.GetProperty("cases").EnumerateArray())
                yield return [file, item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case identity."),
                    symmetric, item.GetProperty("glyph").GetUInt16(), item.GetProperty("nativeValue").GetInt32()];
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(WindowsLegacyDeltaTargetCases))]
    public void Windows_conditional_functions_share_legacy_delta_target_rules(string file, string id, bool symmetric, ushort glyph, int expected)
    {
        Assert.NotEmpty(id);
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-conditional-targets", file));
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        OpenTypeWindowsGlyphProfile profile = OpenTypeFont.GetWindowsGlyphProfile(bytes, metrics, 32, CancellationToken.None);
        Assert.Equal(symmetric ? SvgGlyphLcdMode.Symmetric : SvgGlyphLcdMode.Natural, profile.LcdMode);
        Assert.True(profile.GridFit);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetTrueTypeContours(bytes, glyph, metrics, 32,
            profile.CreateHintTarget(1), TrueTypeImpliedPointRounding.None, budget, CancellationToken.None))
        {
            // The owned font transports the selected coordinate into an integer
            // four-pixel band after executing the independently captured program.
            Assert.Equal(expected * 64, contours.Segments.Min(segment => Math.Min(segment.Start.Y, segment.End.Y)));
            Assert.Equal((expected + 4) * 64, contours.Segments.Max(segment => Math.Max(segment.Start.Y, segment.End.Y)));
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
