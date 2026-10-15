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
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "font-layout-diagram");

    [Fact]
    public async Task Whole_pattern_content_releases_borrowed_font_filter_and_image_resources_on_denial_and_retry()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "pattern-tile-sheet");
        byte[] input = File.ReadAllBytes(Path.Combine(directory, "pattern-tile-sheet.svg"));
        byte[] unchanged = input.ToArray();
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, new Provider(), CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget sceneBudget = new(16_000_000);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None))
        {
            RenderBudget accepted = new(16_000_000);
            byte[] expected = SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray();
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            RenderBudget denied = new(Math.Max(1024, accepted.Snapshot().PeakBytes / 2));
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
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "remaining-label-path-sheet", "stroke-modes.svg"));
        byte[] unchanged = input.ToArray();
        RenderBudget sceneBudget = new(16_000_000);
        using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget accepted = new(16_000_000);
        byte[] expected = SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray();
        Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        RenderBudget denied = new(400_000);
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
        SvgTextGlyphPaint paint = new(new(20, 40, 80, 255), true);
        paint = paint with { Decoration = new(SvgTextDecorationLine.Underline, paint) };
        SvgTextPositionAdjustment[] positions = [new(2, 40, null, 0, 0), new(3, 70, null, 0, 0)];
        RenderBudget budget = new(100_000);
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "fiAB", [], positions, [], paint, null, snapshot, budget, new(100_000), CancellationToken.None))
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
        byte[] original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research", "remaining-label-path-sheet", "solid-decorations.svg"));
        byte[] unchanged = original.ToArray();
        byte[] affine = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("<g font-family", "<g transform='rotate(7 160 80)' font-family", StringComparison.Ordinal));
        byte[] stroked = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("<g font-family", "<g stroke='#a44f32' stroke-width='1' font-family", StringComparison.Ordinal));
        byte[] affineStroked = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(stroked).Replace("<g stroke", "<g transform='rotate(7 160 80)' stroke", StringComparison.Ordinal));
        foreach (byte[] input in new[] { original, affine, stroked, affineStroked })
        {
            RenderBudget accepted = new(32_000_000);
            ParserWorkCounter work = new(100_000_000);
            Run(input, snapshot, accepted, work, CancellationToken.None);
            long peak = accepted.Snapshot().PeakBytes;
            foreach (long limit in new[] { 1, peak / 2 })
            {
                RenderBudget denied = new(limit);
                Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(input, snapshot, denied, new(100_000_000), CancellationToken.None)).ErrorCode);
                Assert.Equal(0, denied.Snapshot().CurrentBytes);
            }
            RenderBudget workDenied = new(32_000_000);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => Run(input, snapshot, workDenied, new(work.Used - 1), CancellationToken.None)).ErrorCode);
            Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
            using CancellationTokenSource cancellation = new();
            using (GlyphExpansionProbe.Observe(cancellation.Cancel))
            {
                OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => Run(input, snapshot, accepted, new(100_000_000), cancellation.Token));
                Assert.Equal(cancellation.Token, cancelled.CancellationToken);
            }
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
            Run(input, snapshot, accepted, new(100_000_000), CancellationToken.None);
            Assert.Equal(0, accepted.Snapshot().CurrentBytes);
        }
        Assert.Equal(unchanged, original);

        static void Run(byte[] input, SvgFontSnapshot snapshot, RenderBudget budget, ParserWorkCounter work, CancellationToken token)
        {
            using SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, work, token);
            Assert.True(scene.Geometries.Length > 0);
        }
    }

    [Fact]
    public async Task Stroked_decoration_compound_clips_release_render_storage_on_denial_cancellation_and_retry()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "stroked-decorations");
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "..", "offset-merge-document", "font-pack.json")), "public-noto-sans");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("serif", 400, false)], new Provider(), CancellationToken.None);
        byte[] original = File.ReadAllBytes(Path.Combine(directory, "direct-stroked-decoration.svg"));
        byte[] unchanged = original.ToArray();
        byte[] affine = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(original).Replace("<text ", "<text transform='rotate(7 100 40)' ", StringComparison.Ordinal));
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        foreach (byte[] input in new[] { original, affine })
        {
            RenderBudget sceneBudget = new(16_000_000);
            using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, sceneBudget, snapshot, CancellationToken.None))
            {
                RenderBudget accepted = new(16_000_000);
                byte[] expected = SvgConverter.ConvertToPng(scene, options, accepted, CancellationToken.None).ToArray();
                Assert.Equal(0, accepted.Snapshot().CurrentBytes);
                RenderBudget denied = new(accepted.Snapshot().PeakBytes / 2);
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
        }
        Assert.Equal(unchanged, original);
    }

    [Fact]
    public async Task Word_spacing_expands_only_separator_advances_and_preserves_ordinary_ligatures()
    {
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-layout");
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(catalog, [new("Example", 400, false)], new Provider(), CancellationToken.None);
        SvgTextStyle style = new(new("Example", 400, false), 18, 0, 24, 0, 0, 0, SvgTextAnchor.Start, string.Empty);
        RenderBudget budget = new(100_000);
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
        RenderBudget budget = new(100_000);
        using (SvgTextRun run = SvgTextRunBuilder.Create(style, "fiAB", [], positions, snapshot, budget, new(1_000_000), CancellationToken.None))
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
        ParserWorkCounter work = new(long.MaxValue);
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            int at = ((y - top) * (right - left) + x - left) * 3;
            Assert.Equal(new SvgGlyphRgbCoverage(expected[at], expected[at + 1], expected[at + 2]),
                SvgGlyphLcdCoverage.GetCoverage(segments, x, y, scratch, true, work, CancellationToken.None));
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
        RenderBudget budget = new(1_000_000); ParserWorkCounter work = new(long.MaxValue);
        using (OpenTypeGlyphContourStorage contours = OpenTypeFont.GetLatinLightContours(bytes, data.GetProperty("glyphIndex").GetUInt16(), metrics, data.GetProperty("fontSize").GetDouble(), budget, work, CancellationToken.None))
        {
            Assert.Equal(expected, contours.Segments.ToArray());
            Assert.Equal(data.GetProperty("contourEnds").EnumerateArray().Select(end => end.GetInt32()), contours.ContourEnds.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(1_000_000);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() =>
            OpenTypeFont.GetLatinLightContours(bytes, data.GetProperty("glyphIndex").GetUInt16(), metrics, data.GetProperty("fontSize").GetDouble(), denied, new ParserWorkCounter(work.Used - 1), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);

        static OpenTypeOutlinePoint Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
    }

    public static IEnumerable<object[]> Cases()
        => ReadCases(DirectoryPath);

    public static IEnumerable<object[]> CascadeCases()
        => ReadCases(Path.Combine(AppContext.BaseDirectory, "Research", "root-resource-cascade"));

    public static IEnumerable<object[]> PatternTileCases()
        => ReadCases(Path.Combine(AppContext.BaseDirectory, "Research", "pattern-tile-sheet"));

    public static IEnumerable<object[]> StrokedDecorationCases()
        => ReadCases(Path.Combine(AppContext.BaseDirectory, "Research", "stroked-decorations"));

    private static IEnumerable<object[]> ReadCases(string directory)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [InlineData("remaining-label-path-sheet")]
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
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "remaining-label-path-sheet");
        await ValidateDeclared(directory, identity, [new("Example", 400, false), new("Mono", 700, false)]);
        if (identity != "remaining-label-path-sheet") return;
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
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "root-resource-cascade");
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
        => ValidateDeclared(Path.Combine(AppContext.BaseDirectory, "Research", "pattern-tile-sheet"), identity, [new("Example", 400, false)]);

    [Theory]
    [MemberData(nameof(StrokedDecorationCases))]
    public Task Declared_stroked_decoration_outputs_preserve_original_sources_and_platform_references(string identity)
        => ValidateDeclared(Path.Combine(AppContext.BaseDirectory, "Research", "stroked-decorations"), identity, [new("serif", 400, false)]);

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
        else Assert.True(input.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("SPDX-License-Identifier: " + license)) >= 0);
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
                "fonts/NotoSansMono-Bold.ttf" => "../supplied-font-documents/fonts/NotoSansMono-Bold.ttf",
                _ => throw new InvalidDataException("Unexpected supplied program."),
            };
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-layout", File.ReadAllBytes(Path.Combine(DirectoryPath, path))));
        }
    }
}
