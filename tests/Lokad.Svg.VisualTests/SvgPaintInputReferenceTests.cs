using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgPaintInputReferenceTests : IClassFixture<SvgPaintInputReferenceTests.NativePaintFixture>
{
    private readonly NativePaintFixture _fixture;
    private static readonly PaintReferenceBank[] Banks =
    [
        new("ordered-clip-coverage"), new("clipped-stroke-preparation"), new("marker-canvas-stages"),
        new("filter-region-coordinates"), new("point-list-source-maps"), new("image-source-views"),
        new("offset-merge-inputs"), new("filter-merge-image-draw"), new("filter-source-recording-map"),
        new("filter-source-composition"), new("linear-shape-source-maps"), new("radial-axis-source-maps"),
        new("gradient-spread-stages"), new("gradient-paint-replay"), new("gradient-factory-colors"),
        new("linear-periodic-stops"), new("gradient-stop-preparation"), new("gradient-color-alpha"),
        new("filter-uniform-precision"), new("solid-paint-alpha"), new("pattern-source-selection"),
        new("text-node-boundaries"), new("hairline-clip-rounding"),
    ];

    public SvgPaintInputReferenceTests(NativePaintFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> Cases() => SelectedCases(false);

    public static IEnumerable<object[]> ResearchCases() => SelectedCases(true);

    private static IEnumerable<object[]> SelectedCases(bool research)
    {
        foreach (PaintReferenceBank bank in Banks)
            foreach (string id in bank.CaseIds)
                if (bank.IsResearch(id) == research)
                    yield return [bank.Name, id];
    }

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Routine_and_research_cases_partition_the_resolved_capture_corpus()
    {
        (string, string)[] all = Cases().Concat(ResearchCases()).Select(row => ((string)row[0], (string)row[1])).ToArray();
        Assert.Equal(Banks.Sum(bank => bank.CaseIds.Count), all.Length);
        Assert.Equal(all.Length, all.Distinct().Count());
        foreach (PaintReferenceBank bank in Banks)
            Assert.Equal(bank.CaseIds.Count, all.Count(key => key.Item1 == bank.Name));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public void Paint_inputs_match_each_native_image(string folder, string id)
        => AssertNativeImage(folder, id, false);

    [Theory]
    [MemberData(nameof(ResearchCases))]
    [Trait("Layer", "Rendering")]
    [Trait("Category", "Research")]
    public void Extended_color_and_filter_matrix_matches_each_native_image(string folder, string id)
        => AssertNativeImage(folder, id, false);

    [Theory]
    [Trait("Layer", "Rendering")]
    [InlineData("ordered-clip-coverage", "clip-geometric-content-0330-candidate")]
    [InlineData("image-source-views", "original-crop-image-aspect-0636-candidate")]
    [InlineData("gradient-paint-replay", "material-1-candidate-1p5")]
    [InlineData("radial-axis-source-maps", "axis-rect-inside-x-local")]
    [InlineData("filter-merge-image-draw", "source-rotated-duplicate-candidate")]
    [InlineData("pattern-source-selection", "source-unused-source-text-before")]
    [InlineData("text-node-boundaries", "node-comment-x2_5")]
    [InlineData("filter-uniform-precision", "controls-opacity-midpoints-0-candidate-1")]
    [InlineData("filter-uniform-precision", "controls-graphs-srgb-13-control-1p5")]
    [InlineData("filter-uniform-precision", "controls-graphs-linear-37-control-1p5")]
    public void Representative_complete_png_paths_retain_their_native_images(string folder, string id)
        => AssertNativeImage(folder, id, true);

    public static IEnumerable<object[]> BankCases() => Banks.Select(bank => new object[] { bank.Name });

    [Theory]
    [MemberData(nameof(BankCases))]
    [Trait("Layer", "Integrity")]
    public void Paint_font_capture_recipes_match_provenance(string folder)
        => AssertFontProvenance(folder, false);

    [Theory]
    [MemberData(nameof(BankCases))]
    [Trait("Layer", "Integrity")]
    [Trait("Category", "Research")]
    public void Extended_paint_font_capture_recipes_match_provenance(string folder)
        => AssertFontProvenance(folder, true);

    private void AssertFontProvenance(string folder, bool research)
    {
        PaintReferenceBank bank = Banks.Single(bank => bank.Name == folder);
        foreach (string id in bank.CaseIds)
        {
            if (bank.IsResearch(id) != research) continue;
            JsonElement row = bank.GetCase(id);
            if (!row.TryGetProperty("font", out JsonElement font) || !font.GetBoolean()) continue;
            byte[] input = File.ReadAllBytes(Path.Combine(Folder(folder), row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.")));
            Assert.Equal(row.GetProperty("nativeFontSha256").GetString(), _fixture.Sha256);
            byte[] captureInput = input;
            if (row.TryGetProperty("captureInput", out JsonElement capturePath))
            {
                captureInput = File.ReadAllBytes(Path.Combine(Folder(folder), capturePath.GetString() ?? throw new InvalidDataException("Missing native capture path.")));
                Assert.Equal(row.GetProperty("captureInputSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(captureInput)));
            }
            string nativeInput = System.Text.Encoding.UTF8.GetString(captureInput).Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
            Assert.Equal(row.GetProperty("nativeSourceSha256").GetString(), CaptureHash(nativeInput, _fixture.CssBytes.Span));
        }

        // Hash the exact UTF-8 prefix + embedded CSS + suffix used by capture.
        // Avoid allocating a font-sized string and byte array for every input.
        static string CaptureHash(string nativeInput, ReadOnlySpan<byte> css)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(nativeInput);
            int insertion = bytes.AsSpan().IndexOf((byte)'>') + 1;
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(bytes.AsSpan(0, insertion));
            hash.AppendData(css);
            hash.AppendData(bytes.AsSpan(insertion));
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
    }

    private void AssertNativeImage(string folder, string id, bool encodePng)
    {
        JsonElement row = Banks.Single(bank => bank.Name == folder).GetCase(id);
        bool hasPlatformReferences = row.TryGetProperty("platforms", out JsonElement platforms);
        JsonElement expected = hasPlatformReferences && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            ? platforms.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux") : row;
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(folder), row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.")));
        RgbaImage reference = _fixture.References.Get(
            Path.Combine(Folder(folder), expected.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.")),
            expected.GetProperty("referenceSha256").GetString() ?? throw new InvalidDataException("Missing reference hash."));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        bool hasFonts = row.TryGetProperty("font", out JsonElement font) && font.GetBoolean();
        SvgFontSnapshot? fonts = hasFonts ? _fixture.Snapshot : null;
        if (hasFonts) Assert.Equal(row.GetProperty("nativeFontSha256").GetString(), _fixture.Sha256);
        SvgRenderOptions options = row.TryGetProperty("scale", out JsonElement scale)
            ? new(null, scale.GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts)
            : new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        // Scaled supplied-font references include a 150 by 78 pixel image.
        const int maximumReferencePixels = 16_384;
        using SvgConverter.RasterizedConversion? conversion = encodePng ? null
            : SvgConverter.PrepareRasterization(input, options, CancellationToken.None);
        RgbaImage actual;
        if (conversion is {} capture)
        {
            Assert.InRange(checked(capture.Surface.Width * capture.Surface.Height), 1, maximumReferencePixels);
            // Borrow the exact straight-alpha pixels handed to the encoder. The
            // existing comparer treats RGB at zero alpha as zero on both paths.
            actual = new RgbaImage(capture.Surface.Width, capture.Surface.Height, capture.Surface.Pixels);
        }
        else actual = PngRgbaDecoder.Decode(SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray(), maximumReferencePixels);
        if (!hasPlatformReferences || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            Assert.True(PixelComparer.Compare(reference, actual).IsExact, id);
    }

    private static string Folder(string name) => Path.Combine(ResearchCorpus.Root, name);

    /// <summary>Owns shared immutable native images and one serif face through all rendering/provenance cases; acquisition and cleanup contracts are tested separately.</summary>
    public sealed class NativePaintFixture : IAsyncLifetime
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private SvgFontSnapshot? _snapshot;

        internal NativeReferenceImages References { get; } = new();

        public NativePaintFixture()
        {
            _bytes = File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "supplied-font-labels/fonts/NotoSans-Regular.ttf"));
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(_bytes.Span));
            string css = "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(_bytes.Span) + ")}</style>";
            CssBytes = System.Text.Encoding.UTF8.GetBytes(css);
        }

        public string Sha256 { get; }
        public ReadOnlyMemory<byte> CssBytes { get; }
        public SvgFontSnapshot Snapshot => _snapshot ?? throw new InvalidOperationException("The shared rendering font is not initialized.");

        /// <summary>Prepares the corpus's sole serif/normal/400 face once; the fixture retains bytes and snapshot until every case completes.</summary>
        public async Task InitializeAsync()
        {
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "static-css-document/font-pack.json")), "public-noto-sans");
            _snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='serif'>A</text></svg>"u8.ToArray(),
                catalog, new FontProvider(_bytes), CancellationToken.None);
        }

        /// <summary>Releases the snapshot and cached images after all borrowed calls finish; no conversion or reference read may overlap fixture disposal.</summary>
        public Task DisposeAsync()
        {
            _snapshot?.Dispose();
            _snapshot = null;
            References.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FontProvider(ReadOnlyMemory<byte> bytes) : ISvgFontProgramSourceProvider
    {
        /// <summary>Returns borrowed immutable OFL bytes, observes cancellation and permits concurrent requests.</summary>
        /// <remarks>The source owns no storage; the test retains bytes through snapshot disposal.</remarks>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}
