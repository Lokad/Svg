using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

[Trait("Layer", "Rendering")]
public sealed class SvgPaintInputReferenceTests : IClassFixture<SvgPaintInputReferenceTests.NativeFontFixture>
{
    private readonly NativeFontFixture _fonts;
    private static readonly IReadOnlyDictionary<(string Folder, string Id), JsonElement> CaseIndex;
    private static readonly HashSet<(string Folder, string Id)> ResearchColorAndFilterCases;

    public SvgPaintInputReferenceTests(NativeFontFixture fonts) => _fonts = fonts;

    static SvgPaintInputReferenceTests()
    {
        Dictionary<(string Folder, string Id), JsonElement> cases = new();
        foreach (string folder in new[] { "offset-merge-inputs", "linear-shape-source-maps", "radial-axis-source-maps", "gradient-spread-stages", "gradient-paint-replay", "gradient-factory-colors", "linear-periodic-stops", "gradient-stop-preparation", "gradient-color-alpha", "filter-uniform-precision", "solid-paint-alpha", "pattern-source-selection", "text-node-boundaries", "hairline-clip-rounding" })
        {
            using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(folder), "capture.json")));
            foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            {
                string id = row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case id.");
                // Independent clones retain managed metadata after the source document is disposed.
                cases.Add((folder, id), row.Clone());
            }
        }
        CaseIndex = cases;
        ResearchColorAndFilterCases = new();
        foreach (string folder in new[] { "gradient-color-alpha", "filter-uniform-precision" })
        {
            using JsonDocument selection = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(folder), "research-cases.json")));
            foreach (JsonElement selected in selection.RootElement.GetProperty("cases").EnumerateArray())
            {
                string id = selected.GetString() ?? throw new InvalidDataException("Missing research case id.");
                if (!cases.ContainsKey((folder, id))) throw new InvalidDataException("Research selection names an unknown native case.");
                if (!ResearchColorAndFilterCases.Add((folder, id))) throw new InvalidDataException("Research selection contains a duplicate case.");
            }
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach ((string folder, string id) in CaseIndex.Keys)
            if (!ResearchColorAndFilterCases.Contains((folder, id)))
                yield return [folder, id];
    }

    public static IEnumerable<object[]> ResearchCases()
    {
        foreach ((string folder, string id) in CaseIndex.Keys)
            if (ResearchColorAndFilterCases.Contains((folder, id)))
                yield return [folder, id];
    }

    [Fact]
    public void Routine_and_research_cases_partition_the_unchanged_capture_corpus()
    {
        (string, string)[] all = Cases().Concat(ResearchCases()).Select(row => ((string)row[0], (string)row[1])).ToArray();
        Assert.Equal(CaseIndex.Count, all.Length);
        Assert.Equal(CaseIndex.Count, all.Distinct().Count());
        Assert.All(all, key => Assert.True(CaseIndex.ContainsKey(key)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Paint_inputs_match_each_native_image(string folder, string id)
        => AssertNativeImage(folder, id);

    [Theory]
    [MemberData(nameof(ResearchCases))]
    [Trait("Category", "Research")]
    public void Extended_color_and_filter_matrix_matches_each_native_image(string folder, string id)
        => AssertNativeImage(folder, id);

    private void AssertNativeImage(string folder, string id)
    {
        JsonElement row = CaseIndex[(folder, id)];
        bool hasPlatformReferences = row.TryGetProperty("platforms", out JsonElement platforms);
        JsonElement expected = hasPlatformReferences && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            ? platforms.GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux") : row;
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(folder), row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing source path.")));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(folder), expected.GetProperty("reference").GetString() ?? throw new InvalidDataException("Missing reference path.")));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(expected.GetProperty("referenceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        bool hasFonts = row.TryGetProperty("font", out JsonElement font) && font.GetBoolean();
        SvgFontSnapshot? fonts = hasFonts ? _fonts.Snapshot : null;
        if (hasFonts)
        {
            Assert.Equal(row.GetProperty("nativeFontSha256").GetString(), _fonts.Sha256);
            string nativeInput = System.Text.Encoding.UTF8.GetString(input).Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal);
            nativeInput = nativeInput.Insert(nativeInput.IndexOf('>') + 1, _fonts.Css);
            Assert.Equal(row.GetProperty("nativeSourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nativeInput))));
        }
        SvgRenderOptions options = row.TryGetProperty("scale", out JsonElement scale)
            ? new(null, scale.GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, fonts)
            : new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        // Scaled supplied-font references include a 150 by 78 pixel image.
        const int maximumReferencePixels = 16_384;
        if (!hasPlatformReferences || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, maximumReferencePixels), PngRgbaDecoder.Decode(actual, maximumReferencePixels)).IsExact, id);
    }

    private static string Folder(string name) => Path.Combine(AppContext.BaseDirectory, "Research", name);

    /// <summary>Owns one immutable serif face for rendering tests; font acquisition and request/cleanup behavior are tested separately as contracts.</summary>
    public sealed class NativeFontFixture : IAsyncLifetime
    {
        private readonly ReadOnlyMemory<byte> _bytes;
        private SvgFontSnapshot? _snapshot;

        public NativeFontFixture()
        {
            _bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Research/supplied-font-labels/fonts/NotoSans-Regular.ttf"));
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(_bytes.Span));
            Css = "<style>@font-face{font-family:ReviewFont;src:url(data:font/ttf;base64," + Convert.ToBase64String(_bytes.Span) + ")}</style>";
        }

        public string Sha256 { get; }
        public string Css { get; }
        public SvgFontSnapshot Snapshot => _snapshot ?? throw new InvalidOperationException("The shared rendering font is not initialized.");

        /// <summary>Prepares the corpus's sole serif/normal/400 face once; the fixture retains bytes and snapshot until every case completes.</summary>
        public async Task InitializeAsync()
        {
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Research/static-css-document/font-pack.json")), "public-noto-sans");
            _snapshot = await SvgFontSnapshot.CreateAsync("<svg xmlns='http://www.w3.org/2000/svg'><text font-family='serif'>A</text></svg>"u8.ToArray(),
                catalog, new FontProvider(_bytes), CancellationToken.None);
        }

        /// <summary>Releases the snapshot after all borrowed rendering calls finish; no conversion may overlap fixture disposal.</summary>
        public Task DisposeAsync()
        {
            _snapshot?.Dispose();
            _snapshot = null;
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
