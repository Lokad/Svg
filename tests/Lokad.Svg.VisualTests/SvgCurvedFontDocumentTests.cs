using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects the retained curved document, independent completed controls and explicit caller-supplied face.</summary>
public sealed class SvgCurvedFontDocumentTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "curved-font-documents");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray()) yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Document_and_controls_preserve_twelve_outputs_and_every_completed_reference(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        byte[] unchanged = input.ToArray();
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgViewport viewport = new(item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32());
        SvgRenderOptions options = new(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        byte[] destination = new byte[png.Length];
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream stream = new();
        Assert.Equal(png.Length, SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None));
        Assert.Equal(png, stream.ToArray());
        Assert.Equal(png, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, destination, options, CancellationToken.None));
        Assert.Equal(png, destination);
        using MemoryStream asyncStream = new();
        Assert.Equal(png.Length, await SvgConverter.ConvertToPngAsync(input, asyncStream, options, CancellationToken.None));
        Assert.Equal(png, asyncStream.ToArray());
        for (int mode = 0; mode < 6; mode++)
        {
            // Each prepared document owns exactly one conversion and font lease.
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog(), Provider(), new SvgRenderOptions(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default, null), CancellationToken.None);
            using MemoryStream preparedStream = new();
            byte[] preparedPng = mode switch
            {
                0 => prepared.ConvertToPng(CancellationToken.None).ToArray(),
                1 => (await prepared.ConvertToPngAsync(CancellationToken.None)).ToArray(),
                2 => prepared.ConvertToPng(destination, CancellationToken.None) == png.Length ? destination : [],
                3 => await prepared.ConvertToPngAsync(destination, CancellationToken.None) == png.Length ? destination : [],
                4 => prepared.ConvertToPng(preparedStream, CancellationToken.None) == png.Length ? preparedStream.ToArray() : [],
                _ => await prepared.ConvertToPngAsync(preparedStream, CancellationToken.None) == png.Length ? preparedStream.ToArray() : []
            };
            Assert.Equal(png, preparedPng);
        }
        Assert.Equal(unchanged, input);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
        {
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, OperatingSystem.IsLinux() ? "linuxReference" : "reference")));
            bool completed = Required(item, "rendererOutcome") == "exact" || OperatingSystem.IsLinux() && item.TryGetProperty("linuxRendererOutcome", out JsonElement linuxOutcome) && linuxOutcome.GetString() == "exact";
            if (completed) Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 16_777_216), PngRgbaDecoder.Decode(png, 16_777_216)).IsExact);
        }
    }

    [Fact]
    public void Public_font_bank_pins_evaluation_data_and_frozen_native_reference_triples()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(6, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(1, root.GetProperty("cases").EnumerateArray().Count(c => Required(c, "role") == "document-output"));
        Assert.Equal(Required(root, "fontProgramSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(FontDirectory, "fonts", "NotoSans-Regular.ttf")))));
        Assert.True(File.ReadAllText(Path.Combine(FontDirectory, "fonts", "OFL.txt")).Contains("SIL OPEN FONT LICENSE", StringComparison.Ordinal));
        foreach (JsonElement item in root.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input"))))));
            foreach (bool linux in new[] { false, true })
            {
                string reference = Required(item, linux ? "linuxReference" : "reference");
                string expectedHash = Required(item, linux ? "linuxReferenceSha256" : "referenceSha256");
                byte[] png = File.ReadAllBytes(Path.Combine(DirectoryPath, reference));
                Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(png)));
                PngRgbaDecoder.Decode(png, checked(item.GetProperty("width").GetInt32() * item.GetProperty("height").GetInt32()));
                JsonElement captures = item.GetProperty(linux ? "linuxCaptures" : "captures");
                Assert.Equal(3, captures.GetArrayLength());
                foreach (JsonElement capture in captures.EnumerateArray())
                {
                    Assert.Equal(expectedHash, Required(capture, "pngSha256"));
                    Assert.Equal("healthy", Required(capture, "startupHealth"));
                    Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
                }
            }
        }
    }

    private static string FontDirectory => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");

    private static ValueTask<SvgFontSnapshot> Prepare(byte[] input) => SvgFontSnapshot.CreateAsync(input, Catalog(), Provider(), CancellationToken.None);

    [Theory]
    [InlineData("inv-127-512x369-transparent")]
    [InlineData("composite-on-path")]
    public async Task Newly_admitted_document_and_composite_control_release_owners_and_allow_retry(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        Assert.Contains(Required(item, "rendererOutcome"), new[] { "exact", "rendered-known-difference" });
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        using SvgFontSnapshot snapshot = await Prepare(input);
        Assert.Single(snapshot.Fonts);
        RenderBudget budget = new(long.MaxValue);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None)) Assert.NotEmpty(root.Geometries);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new(budget.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, snapshot, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(new SvgViewport(item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32()), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.NotEmpty(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Curved_control_retains_mapped_glyph_source_and_releases_storage_on_budget_denial()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, "curved-midpoint.svg"));
        using SvgFontSnapshot snapshot = await Prepare(input);
        byte[] original = Assert.Single(snapshot.Fonts).Bytes.ToArray();
        RenderBudget accounting = new(long.MaxValue);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accounting, snapshot, CancellationToken.None))
        {
            Assert.True(accounting.Snapshot().CurrentBytes > 0);
            Assert.True(Assert.Single(root.Geometries).Glyph.Source is {} source && source.HasGlyphMap);
        }
        Assert.Equal(0, accounting.Snapshot().CurrentBytes);
        RenderBudget denied = new(accounting.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, snapshot, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(new SvgViewport(128, 96), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RgbaImage image = PngRgbaDecoder.Decode(png, 12_288);
        Assert.Contains(image.Pixels, value => value != 0);
        Assert.Equal(png, (await SvgConverter.ConvertToPngAsync(input, options, CancellationToken.None)).ToArray());
        Assert.Equal(original, Assert.Single(snapshot.Fonts).Bytes.ToArray());
        // Pixel completion remains in the manifest's separate ledger until both frozen references match.
    }

    [Theory]
    [InlineData("M8 48 C24 48 40 48 56 48")]
    [InlineData("M8 48 Q32 48 56 48")]
    public async Task Zero_angle_polynomial_baselines_preserve_horizontal_font_strikes(string data)
    {
        byte[] input = Document("<defs><path id='p' d='" + data + "'/></defs><text><textPath href='#p' startOffset='16'>A</textPath></text>", "");
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] equivalent = Document("<defs><path id='p' d='M8 48 H56'/></defs><text><textPath href='#p' startOffset='16'>A</textPath></text>", "");
        Assert.Equal(SvgConverter.ConvertToPng(equivalent, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        RenderBudget budget = new(long.MaxValue);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.True(Assert.Single(root.Geometries).Glyph.Source is {} source && !source.HasGlyphMap);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Theory]
    [InlineData("", "middle")]
    [InlineData("text-anchor='inherit'", "middle")]
    [InlineData("text-anchor='start'", "start")]
    [InlineData("text-anchor='end'", "end")]
    public async Task Sibling_paths_keep_independent_anchors_whitespace_and_inherited_language(string anchor, string expected)
    {
        const string paths = "<defs><path id='p' d='M0 48 H64'/><path id='q' d='M0 24 H64'/></defs>";
        string actual = paths + "<g text-anchor='middle' xml:lang='de'><text><textPath href='#p' startOffset='50%' " + anchor + "> A </textPath>\n<textPath href='#q' startOffset='50%'>A</textPath></text></g>";
        string target = paths + "<text text-anchor='" + expected + "'><textPath href='#p' startOffset='50%'>A</textPath></text><text text-anchor='middle'><textPath href='#q' startOffset='50%'>A</textPath></text>";
        byte[] input = Document(actual, "xml:lang='de' text-anchor='end'");
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(Document(target, ""), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        Assert.Single(snapshot.Fonts);
    }

    [Theory]
    [InlineData("112", "56")]
    [InlineData("50%", "50%")]
    [InlineData("-16", "-8")]
    public async Task Calibrated_numeric_offsets_and_percentages_match_uncalibrated_positions(string offset, string target)
    {
        string body = "<defs><path id='p' d='M0 48 H64' pathLength='128'/></defs><text text-anchor='middle'><textPath href='#p' startOffset='" + offset + "'>AVA</textPath></text>";
        byte[] input = Document(body, "");
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        string equivalent = body.Replace(" pathLength='128'", "", StringComparison.Ordinal).Replace("startOffset='" + offset + "'", "startOffset='" + target + "'", StringComparison.Ordinal);
        Assert.Equal(SvgConverter.ConvertToPng(Document(equivalent, ""), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Theory]
    [InlineData("<textPath href='#missing'>A</textPath>", SvgErrorCode.UnresolvedResource)]
    [InlineData("<textPath href='#p' text-anchor='center'>A</textPath>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("<tspan>A</tspan>", SvgErrorCode.UnsupportedFeature)]
    [InlineData("A", SvgErrorCode.UnsupportedFeature)]
    public async Task A_later_sibling_failure_releases_earlier_layout_and_queued_run_storage(string sibling, SvgErrorCode category)
    {
        byte[] input = Document("<defs><path id='p' d='M0 48 H64'/></defs><text><textPath href='#p'>A</textPath>" + sibling + "</text>", "");
        // Discovery may reject an unsupported child before preparing a font; the admitted control prepares the same caller-owned face.
        using SvgFontSnapshot snapshot = await Prepare(Document("<text>A</text>", ""));
        RenderBudget budget = new(long.MaxValue);
        Assert.Equal(category, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public async Task Sibling_run_budget_denial_and_retry_preserve_borrowed_font_bytes()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, "two-path-runs.svg"));
        using SvgFontSnapshot snapshot = await Prepare(input);
        byte[] original = Assert.Single(snapshot.Fonts).Bytes.ToArray();
        RenderBudget accounting = new(long.MaxValue);
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accounting, snapshot, CancellationToken.None)) Assert.True(accounting.Snapshot().CurrentBytes > 0);
        Assert.Equal(0, accounting.Snapshot().CurrentBytes);
        RenderBudget denied = new(accounting.Snapshot().PeakBytes - 1);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, snapshot, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        RenderBudget workDenied = new(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, workDenied, snapshot, new(16), CancellationToken.None)).ErrorCode);
        Assert.Equal(0, workDenied.Snapshot().CurrentBytes);
        using SvgRoot retried = SvgRootParser.Parse(input, SvgRenderLimits.Default, accounting, snapshot, CancellationToken.None);
        Assert.Equal(original, Assert.Single(snapshot.Fonts).Bytes.ToArray());
    }

    [Fact]
    public async Task Sibling_runs_share_one_source_character_limit()
    {
        string characters = new('A', SvgTextLimits.MaximumCharacters / 2 + 1);
        byte[] input = Document("<text><textPath href='#p'>" + characters + "</textPath><textPath href='#p'>" + characters + "</textPath></text><defs><path id='p' d='M0 48 H64'/></defs>", "");
        using SvgFontSnapshot snapshot = await Prepare(Document("<text>A</text>", ""));
        RenderBudget budget = new(long.MaxValue);
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    [Fact]
    public void Language_metadata_is_bounded_without_changing_geometric_pixels()
    {
        byte[] input = Document("<g xml:lang='de'><rect xml:lang='' width='8' height='8' fill='blue'/></g>", "xml:lang='de'");
        byte[] target = Document("<g><rect width='8' height='8' fill='blue'/></g>", "");
        Assert.Equal(SvgConverter.ConvertToPng(target, SvgRenderOptions.Default, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        byte[] oversized = Document("", "xml:lang='" + new string('a', SvgSyntaxLimits.MaximumValueTokenCharacters + 1) + "'");
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(oversized, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    private static byte[] Document(string body, string attributes)
        => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' font-family='fantasy' font-size='16' " + attributes + ">" + body + "</svg>");

    [Fact]
    public void Missing_corrupt_and_substituted_image_fonts_have_distinct_frozen_native_pixels()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement intended = manifest.RootElement.GetProperty("cases")[0];
        string proofDirectory = Path.Combine(DirectoryPath, "font-use-proof");
        using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(proofDirectory, "qualification.json")));
        Assert.Equal(2, proof.RootElement.GetProperty("platforms").GetArrayLength());
        foreach (JsonElement platform in proof.RootElement.GetProperty("platforms").EnumerateArray())
        {
            bool linux = Required(platform, "platform") == "linux";
            byte[] positive = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(intended, linux ? "linuxReference" : "reference")));
            Assert.Equal(3, platform.GetProperty("cases").GetArrayLength());
            foreach (JsonElement witness in platform.GetProperty("cases").EnumerateArray())
            {
                byte[] negative = File.ReadAllBytes(Path.Combine(proofDirectory, Required(witness, "reference")));
                PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(positive, 16_777_216), PngRgbaDecoder.Decode(negative, 16_777_216));
                Assert.Equal(witness.GetProperty("changedPixelsFromIntendedFont").GetInt32(), comparison.ChangedPixelCount);
                Assert.False(comparison.IsExact);
                Assert.Equal(3, witness.GetProperty("captures").GetArrayLength());
                foreach (JsonElement capture in witness.GetProperty("captures").EnumerateArray())
                {
                    Assert.Equal(Required(capture, "pngSha256"), Convert.ToHexStringLower(SHA256.HashData(negative)));
                    Assert.Equal("healthy", Required(capture, "startupHealth"));
                    Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
                }
            }
        }
    }

    private static SvgFontPackCatalog Catalog()
        => SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-sans");

    private static MemoryProvider Provider()
        => new(File.ReadAllBytes(Path.Combine(FontDirectory, "fonts", "NotoSans-Regular.ttf")));

    private static string Required(JsonElement item, string name)
        => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    /// <summary>Returns the immutable caller-owned public test program, observes cancellation and performs no I/O or host discovery; safe for concurrent requests.</summary>
    private sealed class MemoryProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (file.RelativePath != "fonts/NotoSans-Regular.ttf") throw new InvalidDataException("Unexpected test font request.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}
