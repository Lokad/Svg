using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgWindowsGlyphTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");

    [Fact]
    public async Task Computed_windows_outline_and_rgb_mask_match_independent_native_measurements()
    {
        using SvgFontSnapshot snapshot = await Prepare();
        var budget = new RenderBudget(long.MaxValue);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(36, 8, 48, 30.672), 48, budget))
        using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None))
        {
            using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "windows-lcd.json")));
            foreach (JsonElement point in native.RootElement.GetProperty("fittedPointPositions").EnumerateArray())
            {
                OpenTypeOutlinePoint positioned = new(8 + point[0].GetDouble() / 64, 48 - point[1].GetDouble() / 64);
                Assert.Contains(contours.Segments, segment => segment.Start == positioned || segment.Control == positioned || segment.End == positioned);
            }
            RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "windows-A48-rgb-coverage.png")), 1_088);
            Assert.Equal(32, reference.Width); Assert.Equal(34, reference.Height);
            for (int y = 0; y < reference.Height; y++)
            for (int x = 0; x < reference.Width; x++)
            {
                SvgGlyphRgbCoverage coverage = SvgGlyphLcdCoverage.GetCoverage(contours.Segments, 7 + x, 14 + y, new(long.MaxValue), CancellationToken.None);
                int offset = (y * reference.Width + x) * 4;
                Assert.Equal(reference.Pixels[offset], coverage.Red);
                Assert.Equal(reference.Pixels[offset + 1], coverage.Green);
                Assert.Equal(reference.Pixels[offset + 2], coverage.Blue);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Fractional_mono_strike_at_each_quarter_matches_independent_native_rgb_masks()
    {
        using SvgFontSnapshot snapshot = await PrepareMono();
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "diagnostics");
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "windows-glyph-coverage.json")));
        foreach (JsonElement item in native.RootElement.GetProperty("coverageCases").EnumerateArray())
        {
            var budget = new RenderBudget(long.MaxValue);
            JsonElement origin = item.GetProperty("origin"), bounds = item.GetProperty("coverageBounds");
            using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(item.GetProperty("glyph").GetUInt16(), origin[0].GetDouble(), origin[1].GetDouble(), 0), item.GetProperty("deviceSize").GetDouble(), budget))
            using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None))
            {
                string filename = item.GetProperty("coverage").GetString() ?? throw new InvalidDataException("Missing native RGB texture.");
                RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(directory, filename)), 256);
                int left = bounds[0].GetInt32(), top = bounds[1].GetInt32(), right = bounds[2].GetInt32(), bottom = bounds[3].GetInt32();
                Assert.Equal(right - left, reference.Width); Assert.Equal(bottom - top, reference.Height);
                for (int y = top - 1; y <= bottom; y++)
                for (int x = left - 1; x <= right; x++)
                {
                    SvgGlyphRgbCoverage expected = default;
                    if (x >= left && x < right && y >= top && y < bottom)
                    {
                        int offset = ((y - top) * reference.Width + x - left) * 4;
                        expected = new(reference.Pixels[offset], reference.Pixels[offset + 1], reference.Pixels[offset + 2]);
                    }
                    Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(contours.Segments, x, y, new(long.MaxValue), CancellationToken.None));
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public async Task Windows_implied_curve_midpoints_retain_half_f26_units()
    {
        using SvgFontSnapshot snapshot = await PrepareMono();
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "diagnostics", "windows-glyph-coverage.json")));
        foreach (JsonElement item in native.RootElement.GetProperty("impliedMidpointCases").EnumerateArray())
        {
            var budget = new RenderBudget(long.MaxValue);
            JsonElement origin = item.GetProperty("origin"), points = item.GetProperty("nativeFittedF26Points");
            using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(item.GetProperty("glyph").GetUInt16(), origin[0].GetDouble(), origin[1].GetDouble(), 0), item.GetProperty("deviceSize").GetDouble(), budget))
            using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None))
            {
                OpenTypeOutlinePoint expected = new(origin[0].GetDouble() + (points[0][0].GetDouble() + points[1][0].GetDouble()) / 128, origin[1].GetDouble() - (points[0][1].GetDouble() + points[1][1].GetDouble()) / 128);
                Assert.Contains(contours.Segments, segment => segment.Start == expected);
                Assert.Contains(contours.Segments, segment => segment.End == expected);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Denied_windows_outline_storage_work_or_mapping_releases_scratch(int boundary)
    {
        using SvgFontSnapshot snapshot = await Prepare();
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        byte[] original = font.Bytes.ToArray();
        var budget = new RenderBudget(boundary == 0 ? 200 : long.MaxValue);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(36, 8, 48, 30.672), 48, budget))
        {
            Assert.True(source.HasTrueTypePrograms(new(long.MaxValue), new(long.MaxValue), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.CreateWindowsContours(boundary == 2 ? double.PositiveInfinity : 1, 0, 1, 0, budget, new(boundary == 1 ? 5000 : long.MaxValue), CancellationToken.None)).ErrorCode);
            Assert.Equal(128, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, font.Bytes.ToArray());
    }

    private static ValueTask<SvgFontSnapshot> Prepare()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "logo-live-text.svg"));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-sans");
        return SvgFontSnapshot.CreateAsync(input, catalog, new MemoryProvider(File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSans-Regular.ttf")), "public-noto-sans"), CancellationToken.None);
    }

    private static ValueTask<SvgFontSnapshot> PrepareMono()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, "svg-translate-textpath.svg"));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), "public-noto-sans-mono");
        return SvgFontSnapshot.CreateAsync(input, catalog, new MemoryProvider(File.ReadAllBytes(Path.Combine(directory, "fonts", "NotoSansMono-Bold.ttf")), "public-noto-sans-mono"), CancellationToken.None);
    }

    /// <summary>Borrows the supplied immutable test face, observes cancellation and performs no host discovery; concurrent reads are safe.</summary>
    private sealed class MemoryProvider(byte[] bytes, string stableId) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(stableId, bytes));
        }
    }
}
