using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> WindowsParentMetricMasks()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-font-unit-scaling", "phantom-origins.json")));
        foreach (JsonElement item in data.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (!item.TryGetProperty("mask", out JsonElement mask)) continue;
            int[] bounds = mask.GetProperty("bounds").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            yield return [item.GetProperty("glyph").GetUInt16(), item.GetProperty("horizontalRatio").GetDouble(), bounds[0], bounds[1], bounds[2] - bounds[0], bounds[3] - bounds[1],
                item.GetProperty("nativeAlphaSha256").GetString() ?? throw new InvalidDataException("Missing native mask identity."),
                mask.GetProperty("base64").GetString() ?? throw new InvalidDataException("Missing native mask bytes.")];
        }
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(WindowsParentMetricMasks))]
    public async Task Windows_parent_programs_keep_the_selected_child_paint_origin(ushort glyph, double ratio, int left, int top, int width, int height, string digest, string native)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "windows-font-unit-scaling", "PhantomOrigins.ttf")), unchanged = bytes.ToArray();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync([CreateResolution(bytes)], CancellationToken.None);
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(fonts.Fonts[0], new(glyph, 0, 0, 0), 22, new(ratio, 0, 0, 1, 0, 0), budget))
        using (OpenTypeGlyphContourStorage scan = source.CreateWindowsScanContours(1, 0, 1, 0, budget, CancellationToken.None))
        {
            byte[] expected = Convert.FromBase64String(native), actual = new byte[checked(width * height * 3)];
            Assert.Equal(actual.Length, expected.Length);
            Assert.Equal(digest, Convert.ToHexStringLower(SHA256.HashData(expected)));
            SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(scan.Segments, scan.SmartDropouts, scan.SimpleDropouts, scan.ExcludeDropoutStubs, scan.WindowsLcdMode, CancellationToken.None);
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[scan.Segments.Length * 2];
            int index = 0;
            for (int y = top; y < top + height; y++)
                for (int x = left; x < left + width; x++)
                {
                    SvgGlyphRgbCoverage pixel = SvgGlyphLcdCoverage.GetCoverage(scan.Segments, x, y, scratch, bounds, CancellationToken.None);
                    actual[index++] = pixel.Red; actual[index++] = pixel.Green; actual[index++] = pixel.Blue;
                }
            Assert.Equal(expected, actual);
        }
        Assert.Equal(unchanged, bytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
