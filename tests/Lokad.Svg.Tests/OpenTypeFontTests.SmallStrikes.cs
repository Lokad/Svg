using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed partial class OpenTypeFontTests
{
    public static IEnumerable<object[]> WindowsSmallStrikeCases()
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "windows-small-strikes", "native-masks.json")));
        foreach (JsonElement item in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native case identity.")];
    }

    [Theory]
    [Trait("Layer", "Mechanism")]
    [MemberData(nameof(WindowsSmallStrikeCases))]
    public void Windows_raster_size_governs_program_execution_and_default_scanning(string id)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "TestData", "windows-small-strikes");
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "native-masks.json")));
        JsonElement root = data.RootElement;
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        string file = item.GetProperty("font").GetString() ?? throw new InvalidDataException("Missing font identity.");
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, Path.GetFileName(file)));
        JsonElement font = root.GetProperty("fonts").EnumerateArray().Single(x => x.GetProperty("file").GetString() == file);
        Assert.Equal(font.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        double size = item.GetProperty("deviceSize").GetDouble(), ratio = item.GetProperty("horizontalRatio").GetDouble();
        ushort glyph = item.GetProperty("glyph").GetUInt16();
        SvgGlyphLcdMode mode = item.GetProperty("profile").GetString() switch
        {
            "natural" => SvgGlyphLcdMode.Natural,
            "symmetric" => SvgGlyphLcdMode.Symmetric,
            _ => throw new InvalidDataException("Unknown native LCD profile.")
        };
        byte[] expected = Convert.FromBase64String(item.GetProperty("nativeAlphaBase64").GetString() ?? throw new InvalidDataException("Missing native mask."));
        Assert.Equal(item.GetProperty("nativeAlphaSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(expected)));
        RenderBudget budget = new(new HostMemoryBudget(long.MaxValue));
        using (OpenTypeGlyphContourStorage contours = item.GetProperty("unfitted").GetBoolean()
            ? OpenTypeFont.GetWindowsUnfittedContours(bytes, glyph, metrics, size, ratio, null, budget, CancellationToken.None)
            : OpenTypeFont.GetTrueTypeContours(bytes, glyph, metrics, size,
                new OpenTypeWindowsGlyphProfile(mode, true).CreateHintTarget(ratio), TrueTypeImpliedPointRounding.None, budget, CancellationToken.None))
        {
            double originX = root.GetProperty("origin").GetProperty("x").GetDouble();
            double originY = root.GetProperty("origin").GetProperty("y").GetDouble();
            for (int index = 0; index < contours.Segments.Length; index++)
            {
                OpenTypeQuadraticSegment segment = contours.Segments[index];
                contours.Segments[index] = new(Map(segment.Start), Map(segment.Control), Map(segment.End));
            }
            contours.WindowsLcdMode = mode;
            contours.ExtendWindowsLcdSpans = metrics.ExtendWindowsLcdSpans;
            using OpenTypeGlyphContourStorage scan = SvgWindowsGlyphCurves.Prepare(contours, budget, CancellationToken.None);
            SvgGlyphLcdCoverage.DropoutBounds bounds = SvgGlyphLcdCoverage.PrepareBounds(scan.Segments,
                scan.SmartDropouts, scan.SimpleDropouts, scan.ExcludeDropoutStubs, mode, CancellationToken.None)
                with { ExtendRight = scan.ExtendWindowsLcdSpans };
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[scan.Segments.Length * 2];
            JsonElement region = root.GetProperty("region");
            int left = region.GetProperty("left").GetInt32(), top = region.GetProperty("top").GetInt32();
            int width = region.GetProperty("width").GetInt32(), height = region.GetProperty("height").GetInt32();
            byte[] actual = new byte[checked(width * height * 3)];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    SvgGlyphRgbCoverage value = SvgGlyphLcdCoverage.GetCoverage(scan.Segments, left + x, top + y, scratch, bounds, CancellationToken.None);
                    int offset = (y * width + x) * 3;
                    actual[offset] = value.Red; actual[offset + 1] = value.Green; actual[offset + 2] = value.Blue;
                }
            Assert.Equal(expected, actual);

            OpenTypeOutlinePoint Map(OpenTypeOutlinePoint point) => new(originX + point.X / 64, originY - point.Y / 64);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }
}
