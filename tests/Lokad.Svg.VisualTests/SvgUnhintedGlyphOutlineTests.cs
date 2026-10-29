using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

/// <summary>Checks immutable native outlines independently of the final matching-platform pixel controls.</summary>
public sealed class SvgUnhintedGlyphOutlineTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "curved-font-documents");

    [Theory]
    [InlineData(36, 20)]
    [InlineData(191, 20)]
    [InlineData(36, 20.48)]
    [InlineData(191, 20.48)]
    [InlineData(36, 19.994983673095703)]
    [InlineData(191, 19.994983673095703)]
    public void Device_contours_match_the_independent_native_point_loader(int glyph, double size)
    {
        using JsonDocument diagnostic = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "unhinted-device-outlines.json")));
        JsonElement root = diagnostic.RootElement;
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels", "fonts", "NotoSans-Regular.ttf"));
        Assert.Equal(root.GetProperty("fontProgramSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        JsonElement expected = root.GetProperty("outlines").EnumerateArray().Single(x => x.GetProperty("glyph").GetInt32() == glyph && x.GetProperty("size").GetDouble() == size);
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        RenderBudget budget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetUnhintedDeviceContours(bytes, (ushort)glyph, metrics, size, budget, CancellationToken.None))
        {
            Assert.Equal(expected.GetProperty("ends").EnumerateArray().Select(x => x.GetInt32()), contours.ContourEnds);
            double[][] segments = contours.Segments.Select(s => new[] { s.Start.X / 64, s.Start.Y / 64, s.Control.X / 64, s.Control.Y / 64, s.End.X / 64, s.End.Y / 64 }).ToArray();
            Assert.Equal(expected.GetProperty("segments").GetArrayLength(), segments.Length);
            int index = 0;
            foreach (JsonElement segment in expected.GetProperty("segments").EnumerateArray())
                Assert.Equal(segment.EnumerateArray().Select(x => x.GetDouble()), segments[index++]);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Curved_strike_uses_the_native_relaxed_map_and_unhinted_vertical_size()
    {
        using JsonDocument diagnostic = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "unhinted-device-outlines.json")));
        JsonElement expected = diagnostic.RootElement.GetProperty("curvedStrike");
        double angle = expected.GetProperty("rotationDegrees").GetDouble() * Math.PI / 180;
        SvgGlyphStrikeTransform strike = SvgGlyphStrikeTransform.Create(20, new(Math.Cos(angle), Math.Sin(angle), -Math.Sin(angle), Math.Cos(angle), 0, 0));
        Assert.Equal(expected.GetProperty("deviceSize").GetSingle(), strike.DeviceSize);
        Assert.Equal(expected.GetProperty("relaxedMap").EnumerateArray().Select(x => x.GetDouble()), new[] { strike.RelaxedMap.A, strike.RelaxedMap.B, strike.RelaxedMap.C, strike.RelaxedMap.D });
        Assert.Equal(expected.GetProperty("remainingMap").EnumerateArray().Select(x => x.GetDouble()), new[] { strike.RemainingMap.A, strike.RemainingMap.B, strike.RemainingMap.C, strike.RemainingMap.D });
        Assert.Equal(3, expected.GetProperty("captures").GetArrayLength());
        foreach (JsonElement capture in expected.GetProperty("captures").EnumerateArray())
        {
            Assert.Equal("healthy", capture.GetProperty("startupHealth").GetString());
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            Assert.Equal(0, capture.GetProperty("preblendedDifferingPixels").GetInt32());
        }
    }
}
