using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgWindowsGlyphTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");

    [Theory]
    [InlineData("curved-A")]
    [InlineData("document-0")]
    [InlineData("document-120")]
    [InlineData("document-251")]
    [InlineData("document-298")]
    [InlineData("document-214")]
    [InlineData("document-316")]
    [InlineData("document-30")]
    [InlineData("document-49")]
    [InlineData("document-50")]
    [InlineData("document-57")]
    [InlineData("document-97")]
    [InlineData("document-156")]
    public async Task Mapped_windows_masks_match_complete_independent_native_textures(string identity)
    {
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "curved-font-documents", "diagnostics", "windows-mapped-masks.json")));
        JsonElement item = native.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == identity);
        using SvgFontSnapshot snapshot = await Prepare();
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        byte[] original = font.Bytes.ToArray();
        RenderBudget budget = new(long.MaxValue);
        JsonElement matrix = item.GetProperty("map"), bounds = item.GetProperty("coverageBounds");
        SvgAffineTransform map = new(matrix[0].GetDouble(), matrix[1].GetDouble(), matrix[2].GetDouble(), matrix[3].GetDouble(), matrix[4].GetDouble(), matrix[5].GetDouble());
        int ox = (int)Math.Floor(map.E), oy = (int)Math.Floor(map.F);
        int left = bounds[0].GetInt32(), top = bounds[1].GetInt32(), right = bounds[2].GetInt32(), bottom = bounds[3].GetInt32();
        byte[] expectedBytes = Convert.FromBase64String(item.GetProperty("rgbTextureBase64").GetString() ?? throw new InvalidDataException("Missing native texture."));
        Assert.Equal((right - left) * (bottom - top) * 3, expectedBytes.Length);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(item.GetProperty("glyph").GetUInt16(), 0, 0, 0), item.GetProperty("deviceSize").GetDouble(), map, budget))
        using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None))
        {
            using RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(contours.Segments.Length * 2), SvgMemoryCategory.Temporary);
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[contours.Segments.Length * 2];
            for (int y = top - 1; y <= bottom; y++)
            for (int x = left - 1; x <= right; x++)
            {
                SvgGlyphRgbCoverage expected = default;
                if (x >= left && x < right && y >= top && y < bottom)
                {
                    int offset = ((y - top) * (right - left) + x - left) * 3;
                    expected = new(expectedBytes[offset], expectedBytes[offset + 1], expectedBytes[offset + 2]);
                }
                SvgGlyphRgbCoverage actual = SvgGlyphLcdCoverage.GetCoverage(contours.Segments, ox + x, oy + y, scratch, contours.SmartDropouts, new(long.MaxValue), CancellationToken.None);
                Assert.True(expected == actual, $"{identity} ({x}, {y}): expected {expected}, actual {actual}");
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, font.Bytes.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Denied_windows_curve_subdivision_releases_its_owner_and_preserves_the_borrow(int boundary)
    {
        OpenTypeQuadraticSegment[] segments = [new(new(1, 1), new(18, -3), new(2, 20))];
        using OpenTypeGlyphContourStorage original = new(segments, [1], null, null);
        RenderBudget budget = new(boundary == 0 ? 100 : long.MaxValue);
        using CancellationTokenSource cancellation = new();
        bool observed = false;
        using (GlyphDecoderBudgetProbe.Arm((phase, _) =>
        {
            if (boundary == 2 && phase == GlyphDecoderBudgetPhase.ExpandedSegmentCapacity)
            { observed = true; cancellation.Cancel(); }
        }))
        {
            if (boundary == 2)
                Assert.ThrowsAny<OperationCanceledException>(() => SvgWindowsGlyphCurves.Prepare(original, budget, new(long.MaxValue), cancellation.Token));
            else
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgWindowsGlyphCurves.Prepare(original, budget, new(boundary == 1 ? 4 : long.MaxValue), CancellationToken.None)).ErrorCode);
        }
        Assert.Equal(boundary == 2, observed);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(new(new(1, 1), new(18, -3), new(2, 20)), original.Segments[0]);
        RenderBudget retryBudget = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage retry = SvgWindowsGlyphCurves.Prepare(original, retryBudget, new(long.MaxValue), CancellationToken.None))
            Assert.True(retry.Segments.Length > original.Segments.Length);
        Assert.Equal(0, retryBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Collapsed_tips_and_bounded_dropouts_match_complete_independent_native_textures()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "diagnostics", "windows-smart-stubs.json");
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(6, native.RootElement.GetProperty("originalCases").GetArrayLength());
        foreach (JsonElement item in native.RootElement.GetProperty("originalCases").EnumerateArray())
        {
            JsonElement origin = item.GetProperty("origin");
            OpenTypeOutlinePoint[] points = item.GetProperty("pointsF26").EnumerateArray()
                .Select(point => new OpenTypeOutlinePoint(origin[0].GetDouble() + point[0].GetDouble() / 64, origin[1].GetDouble() - point[1].GetDouble() / 64)).ToArray();
            int[] flags = item.GetProperty("onCurve").EnumerateArray().Select(flag => flag.GetInt32()).ToArray();
            List<OpenTypeQuadraticSegment> segments = []; int first = 0;
            foreach (JsonElement endpoint in item.GetProperty("contourEnds").EnumerateArray())
            {
                int end = endpoint.GetInt32(), count = end - first + 1, start = first;
                while (start <= end && flags[start] == 0) start++;
                Assert.True(start <= end);
                OpenTypeOutlinePoint current = points[start];
                for (int offset = 1; offset <= count; offset++)
                {
                    int index = first + (start - first + offset) % count;
                    OpenTypeOutlinePoint next = points[index];
                    if (flags[index] != 0) { segments.Add(new(current, next, next)); current = next; }
                    else
                    {
                        int following = first + (start - first + offset + 1) % count;
                        OpenTypeOutlinePoint destination = points[following];
                        if (flags[following] != 0) offset++;
                        else destination = new((next.X + destination.X) / 2, (next.Y + destination.Y) / 2);
                        segments.Add(new(current, next, destination)); current = destination;
                    }
                }
                first = end + 1;
            }
            AssertTexture(item, segments.ToArray(), item.GetProperty("scanType").GetInt32() == 4);
        }
        using SvgFontSnapshot snapshot = await PrepareMono();
        Assert.Equal(6, native.RootElement.GetProperty("suppliedFontCases").GetArrayLength());
        foreach (JsonElement item in native.RootElement.GetProperty("suppliedFontCases").EnumerateArray())
        {
            var budget = new RenderBudget(long.MaxValue);
            JsonElement origin = item.GetProperty("origin");
            using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(item.GetProperty("glyph").GetUInt16(), origin[0].GetDouble(), origin[1].GetDouble(), 0), item.GetProperty("deviceSize").GetDouble(), budget))
            using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None))
            {
                Assert.True(contours.SmartDropouts);
                AssertTexture(item, contours.Segments, contours.SmartDropouts);
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }

        static void AssertTexture(JsonElement item, ReadOnlySpan<OpenTypeQuadraticSegment> segments, bool dropouts)
        {
            byte[] expectedBytes = Convert.FromBase64String(item.GetProperty("rgbTextureBase64").GetString() ?? throw new InvalidDataException("Missing native texture."));
            JsonElement bounds = item.GetProperty("coverageBounds");
            int left = bounds[0].GetInt32(), top = bounds[1].GetInt32(), right = bounds[2].GetInt32(), bottom = bounds[3].GetInt32();
            Assert.Equal((right - left) * (bottom - top) * 3, expectedBytes.Length);
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
            for (int y = top - 1; y <= bottom; y++)
            for (int x = left - 1; x <= right; x++)
            {
                SvgGlyphRgbCoverage expected = default;
                if (x >= left && x < right && y >= top && y < bottom)
                {
                    int offset = ((y - top) * (right - left) + x - left) * 3;
                    expected = new(expectedBytes[offset], expectedBytes[offset + 1], expectedBytes[offset + 2]);
                }
                SvgGlyphRgbCoverage actual = SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, dropouts, new(long.MaxValue), CancellationToken.None);
                Assert.True(expected == actual, $"{item.GetProperty("id").GetString()} ({x}, {y}): expected {expected}, actual {actual}");
            }
        }
    }

    [Fact]
    public async Task Cancellation_at_windows_cell_coverage_releases_fitted_contours_and_dropout_scratch()
    {
        if (!OperatingSystem.IsWindows()) return;
        using SvgFontSnapshot snapshot = await PrepareMono();
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "scalar-x-reset.svg"));
        var budget = new RenderBudget(long.MaxValue);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
        {
            long baseline = budget.Snapshot().CurrentBytes;
            using var cancellation = new CancellationTokenSource();
            bool started = false;
            using (GlyphCoverageProbe.Arm(() => { started = true; cancellation.Cancel(); }))
                Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.ConvertToPng(root, new(new SvgViewport(64, 64), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), budget, cancellation.Token));
            Assert.True(started);
            Assert.Equal(baseline, budget.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

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
            using RenderBudget.BudgetReservation scratchReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(contours.Segments.Length * 2), SvgMemoryCategory.Temporary);
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[contours.Segments.Length * 2];
            for (int y = 0; y < reference.Height; y++)
            for (int x = 0; x < reference.Width; x++)
            {
                SvgGlyphRgbCoverage coverage = SvgGlyphLcdCoverage.GetCoverage(contours.Segments, 7 + x, 14 + y, scratch, contours.SmartDropouts, new(long.MaxValue), CancellationToken.None);
                int offset = (y * reference.Width + x) * 4;
                Assert.Equal(reference.Pixels[offset], coverage.Red);
                Assert.Equal(reference.Pixels[offset + 1], coverage.Green);
                Assert.Equal(reference.Pixels[offset + 2], coverage.Blue);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Mono_quarter_strikes_and_smart_dropout_strike_match_independent_native_rgb_masks()
    {
        using SvgFontSnapshot snapshot = await PrepareMono();
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "diagnostics");
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "windows-glyph-coverage.json")));
        foreach (JsonElement item in native.RootElement.GetProperty("coverageCases").EnumerateArray()
            .Concat(native.RootElement.GetProperty("smartDropoutCases").EnumerateArray())
            .Concat(native.RootElement.GetProperty("directedBoundaryCases").EnumerateArray()))
        {
            var budget = new RenderBudget(long.MaxValue);
            JsonElement origin = item.GetProperty("origin"), bounds = item.GetProperty("coverageBounds");
            using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(Assert.Single(snapshot.Fonts), new(item.GetProperty("glyph").GetUInt16(), origin[0].GetDouble(), origin[1].GetDouble(), 0), item.GetProperty("deviceSize").GetDouble(), budget))
            using (OpenTypeGlyphContourStorage contours = source.CreateWindowsContours(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None))
            {
                Assert.True(contours.SmartDropouts);
                using RenderBudget.BudgetReservation scratchReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGlyphLcdCoverage.Crossing>(contours.Segments.Length * 2), SvgMemoryCategory.Temporary);
                SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[contours.Segments.Length * 2];
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
                    Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(contours.Segments, x, y, scratch, contours.SmartDropouts, new(long.MaxValue), CancellationToken.None));
                }
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public void Directed_contours_match_complete_independent_native_boundary_textures()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "diagnostics");
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "windows-contour-boundaries.json")));
        foreach (JsonElement item in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            JsonElement origin = item.GetProperty("origin"), bounds = item.GetProperty("coverageBounds");
            OpenTypeOutlinePoint[] points = item.GetProperty("pointsF26").EnumerateArray()
                .Select(p => new OpenTypeOutlinePoint(origin[0].GetDouble() + p[0].GetDouble() / 64, origin[1].GetDouble() - p[1].GetDouble() / 64)).ToArray();
            int[] flags = item.GetProperty("onCurve").EnumerateArray().Select(p => p.GetInt32()).ToArray();
            List<OpenTypeQuadraticSegment> segments = []; int first = 0;
            foreach (JsonElement endpoint in item.GetProperty("contourEnds").EnumerateArray())
            {
                int end = endpoint.GetInt32(); Assert.Equal(1, flags[first]);
                OpenTypeOutlinePoint start = points[first], current = start;
                for (int index = first + 1; index <= end; index++)
                {
                    OpenTypeOutlinePoint next = points[index];
                    if (flags[index] != 0) { segments.Add(new(current, next, next)); current = next; }
                    else
                    {
                        int destination = index == end ? first : index + 1; Assert.Equal(1, flags[destination]);
                        segments.Add(new(current, next, points[destination])); current = points[destination]; index++;
                    }
                }
                if (current != start) segments.Add(new(current, start, start));
                first = end + 1;
            }
            byte[] expectedBytes = Convert.FromBase64String(item.GetProperty("rgbTextureBase64").GetString() ?? throw new InvalidDataException("Missing native texture."));
            int left = bounds[0].GetInt32(), top = bounds[1].GetInt32(), right = bounds[2].GetInt32(), bottom = bounds[3].GetInt32();
            Assert.Equal((right - left) * (bottom - top) * 3, expectedBytes.Length);
            OpenTypeQuadraticSegment[] outline = segments.ToArray();
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[outline.Length * 2];
            for (int y = top - 1; y <= bottom; y++)
            for (int x = left - 1; x <= right; x++)
            {
                SvgGlyphRgbCoverage expected = default;
                if (x >= left && x < right && y >= top && y < bottom)
                {
                    int offset = ((y - top) * (right - left) + x - left) * 3;
                    expected = new(expectedBytes[offset], expectedBytes[offset + 1], expectedBytes[offset + 2]);
                }
                Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(outline, x, y, scratch, true, new(long.MaxValue), CancellationToken.None));
            }
        }
    }

    [Fact]
    public void Coefficient_precision_and_implied_points_match_complete_translated_native_curve_textures()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents", "diagnostics");
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "windows-curve-cells.json")));
        JsonElement[] cases = native.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(5, cases.Length);
        foreach (JsonElement item in cases)
        foreach (JsonElement texture in item.GetProperty("nativeTextures").EnumerateArray())
        {
            double originX = 8 + texture.GetProperty("xCellPhase").GetInt32() / 6.0;
            double originY = 256 + texture.GetProperty("yCellPhase").GetInt32() / 5.0;
            OpenTypeOutlinePoint[] points = item.GetProperty("pointsF26").EnumerateArray()
                .Select(point => new OpenTypeOutlinePoint(originX + point[0].GetDouble() / 64, originY - point[1].GetDouble() / 64)).ToArray();
            int[] flags = item.GetProperty("onCurve").EnumerateArray().Select(flag => flag.GetInt32()).ToArray();
            var segments = new List<OpenTypeQuadraticSegment>();
            OpenTypeOutlinePoint current = points[0];
            for (int index = 1; index < points.Length; index++)
            {
                OpenTypeOutlinePoint next = points[index];
                if (flags[index] != 0) { segments.Add(new(current, next, next)); current = next; }
                else
                {
                    OpenTypeOutlinePoint following = index + 1 < points.Length ? points[index + 1] : points[0];
                    if (index + 1 == points.Length || flags[index + 1] != 0)
                    { segments.Add(new(current, next, following)); current = following; index++; }
                    else
                    {
                        OpenTypeOutlinePoint middle = new((next.X + following.X) / 2, (next.Y + following.Y) / 2);
                        segments.Add(new(current, next, middle)); current = middle;
                    }
                }
            }
            if (current != points[0]) segments.Add(new(current, points[0], points[0]));
            OpenTypeQuadraticSegment[] outline = segments.ToArray();
            SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[outline.Length * 2];
            byte[] bytes = Convert.FromBase64String(texture.GetProperty("rgbTextureBase64").GetString() ?? throw new InvalidDataException("Missing native curve texture."));
            JsonElement bounds = texture.GetProperty("coverageBounds");
            int left = bounds[0].GetInt32(), top = bounds[1].GetInt32(), right = bounds[2].GetInt32(), bottom = bounds[3].GetInt32();
            Assert.Equal((right - left) * (bottom - top) * 3, bytes.Length);
            for (int y = top - 1; y <= bottom; y++)
            for (int x = left - 1; x <= right; x++)
            {
                SvgGlyphRgbCoverage expected = default;
                if (x >= left && x < right && y >= top && y < bottom)
                {
                    int offset = ((y - top) * (right - left) + x - left) * 3;
                    expected = new(bytes[offset], bytes[offset + 1], bytes[offset + 2]);
                }
                Assert.Equal(expected, SvgGlyphLcdCoverage.GetCoverage(outline, x, y, scratch, false, new(long.MaxValue), CancellationToken.None));
            }
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
            long retained = budget.Snapshot().CurrentBytes;
            Assert.True(source.HasTrueTypePrograms(new(long.MaxValue), new(long.MaxValue), CancellationToken.None));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.CreateWindowsContours(boundary == 2 ? double.PositiveInfinity : 1, 0, 1, 0, budget, new(boundary == 1 ? 5000 : long.MaxValue), CancellationToken.None)).ErrorCode);
            Assert.Equal(retained, budget.Snapshot().CurrentBytes);
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
