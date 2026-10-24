using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TextEncoding = System.Text.Encoding;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgTextPaintBoundsTests
{
    [Fact]
    public async Task Source_paint_boxes_match_native_ink_anchors_positions_spacing_and_font_fragments()
    {
        using SvgFontSnapshot snapshot = await CreateSnapshotAsync();
        byte[] bytes = File.ReadAllBytes(DataPath("native-vectors.json"));
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(DataPath("capture.json")));
        Assert.Equal(capture.RootElement.GetProperty("vectorSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using JsonDocument vectors = JsonDocument.Parse(bytes);
        string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
        foreach (JsonElement row in vectors.RootElement.EnumerateArray())
        {
            StringBuilder text = new("<text xml:space='preserve' fill='url(#g)'");
            foreach (JsonProperty attribute in row.GetProperty("attributes").EnumerateObject())
                text.Append(' ').Append(attribute.Name).Append("='").Append(attribute.Value.GetString()).Append('\'');
            text.Append('>');
            JsonElement markup = row.GetProperty("markup");
            text.Append(markup.ValueKind == JsonValueKind.String ? markup.GetString() : row.GetProperty("text").GetString());
            text.Append("</text>");
            byte[] input = TextEncoding.UTF8.GetBytes(Document(text.ToString()));
            RenderBudget budget = new(8_000_000);
            using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, new(10_000_000), CancellationToken.None))
            {
                SvgTextLinearShader shader = Assert.IsType<SvgTextLinearShader>(root.Geometries.ToArray()
                    .First(geometry => geometry.Kind == SvgGeometryKind.GlyphContours).Glyph.Shader);
                SvgGradientTransform box = (shader.Gradient.SourcePaint ?? throw new InvalidDataException("Missing source paint box.")).Data.Local;
                double[] actual = [box.E, box.F, box.A, box.D];
                double[] expected = row.GetProperty(platform).EnumerateArray().Select(value => value.GetDouble()).ToArray();
                Assert.True(expected.SequenceEqual(actual), row.GetProperty("id").GetString() + ": expected "
                    + JsonSerializer.Serialize(expected) + ", actual " + JsonSerializer.Serialize(actual));
            }
            Assert.Equal(0, budget.Snapshot().CurrentBytes);
        }
    }

    [Fact]
    public async Task Metric_scratch_releases_after_denial_work_exhaustion_cancellation_and_retry()
    {
        using SvgFontSnapshot snapshot = await CreateSnapshotAsync();
        RenderBudget runBudget = new(1_000_000);
        using (SvgTextRun run = CreateRun("AVj", snapshot, runBudget))
        {
            foreach (long limit in new long[] { 1, 256 })
            {
                RenderBudget denied = new(limit);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                    SvgTextLayoutBounds.Measure(run, [], SvgAffineTransform.Identity, denied, new(long.MaxValue), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
            }
            RenderBudget scratch = new(1_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
                SvgTextLayoutBounds.Measure(run, [], SvgAffineTransform.Identity, scratch, new(100), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                SvgTextLayoutBounds.Measure(run, [], SvgAffineTransform.Identity, scratch, new(long.MaxValue), cancellation.Token));
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
            SvgRectangle box = SvgTextLayoutBounds.Measure(run, [], SvgAffineTransform.Identity, scratch, new(long.MaxValue), CancellationToken.None);
            Assert.True(box.Width > 0 && box.Height > 0);
            Assert.Equal(0, scratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, runBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Repeated_glyphs_reuse_canonical_metrics_without_growing_scratch_per_letter()
    {
        using SvgFontSnapshot snapshot = await CreateSnapshotAsync();
        RenderBudget runBudget = new(4_000_000);
        using (SvgTextRun single = CreateRun("A", snapshot, runBudget))
        using (SvgTextRun repeated = CreateRun(new string('A', 1_000), snapshot, runBudget))
        {
            RenderBudget singleScratch = new(1_000_000), repeatedScratch = new(1_000_000);
            ParserWorkCounter singleWork = new(long.MaxValue), repeatedWork = new(long.MaxValue);
            SvgTextLayoutBounds.Measure(single, [], SvgAffineTransform.Identity, singleScratch, singleWork, CancellationToken.None);
            SvgTextLayoutBounds.Measure(repeated, [], SvgAffineTransform.Identity, repeatedScratch, repeatedWork, CancellationToken.None);
            Assert.InRange(repeatedWork.Used - singleWork.Used, 999, 5_000);
            Assert.Equal(singleScratch.Snapshot().PeakBytes, repeatedScratch.Snapshot().PeakBytes);
            Assert.Equal(0, singleScratch.Snapshot().CurrentBytes);
            Assert.Equal(0, repeatedScratch.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, runBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Parser_metric_cache_reuses_distinct_runs_and_retains_only_owned_values_after_cancellation()
    {
        using SvgFontSnapshot snapshot = await CreateSnapshotAsync();
        RenderBudget runBudget = new(1_000_000), scratch = new(1_000_000);
        using (SvgTextRun first = CreateRun("A", snapshot, runBudget))
        using (SvgTextRun second = CreateRun("A", snapshot, runBudget))
        using (SvgTextLayoutBounds.MetricCache cache = SvgTextLayoutBounds.MetricCache.Create(scratch))
        {
            ParserWorkCounter initial = new(long.MaxValue), repeated = new(100);
            SvgRectangle expected = SvgTextLayoutBounds.Measure(first, [], SvgAffineTransform.Identity, cache, initial, CancellationToken.None);
            Assert.Equal(expected, SvgTextLayoutBounds.Measure(second, [], SvgAffineTransform.Identity, cache, repeated, CancellationToken.None));
            Assert.True(repeated.Used < initial.Used / 4);
            long held = scratch.Snapshot().CurrentBytes;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => SvgTextLayoutBounds.Measure(second, [], SvgAffineTransform.Identity, cache, new(long.MaxValue), cancellation.Token));
            Assert.Equal(held, scratch.Snapshot().CurrentBytes);
            Assert.Equal(expected, SvgTextLayoutBounds.Measure(second, [], SvgAffineTransform.Identity, cache, new(100), CancellationToken.None));
        }
        Assert.Equal(0, runBudget.Snapshot().CurrentBytes);
        Assert.Equal(0, scratch.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task One_text_run_shares_its_scene_drawing_record_across_repeated_glyphs()
    {
        using SvgFontSnapshot snapshot = await CreateSnapshotAsync();
        byte[] input = TextEncoding.UTF8.GetBytes(Document("<text x='4' y='24' font-family='serif' font-size='24'>" + new string('A', 1_000) + "</text>"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, new(1_000_000), CancellationToken.None))
        {
            Assert.Equal(1_000, root.Geometries.Length);
            SvgRecordedPaint first = root.Geometries[0].RecordedPaint;
            Assert.True(first.Identity > 0);
            foreach (SvgGeometry glyph in root.Geometries) Assert.Equal(first, glyph.RecordedPaint);
            SvgSceneStorage storage = root.SceneStorage ?? throw new InvalidDataException("Missing drawing records.");
            SvgRecordedPaintValue drawing = Assert.Single(storage.RecordedPaintValues);
            Assert.NotNull(drawing.VisualBounds);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Fitted_spacing_shares_one_source_box_across_fill_and_stroke_fragments()
    {
        using SvgFontSnapshot snapshot = await CreateSnapshotAsync();
        byte[] input = TextEncoding.UTF8.GetBytes(Document("<text x='4' y='24' font-family='serif' font-size='24' textLength='4000' fill='url(#g)' stroke='url(#g)' stroke-width='.25'>" + new string('A', 128) + "</text>"));
        RenderBudget budget = new(8_000_000);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, new(300_000), CancellationToken.None))
            Assert.Equal(256, root.Geometries.Length);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static SvgTextRun CreateRun(string text, SvgFontSnapshot snapshot, RenderBudget budget)
    {
        SvgTextStyle style = new(new("serif", 400, false), 24, 0, 0, 0, 0, 0, SvgTextAnchor.Start, "");
        SvgTextGlyphPaint paint = new(new(0, 0, 0, 255), true) { FillServer = "url(#g)" };
        SvgGlyphDeviceMetrics metrics = new(1, OperatingSystem.IsWindows() ? SvgGlyphMetricTarget.Windows : SvgGlyphMetricTarget.Linux, false);
        return SvgTextRunBuilder.Create(style, text, [], [], [], paint, metrics, snapshot, budget, new(long.MaxValue), CancellationToken.None);
    }

    private static ValueTask<SvgFontSnapshot> CreateSnapshotAsync()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(DataPath("font-pack.json")), "r99-fonts");
        byte[] input = "<svg xmlns='http://www.w3.org/2000/svg'><text font-family='serif'>A</text><text font-family='monospace' font-weight='700'>A</text></svg>"u8.ToArray();
        return SvgFontSnapshot.CreateAsync(input, catalog, new FontPrograms(), CancellationToken.None);
    }

    private static string Document(string text) => "<svg xmlns='http://www.w3.org/2000/svg' width='256' height='256'><defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs>" + text + "</svg>";
    private static string DataPath(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", file);

    private sealed class FontPrograms : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath, File.ReadAllBytes(DataPath(file.RelativePath))));
        }
    }
}
