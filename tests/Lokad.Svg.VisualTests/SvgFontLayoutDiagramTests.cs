using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Retains the complete declared layout guide and orthogonal missing-semantic witnesses.</summary>
public sealed class SvgFontLayoutDiagramTests
{
    private static string DirectoryPath => Path.Combine(ResearchCorpus.Root, "font-layout-diagram");

    [Fact]
    public async Task Nested_viewport_own_map_keeps_supplied_glyph_placement_and_physical_clipping()
    {
        const string viewport = "<svg x='8' y='6' width='60' height='40' viewBox='0 0 30 20' opacity='.625'><text x='2' y='14' fill='#2168ad'>Aa</text></svg>";
        const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='144' height='96' font-family='Example' font-size='18'>";
        const string end = "<rect x='120' y='4' width='4' height='4' fill='blue'/></svg>";
        const string map = "matrix(1 .25 -.3 1 32 8)";
        byte[] input = System.Text.Encoding.UTF8.GetBytes(start + viewport.Replace("<svg ", "<svg transform='" + map + "' ", StringComparison.Ordinal) + end);
        byte[] expected = System.Text.Encoding.UTF8.GetBytes(start + "<g transform='" + map + "'>" + viewport + "</g>" + end);
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(),
            SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Whole_pattern_content_releases_borrowed_font_filter_and_image_resources_on_denial_and_retry()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "pattern-tile-sheet");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, "pattern-tile-sheet.svg"));
        byte[] unchanged = input.ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(16_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None))
        {
            RenderBudget accepted = new RenderBudget(new HostMemoryBudget(16_000_000));
            byte[] expected = SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray();
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(Math.Max(1024, accepted.Snapshot().PeakBytes / 2)));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(scene, options, denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.ConvertToPng(scene, options, accepted, cancellation.Token)).CancellationToken);
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            Assert.Equal(expected, SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray());
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
        Assert.Equal(unchanged, input);
    }

    [Fact]
    public async Task Dashed_and_non_scaling_glyph_sources_release_on_denial_cancellation_and_retry()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("Example", 400, false)], new Provider(), CancellationToken.None);
        byte[] input = File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "remaining-label-path-sheet", "stroke-modes.svg"));
        byte[] unchanged = input.ToArray();
        RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(16_000_000));
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget accepted = new RenderBudget(new HostMemoryBudget(16_000_000));
        byte[] expected = SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray();
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(400_000));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(scene, options, denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        OperationCanceledException canceled = Assert.Throws<OperationCanceledException>(() => SvgConverter.ConvertToPng(scene, options, accepted, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Equal(expected, SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
        scene.Dispose();
        Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Decoration_fragments_follow_glyph_clusters_and_position_boundaries()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("Example", 400, false)], new Provider(), CancellationToken.None);
        SvgTextStyle style = new(new("Example", 400, false), 18, 0, 24, 0, 0, 0, SvgTextAnchor.Start, string.Empty);
        SvgTextGlyphPaint paint = new(new(new(20, 40, 80, 255), 1), true);
        paint = paint with { Decoration = new(SvgTextDecorationLine.Underline, paint) };
        SvgTextPositionAdjustment[] positions = [new(2, 40, null, 0, 0), new(3, 70, null, 0, 0)];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "fiAB", [], positions, [], paint, null, snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(3, run.Glyphs.Length);
            Assert.Equal(1652, run.Glyphs[0].GlyphIndex);
            Assert.Equal(3, run.PaintFragments.Length);
            for (int index = 0; index < 3; index++)
            {
                Assert.Equal(index, run.PaintFragments[index].Start);
                Assert.Equal(index + 1, run.PaintFragments[index].End);
                Assert.Equal(run.Glyphs[index].Advance, run.PaintFragments[index].Width, 10);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Decoration_ink_clips_and_affine_fragments_release_on_denial_cancellation_and_retry()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("Example", 400, false), new("Mono", 700, false)], new Provider(), CancellationToken.None);
        byte[] original = File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "remaining-label-path-sheet", "solid-decorations.svg"));
        byte[] unchanged = original.ToArray();
        byte[] affine = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("<g font-family", "<g transform='rotate(7 160 80)' font-family", StringComparison.Ordinal));
        byte[] stroked = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("<g font-family", "<g stroke='#a44f32' stroke-width='1' font-family", StringComparison.Ordinal));
        byte[] affineStroked = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(stroked).Replace("<g stroke", "<g transform='rotate(7 160 80)' stroke", StringComparison.Ordinal));
        foreach (byte[] input in new[] { original, affine, stroked, affineStroked })
        {
            RenderBudget accepted = new RenderBudget(new HostMemoryBudget(32_000_000));
            Run(input, snapshot, accepted, CancellationToken.None);
            long peak = accepted.Snapshot().PeakBytes;
            foreach (long limit in new[] { 1, peak / 2 })
            {
                RenderBudget denied = new RenderBudget(new HostMemoryBudget(limit));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(input, snapshot, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
            }
            using CancellationTokenSource cancellation = new();
            using (GlyphExpansionProbe.Observe(cancellation.Cancel))
            {
                OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => Run(input, snapshot, accepted, cancellation.Token));
                Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            }
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            Run(input, snapshot, accepted, CancellationToken.None);
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        }
        Assert.Equal(unchanged, original);

        static void Run(byte[] input, SvgFontSnapshot snapshot, RenderBudget budget, CancellationToken token)
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, token);
            Assert.True(scene.Geometries.Length > 0);
        }
    }

    [Fact]
    public async Task Stroked_decoration_compound_clips_release_render_storage_on_denial_cancellation_and_retry()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "stroked-decorations");
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "..", "offset-merge-document", "font-pack.json")), "public-noto-sans");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("serif", 400, false)], new Provider(), CancellationToken.None);
        byte[] original = File.ReadAllBytes(Path.Combine(directory, "direct-stroked-decoration.svg"));
        byte[] unchanged = original.ToArray();
        byte[] affine = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("<text ", "<text transform='rotate(7 100 40)' ", StringComparison.Ordinal));
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[][] pathInputs = new[] { "curved-stroked-decoration.svg", "path-decoration-lines.svg", "path-decoration-positioning.svg" }
            .Select(name => File.ReadAllBytes(Path.Combine(directory, name))).ToArray();
        foreach (byte[] input in new[] { original, affine }.Concat(pathInputs))
        {
            byte[] unchangedInput = input.ToArray();
            RenderBudget parsed = new RenderBudget(new HostMemoryBudget(16_000_000));
            using (SvgRoot measured = SvgRootParser.Parse(input, SvgRenderLimits.Default, parsed, snapshot, CancellationToken.None))
                Assert.NotEmpty(measured.Geometries.ToArray());
            Assert.Equal(0, parsed.Snapshot().CurrentBytes);
            using (CancellationTokenSource parseCancellation = new())
            {
                using (GlyphExpansionProbe.Observe(parseCancellation.Cancel))
                    Assert.Equal(parseCancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
                        SvgRootParser.Parse(input, SvgRenderLimits.Default, parsed, snapshot, parseCancellation.Token)).CancellationToken);
                Assert.Equal(0, parsed.Snapshot().CurrentBytes);
            }
            RenderBudget sceneBudget = new RenderBudget(new HostMemoryBudget(16_000_000));
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None))
            {
                RenderBudget accepted = new RenderBudget(new HostMemoryBudget(16_000_000));
                byte[] expected = SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray();
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                RenderBudget denied = new RenderBudget(new HostMemoryBudget(accepted.Snapshot().PeakBytes / 2));
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(scene, options, denied, CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
                using CancellationTokenSource cancellation = new();
                cancellation.Cancel();
                Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgConverter.ConvertToPng(scene, options, accepted, cancellation.Token)).CancellationToken);
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                Assert.Equal(expected, SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray());
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            }
            Assert.Equal(0, sceneBudget.Snapshot().CurrentBytes);
            Assert.Equal(unchangedInput, input);
        }
        Assert.Equal(unchanged, original);
    }

    [Fact]
    public async Task Word_spacing_expands_only_separator_advances_and_preserves_ordinary_ligatures()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("Example", 400, false)], new Provider(), CancellationToken.None);
        SvgTextStyle style = new(new("Example", 400, false), 18, 0, 24, 0, 0, 0, SvgTextAnchor.Start, string.Empty);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        using (SvgTextRun ordinary = SvgTextRunBuilder.Create(style, "fi A\u00a0B\u2002C", snapshot, budget, CancellationToken.None))
        using (SvgTextRun spaced = SvgTextRunBuilder.Create(style with { WordSpacing = 5 }, "fi A\u00a0B\u2002C", snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(7, spaced.Glyphs.Length);
            Assert.Equal(1652, spaced.Glyphs[0].GlyphIndex);
            for (int index = 0; index < spaced.Glyphs.Length; index++)
            {
                Assert.Equal(ordinary.Glyphs[index].GlyphIndex, spaced.Glyphs[index].GlyphIndex);
                Assert.Equal(index < 2 ? 0 : index < 4 ? 5 : 10, spaced.Glyphs[index].X - ordinary.Glyphs[index].X, 10);
                Assert.Equal(index is 1 or 3 ? 5 : 0, spaced.Glyphs[index].Advance - ordinary.Glyphs[index].Advance, 10);
            }
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Scoped_rotation_keeps_ligatures_outside_the_positioned_span_and_maps_angles_to_glyphs()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("Example", 400, false)], new Provider(), CancellationToken.None);
        SvgTextStyle style = new(new("Example", 400, false), 18, 0, 24, 0, 0, 0, SvgTextAnchor.Start, string.Empty);
        SvgTextPositionAdjustment[] positions = [new(2, null, null, 0, 0) { Rotation = 10 }, new(3, null, null, 0, 0) { Rotation = 20 }];
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(100_000));
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "fiAB", [], positions, snapshot, budget, CancellationToken.None))
        {
            Assert.Equal(3, run.Glyphs.Length);
            Assert.Equal(1652, run.Glyphs[0].GlyphIndex);
            Assert.Equal(SvgAffineTransform.Identity, run.GetGlyphPlacement(0, SvgAffineTransform.Identity).Map);
            Assert.Equal(SvgAffineTransform.FromRotationDegrees(10).A, run.GetGlyphPlacement(1, SvgAffineTransform.Identity).Map.A);
            Assert.Equal(SvgAffineTransform.FromRotationDegrees(20).B, run.GetGlyphPlacement(2, SvgAffineTransform.Identity).Map.B);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Reduced_quadratic_crossings_preserve_the_complete_native_curved_counter_mask()
    {
        using JsonDocument vector = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "windows-curve-span.json")));
        JsonElement data = vector.RootElement;
        byte[] font = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(data, "fontSource")));
        Assert.Equal(Required(data, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(font)));
        Assert.True(File.Exists(Path.Combine(DirectoryPath, Required(data, "fontLicense"))));
        Assert.Equal(3, data.GetProperty("freshProcessCaptures").GetInt32());
        OpenTypeQuadraticSegment[] segments = data.GetProperty("segments").EnumerateArray()
            .Select(segment => new OpenTypeQuadraticSegment(Point(segment[0]), Point(segment[1]), Point(segment[2]))).ToArray();
        byte[] expected = Convert.FromBase64String(Required(data, "nativeRgbBase64"));
        JsonElement bounds = data.GetProperty("bounds");
        int left = bounds[0].GetInt32(), top = bounds[1].GetInt32(), right = bounds[2].GetInt32(), bottom = bounds[3].GetInt32();
        Assert.Equal((right - left) * (bottom - top) * 3, expected.Length);
        SvgGlyphLcdCoverage.Crossing[] scratch = new SvgGlyphLcdCoverage.Crossing[segments.Length * 2];
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            int at = ((y - top) * (right - left) + x - left) * 3;
            Assert.Equal(new SvgGlyphRgbCoverage(expected[at], expected[at + 1], expected[at + 2]),
                SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, true, CancellationToken.None));
        }

        static OpenTypeOutlinePoint Point(JsonElement value) => new(value[0].GetDouble() / 384, value[1].GetDouble() / 320);
    }

    [Fact]
    public void Bold_mono_stem_contours_match_the_independent_native_vector_and_release_on_denial()
    {
        using JsonDocument vector = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "diagnostics", "mono-stem-contours.json")));
        JsonElement data = vector.RootElement;
        byte[] bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(data, "fontSource")));
        Assert.Equal(Required(data, "fontSha256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.True(File.Exists(Path.Combine(DirectoryPath, Required(data, "fontLicense"))));
        OpenTypeFontMetrics metrics = OpenTypeFont.ParseMetrics(bytes, CancellationToken.None);
        OpenTypeQuadraticSegment[] expected = data.GetProperty("segments").EnumerateArray()
            .Select(segment => new OpenTypeQuadraticSegment(Point(segment[0]), Point(segment[1]), Point(segment[2]))).ToArray();
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));         using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(bytes, data.GetProperty("glyphIndex").GetUInt16(), metrics, data.GetProperty("fontSize").GetDouble(), budget, CancellationToken.None))
        {
            Assert.Equal(expected, contours.Segments.ToArray());
            Assert.Equal(data.GetProperty("contourEnds").EnumerateArray().Select(end => end.GetInt32()), contours.ContourEnds.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(budget.Snapshot().PeakBytes - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            OpenTypeFont.GetLatinLightContours(bytes, data.GetProperty("glyphIndex").GetUInt16(), metrics, data.GetProperty("fontSize").GetDouble(), denied, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
    }

    public static IEnumerable<object[]> Cases()
        => ReadCases(DirectoryPath);

    public static IEnumerable<object[]> CascadeCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "root-resource-cascade"));

    public static IEnumerable<object[]> PatternTileCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "pattern-tile-sheet"));

    public static IEnumerable<object[]> StrokedDecorationCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "stroked-decorations"));

    public static IEnumerable<object[]> GlyphResourceCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "glyph-resource-exit-sheet"));

    [Fact]
    public async Task Glyph_resource_scopes_preserve_preparation_denial_cancellation_cleanup_and_retry()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "glyph-resource-exit-sheet");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, "glyph-resource-sheet.svg")), unchanged = input.ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "..", "supplied-font-labels", "font-pack.json")), "public-noto-sans");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        Assert.Single(snapshot.Fonts);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget accepted = new RenderBudget(new HostMemoryBudget(32_000_000));         byte[] expected = Run(accepted, CancellationToken.None);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        foreach (long maximum in new[] { 1L, accepted.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(maximum));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        using (GlyphExpansionProbe.Observe(cancellation.Cancel))
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => Run(accepted, cancellation.Token)).CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Equal(expected, Run(accepted, CancellationToken.None));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new Provider(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);

        byte[] Run(RenderBudget budget, CancellationToken token)
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, token);
            return SvgConverter.ConvertToPng(root, options, budget, token).ToArray();
        }
    }

    public static IEnumerable<object[]> MarkerCompletionCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "marker-completion-sheet"));

    [Theory]
    [MemberData(nameof(MarkerCompletionCases))]
    public Task Marker_completion_outputs_match_original_sources_and_verified_font_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "marker-completion-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Marker_completion_sheet_uses_its_svg_local_supplied_face()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "marker-completion-sheet"), "marker-completion-sheet");

    [Fact]
    public Task Marker_completion_releases_owners_on_denial_cancellation_and_prepared_retry()
        => ValidateRetainedTextOwners("marker-completion-sheet", "marker-completion-sheet.svg");

    [Theory]
    [InlineData("<path d='M20 30H80' marker-end='url(#m)'/>", true)]
    [InlineData("<g marker-end='url(#m)'><path d='M20 30H80'/></g>", true)]
    [InlineData("<defs><path id='s' d='M20 30H80' marker-end='url(#m)'/></defs><use href='#s'/>", true)]
    [InlineData("<defs><g id='s'><path d='M20 30H80' marker-end='url(#m)'/></g></defs><use href='#s'/>", true)]
    [InlineData("<path d='M20 30H80'/>", false)]
    [InlineData("<path d='M20 30H80' visibility='hidden' marker-end='url(#m)'/>", false)]
    [InlineData("<path d='' marker-end='url(#m)'/>", false)]
    public async Task Marker_ex_lengths_prepare_only_live_required_public_faces(string content, bool required)
    {
        string marker = "<marker id='m' font-family='Example' font-size='18' markerUnits='userSpaceOnUse' markerWidth='3ex' markerHeight='2ex' refX='.5ex' refY='1ex'><path d='M0 0L20 8L0 16Z' fill='#43805b'/></marker>";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='80'><defs>" + marker + "</defs>" + content + "</svg>");
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "..", "supplied-font-labels", "font-pack.json")), "public-noto-sans");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        Assert.Equal(required ? 1 : 0, snapshot.Fonts.Count);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new Provider(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    public static IEnumerable<object[]> RetainedInstanceEffectCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "retained-instance-effect-sheet"));

    [Theory]
    [MemberData(nameof(RetainedInstanceEffectCases))]
    public Task Retained_instance_effect_outputs_match_unchanged_platform_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "retained-instance-effect-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Retained_instance_effect_sheet_uses_the_declared_supplied_face()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "retained-instance-effect-sheet"), "retained-instance-effect-sheet");

    [Fact]
    public Task Retained_instance_effect_sheet_releases_owners_on_denial_cancellation_and_prepared_retry()
        => ValidateRetainedTextOwners("retained-instance-effect-sheet", "retained-instance-effect-sheet.svg");

    public static IEnumerable<object[]> FilterSourceCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "filter-source-exit-sheet"));

    [Theory]
    [MemberData(nameof(FilterSourceCases))]
    public Task Declared_filter_source_document_and_controls_match_frozen_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "filter-source-exit-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Filter_source_document_uses_the_declared_supplied_face()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "filter-source-exit-sheet"), "filter-source-sheet");

    [Fact]
    public Task Filter_source_sheet_releases_owners_on_denial_cancellation_and_prepared_retry()
        => ValidateRetainedTextOwners("filter-source-exit-sheet", "filter-source-sheet.svg");

    public static IEnumerable<object[]> AffineSourceCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "affine-source-exit-sheet"));

    [Theory]
    [MemberData(nameof(AffineSourceCases))]
    public Task Declared_affine_source_document_and_controls_match_frozen_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "affine-source-exit-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Affine_source_document_uses_the_declared_supplied_face()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "affine-source-exit-sheet"), "affine-source-sheet");

    [Fact]
    public Task Affine_source_sheet_releases_owners_on_denial_cancellation_and_prepared_retry()
        => ValidateRetainedTextOwners("affine-source-exit-sheet", "affine-source-sheet.svg");

    public static IEnumerable<object[]> CompositeFlagCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "composite-flag-exit-sheet"));

    [Theory]
    [MemberData(nameof(CompositeFlagCases))]
    public Task Declared_composite_flag_document_and_controls_match_frozen_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "composite-flag-exit-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Composite_flag_document_uses_the_authored_supplied_face()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "composite-flag-exit-sheet"), "composite-flag-sheet");

    [Fact]
    public Task Composite_flag_sheet_releases_owners_on_denial_cancellation_and_prepared_retry()
        => ValidateRetainedTextOwners("composite-flag-exit-sheet", "composite-flag-sheet.svg");

    public static IEnumerable<object[]> InstancePaintCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "instance-paint-exit-sheet"));

    [Theory]
    [MemberData(nameof(InstancePaintCases))]
    public Task Declared_instance_paint_outputs_preserve_sources_and_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "instance-paint-exit-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Instance_paint_document_uses_the_declared_supplied_font()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "instance-paint-exit-sheet"), "instance-paint-sheet");

    public static IEnumerable<object[]> TextMapCases()
        => ReadCases(Path.Combine(ResearchCorpus.Root, "text-map-exit-sheet"));

    [Theory]
    [InlineData("mixed-direct-path.svg")]
    [InlineData("shader-curved-path.svg")]
    [InlineData("shader-stroke-maps.svg")]
    [InlineData("local-use-label.svg")]
    [InlineData("text-map-sheet.svg")]
    public Task Retained_text_maps_release_owners_and_support_prepared_retry(string inputName)
        => ValidateRetainedTextOwners("text-map-exit-sheet", inputName);

    [Fact]
    public Task Instanced_path_resources_release_owners_on_denial_cancellation_and_retry()
        => ValidateRetainedTextOwners("instance-paint-exit-sheet", "instanced-path-resources.svg");

    [Fact]
    public Task Text_paint_sheet_releases_owners_on_denial_cancellation_and_prepared_retry()
        => ValidateRetainedTextOwners("instance-paint-exit-sheet", "instance-paint-sheet.svg");

    [Fact]
    public async Task Retained_dormant_forward_paint_diagnostic_is_definition_order_invariant()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "instance-paint-exit-sheet");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, "diagnostics", "dormant-forward-paint.svg")), unchanged = input.ToArray();
        string xml = System.Text.Encoding.UTF8.GetString(input);
        int start = xml.IndexOf("<linearGradient", StringComparison.Ordinal);
        int end = xml.IndexOf("</linearGradient>", start, StringComparison.Ordinal) + "</linearGradient>".Length;
        string gradient = xml[start..end];
        string ordered = xml.Remove(start, end - start);
        ordered = ordered.Insert(ordered.IndexOf("<path ", StringComparison.Ordinal), gradient);
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "..", "supplied-font-labels", "font-pack.json")), "public-noto-sans");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = SvgConverter.ConvertToPng(System.Text.Encoding.UTF8.GetBytes(ordered), options, CancellationToken.None).ToArray();
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.Equal(expected, actual);
        RgbaImage pixels = PngRgbaDecoder.Decode(actual, SvgRenderLimits.Default.MaximumPixels);
        Assert.Contains(pixels.Pixels.Where((_, index) => index % 4 == 3), alpha => alpha != 0);
        Assert.Equal(unchanged, input);
    }

    private static async Task ValidateRetainedTextOwners(string bankName, string inputName)
    {
        string directory = Path.Combine(ResearchCorpus.Root, bankName);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, inputName)), unchanged = input.ToArray();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, Required(manifest.RootElement, "fontPack"))), Required(manifest.RootElement, "fontPackId"));
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget accepted = new RenderBudget(new HostMemoryBudget(32_000_000));         byte[] expected = Run(accepted, CancellationToken.None);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        foreach (long maximum in new[] { 1L, accepted.Snapshot().PeakBytes / 2 })
        {
            RenderBudget denied = new RenderBudget(new HostMemoryBudget(maximum));
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(denied, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, denied.Snapshot().CurrentBytes);
        }
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        int visits = 0;
        using (GlyphExpansionProbe.Observe(() => visits++))
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accepted, snapshot, CancellationToken.None)) Assert.NotEmpty(root.Geometries);
        Assert.True(visits > 0); Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        int observed = 0; using CancellationTokenSource cancellation = new();
        using (GlyphExpansionProbe.Observe(() => { if (++observed == visits) cancellation.Cancel(); }))
            Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => Run(accepted, cancellation.Token)).CancellationToken);
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        Assert.Equal(expected, Run(accepted, CancellationToken.None));
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, catalog, new Provider(), SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expected, prepared.ConvertToPng(CancellationToken.None).ToArray()); Assert.Equal(unchanged, input);

        byte[] Run(RenderBudget budget, CancellationToken token)
        {
            using SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, token);
            return SvgConverter.ConvertToPng(root, options, budget, token).ToArray();
        }
    }

    private static IEnumerable<object[]> ReadCases(string directory)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            if (item.GetProperty("role").GetString() != "font-contract-fixture")
                yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [InlineData("nested-list-rotation")]
    [InlineData("word-spacing")]
    [InlineData("solid-decorations")]
    [InlineData("stroke-paint-order")]
    [InlineData("stroke-modes")]
    [InlineData("path-span-rotation")]
    [InlineData("closed-path-anchors")]
    [InlineData("mixed-length-layout")]
    public async Task Remaining_label_path_outputs_preserve_frozen_inputs_and_platform_references(string identity)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "remaining-label-path-sheet");
        await ValidateDeclared(directory, identity, [new("Example", 400, false), new("Mono", 700, false)]);
    }

    [Fact]
    public void Remaining_label_font_selection_keeps_its_independent_native_proofs()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "remaining-label-path-sheet");
        using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "font-use-proof", "qualification.json")));
        string platform = OperatingSystem.IsLinux() ? "linux" : "windows";
        string referencePath = OperatingSystem.IsLinux() ? "references/linux64/remaining-label-path-sheet.png" : "references/remaining-label-path-sheet.png";
        RgbaImage reference = PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(directory, referencePath)), SvgRenderLimits.Default.MaximumPixels);
        foreach (JsonElement missing in proof.RootElement.GetProperty("platforms").GetProperty(platform).EnumerateArray())
        {
            JsonElement captures = missing.GetProperty("captures");
            Assert.Equal(3, captures.GetProperty("freshProcessCaptures").GetInt32());
            Assert.Equal("identical", Required(captures, "stability"));
            Assert.Equal("healthy", Required(captures, "startupHealth"));
            Assert.True(captures.GetProperty("profileCleaned").GetBoolean());
            byte[] png = File.ReadAllBytes(Path.Combine(directory, Required(missing, "reference")));
            Assert.Equal(Required(captures, "pngSha256"), Convert.ToHexStringLower(SHA256.HashData(png)));
            PixelComparison difference = PixelComparer.Compare(reference, PngRgbaDecoder.Decode(png, SvgRenderLimits.Default.MaximumPixels));
            Assert.Equal(missing.GetProperty("changedPixelsFromIntendedFont").GetInt64(), difference.ChangedPixelCount);
            Assert.False(difference.IsExact);
        }
    }

    [Theory]
    [MemberData(nameof(CascadeCases))]
    public async Task Declared_cascade_outputs_preserve_original_sources_and_platform_references(string identity)
    {
        string directory = Path.Combine(ResearchCorpus.Root, "root-resource-cascade");
        await ValidateDeclared(directory, identity, [new("Example", 400, false)]);
        if (identity != "cascade-sheet") return;
        using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "font-use-proof", "qualification.json")));
        JsonElement result = proof.RootElement.GetProperty(OperatingSystem.IsLinux() ? "linux" : "windows");
        Assert.Equal("healthy", Required(result, "startupHealth")); Assert.True(result.GetProperty("profileCleaned").GetBoolean());
        byte[] missing = File.ReadAllBytes(Path.Combine(directory, "font-use-proof", Required(result, "missingFaceReference")));
        Assert.Equal(Required(result, "missingFaceSha256"), Convert.ToHexStringLower(SHA256.HashData(missing)));
        string reference = OperatingSystem.IsLinux() ? "references/linux64/cascade-sheet.png" : "references/cascade-sheet.png";
        Assert.False(PixelComparer.Compare(PngRgbaDecoder.Decode(File.ReadAllBytes(Path.Combine(directory, reference)), SvgRenderLimits.Default.MaximumPixels), PngRgbaDecoder.Decode(missing, SvgRenderLimits.Default.MaximumPixels)).IsExact);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Declared_layout_outputs_preserve_original_sources_and_platform_references(string identity)
        => ValidateDeclared(DirectoryPath, identity, [new("Example", 400, false), new("Mono", 700, false), new("Missing", 400, false)]);

    [Theory]
    [MemberData(nameof(PatternTileCases))]
    public Task Declared_pattern_tile_outputs_preserve_original_sources_and_platform_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "pattern-tile-sheet"), identity, [new("Example", 400, false)]);

    [Theory]
    [MemberData(nameof(StrokedDecorationCases))]
    public Task Declared_stroked_decoration_outputs_preserve_original_sources_and_platform_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "stroked-decorations"), identity, [new("serif", 400, false)]);

    [Theory]
    [MemberData(nameof(GlyphResourceCases))]
    public Task Declared_glyph_resource_outputs_preserve_original_sources_and_platform_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "glyph-resource-exit-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Glyph_resource_document_uses_the_declared_supplied_font()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "glyph-resource-exit-sheet");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        Assert.Equal(Required(manifest.RootElement, "fontProgramSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, "..", "supplied-font-labels", "fonts", "NotoSans-Regular.ttf")))));
        using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "font-use-proof", "qualification.json")));
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement witness = proof.RootElement.GetProperty(platform);
            byte[] missing = File.ReadAllBytes(Path.Combine(directory, "font-use-proof", Required(witness, "missingFaceReference")));
            Assert.Equal(Required(witness, "missingFaceSha256"), Convert.ToHexStringLower(SHA256.HashData(missing)));
            foreach (JsonElement capture in witness.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal("healthy", Required(capture, "startupHealth")); Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
                Assert.Equal(Required(witness, "missingFaceSha256"), Required(capture, "pngSha256"));
            }
            byte[] reference = File.ReadAllBytes(Path.Combine(directory, platform == "linux" ? "references/linux64/glyph-resource-sheet.png" : "references/glyph-resource-sheet.png"));
            Assert.False(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, SvgRenderLimits.Default.MaximumPixels), PngRgbaDecoder.Decode(missing, SvgRenderLimits.Default.MaximumPixels)).IsExact);
        }
    }

    [Theory]
    [MemberData(nameof(TextMapCases))]
    public Task Declared_text_map_outputs_preserve_original_sources_and_platform_references(string identity)
        => ValidateDeclared(Path.Combine(ResearchCorpus.Root, "text-map-exit-sheet"), identity, [new("Example", 400, false)]);

    [Fact]
    public void Text_map_document_uses_the_declared_supplied_font()
        => ValidateIntendedFont(Path.Combine(ResearchCorpus.Root, "text-map-exit-sheet"), "text-map-sheet");

    private static void ValidateIntendedFont(string directory, string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        Assert.Equal(Required(manifest.RootElement, "fontProgramSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, manifest.RootElement.TryGetProperty("fontProgram", out JsonElement program) ? program.GetString() ?? throw new InvalidDataException() : "../supplied-font-labels/fonts/NotoSans-Regular.ttf")))));
        using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "font-use-proof", "qualification.json")));
        foreach (string platform in new[] { "windows", "linux" })
        {
            JsonElement witness = proof.RootElement.GetProperty(platform);
            byte[] missing = File.ReadAllBytes(Path.Combine(directory, "font-use-proof", Required(witness, "missingFaceReference")));
            Assert.Equal(Required(witness, "missingFaceSha256"), Convert.ToHexStringLower(SHA256.HashData(missing)));
            foreach (JsonElement capture in witness.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal("healthy", Required(capture, "startupHealth")); Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
                Assert.Equal(Required(witness, "missingFaceSha256"), Required(capture, "pngSha256"));
            }
            Assert.Equal(3, witness.GetProperty("captures").GetArrayLength());
            JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
            byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(item, platform == "linux" ? "linuxReference" : "reference")));
            Assert.False(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, SvgRenderLimits.Default.MaximumPixels), PngRgbaDecoder.Decode(missing, SvgRenderLimits.Default.MaximumPixels)).IsExact);
        }
    }

    private static async Task ValidateDeclared(string directory, string identity, SvgFontFaceRequest[] requests)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
        byte[] unchanged = input.ToArray();
        Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        string license = Required(item, "license");
        Assert.Contains(license, new[] { "CC0-1.0", "MIT", "LicenseRef-W3C-Software-2023" });
        if (license == "LicenseRef-W3C-Software-2023")
            Assert.True(File.Exists(Path.Combine(directory, Required(item, "licenseEvidence"))));
        else if (input.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("SPDX-License-Identifier: " + license)) < 0)
        {
            Assert.Equal("MIT", license);
            Assert.Equal("LICENSE.txt", Required(item, "licenseEvidence"));
            Assert.Contains("MIT License", File.ReadAllText(Path.Combine(directory, "LICENSE.txt")));
        }
        string referenceKey = OperatingSystem.IsLinux() ? "linuxReference" : "reference";
        byte[] reference = File.ReadAllBytes(Path.Combine(directory, Required(item, referenceKey)));
        Assert.Equal(Required(item, referenceKey + "Sha256"), Convert.ToHexStringLower(SHA256.HashData(reference)));
        JsonElement captures = item.GetProperty(OperatingSystem.IsLinux() ? "linuxCaptures" : "captures");
        Assert.Equal(3, captures.GetArrayLength());
        foreach (JsonElement capture in captures.EnumerateArray())
        {
            Assert.Equal("healthy", Required(capture, "startupHealth"));
            Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
            Assert.Equal(Required(item, referenceKey + "Sha256"), Required(capture, "pngSha256"));
        }
        string packId = root.TryGetProperty("fontPackId", out JsonElement declaredPack) ? declaredPack.GetString() ?? throw new InvalidDataException() : "public-noto-layout";
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, Required(root, "fontPack"))), packId);
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog,
            requests, new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(new SvgViewport(item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32()), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        if (item.TryGetProperty("expectedError", out JsonElement error))
            Assert.Equal(Enum.Parse<SvgErrorCode>(error.GetString() ?? throw new InvalidDataException()),
                Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, options, CancellationToken.None)).ErrorCode);
        else
        {
            RgbaImage expected = PngRgbaDecoder.Decode(reference, SvgRenderLimits.Default.MaximumPixels);
            RgbaImage actual = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(), SvgRenderLimits.Default.MaximumPixels);
            Assert.True(PixelComparer.Compare(expected, actual).IsExact);
        }
        Assert.Equal(unchanged, input);
    }

    private static string Required(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    /// <summary>Supplies immutable existing public programs under caller ownership, without host discovery.</summary>
    /// <remarks>Cancellation is observed before I/O; independent acquisitions have no shared mutable state.</remarks>
    private sealed class Provider : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = file.RelativePath switch
            {
                "fonts/NotoSans-Regular.ttf" => "../supplied-font-labels/fonts/NotoSans-Regular.ttf",
                "fonts/CompositeFlags.ttf" => "../composite-flag-exit-sheet/fonts/CompositeFlags.ttf",
                "fonts/NotoSansMono-Bold.ttf" => "../supplied-font-documents/fonts/NotoSansMono-Bold.ttf",
                _ => throw new InvalidDataException("Unexpected supplied program."),
            };
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-layout", File.ReadAllBytes(Path.Combine(DirectoryPath, path))));
        }
    }
}
