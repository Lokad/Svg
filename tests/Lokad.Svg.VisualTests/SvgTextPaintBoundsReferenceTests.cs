using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;
using Lokad.Svg.Testing;

namespace Lokad.Svg.VisualTests;

public sealed partial class SvgTextPaintBoundsReferenceTests
{
    // Discovery reads identities only. Immutable metadata/catalog/program bytes are
    // shared; every rendering case still creates and disposes its own snapshot.
    private static readonly FrozenDictionary<string, PaintReferenceBank> Banks = new[]
    {
        "image-capture.json", "shared-layout-images.json", "filter-source-images.json",
        "precision-pattern-images.json", "paint-stage-images.json", "precision-stroke-images.json",
        "fragment-stages.json", "precision-source-images.json", "precision-screening.json",
        "decoration-ink-images.json"
    }.ToFrozenDictionary(static file => file,
        static file => new PaintReferenceBank("text-paint-bounds", file), StringComparer.Ordinal);
    public static IEnumerable<object[]> Cases()
        => CaptureCases("image-capture.json");

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Layer", "Rendering")]
    public Task Text_paint_boxes_match_native_fill_stroke_and_user_space_controls(string id)
        => AssertNativeImage("image-capture.json", id);

    public static IEnumerable<object[]> SharedLayoutCases()
        => CaptureCases("shared-layout-images.json");

    [Theory]
    [MemberData(nameof(SharedLayoutCases))]
    [Trait("Layer", "Rendering")]
    public Task Shared_layout_matches_each_native_source_image(string id)
        => AssertNativeImage("shared-layout-images.json", id);

    public static IEnumerable<object[]> FilterSourceCases()
        => CaptureCases("filter-source-images.json");

    [Theory]
    [MemberData(nameof(FilterSourceCases))]
    [Trait("Layer", "Rendering")]
    public Task Filter_source_recording_matches_each_native_source_image(string id)
        => AssertNativeImage("filter-source-images.json", id);

    public static IEnumerable<object[]> PatternMatrixCases()
        => CaptureCases("precision-pattern-images.json");

    [Theory]
    [MemberData(nameof(PatternMatrixCases))]
    [Trait("Layer", "Rendering")]
    public Task Glyph_pattern_matrices_match_each_original_native_image(string id)
        => AssertNativeImage("precision-pattern-images.json", id);

    public static IEnumerable<object[]> PaintStageCases() => CaptureCases("paint-stage-images.json");

    [Theory]
    [MemberData(nameof(PaintStageCases))]
    [Trait("Layer", "Rendering")]
    public Task Shared_paint_stages_match_each_native_image(string id)
        => AssertNativeImage("paint-stage-images.json", id);

    public static IEnumerable<object[]> StrokeDrawingCases() => CaptureCases("precision-stroke-images.json");

    [Theory]
    [MemberData(nameof(StrokeDrawingCases))]
    [Trait("Layer", "Rendering")]
    public Task Stroke_paints_retain_the_fragment_drawing_stage(string id)
        => AssertNativeImage("precision-stroke-images.json", id);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Stroke_drawing_cases_retain_original_context_and_fresh_native_triples()
    {
        using JsonDocument source = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "precision-residuals.json")));
        using JsonDocument bank = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "precision-stroke-images.json")));
        using JsonDocument native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), Required(bank.RootElement, "nativeVerification"))));
        string original = File.ReadAllText(Path.Combine(Folder(), "precision-residuals.json")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(Required(native.RootElement, "sourceManifestLfSha256"), Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))));
        Dictionary<string, JsonElement> cases = source.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
        Assert.Equal(39, cases.Count);
        Assert.Equal(5, bank.RootElement.GetProperty("cases").GetArrayLength());
        foreach (JsonElement row in bank.RootElement.GetProperty("cases").EnumerateArray())
            Assert.True(JsonElement.DeepEquals(cases[Required(row, "id")], row));
        Assert.Equal(new[] { "linux", "windows" }, native.RootElement.GetProperty("hosts").EnumerateArray()
            .Select(host => Required(host, "host")).Order(StringComparer.Ordinal));
        foreach (JsonElement host in native.RootElement.GetProperty("hosts").EnumerateArray())
        {
            Assert.Equal(3, host.GetProperty("captures").GetArrayLength());
            foreach (JsonElement process in host.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal(0, process.GetProperty("terminalExitCode").GetInt32());
                JsonElement health = process.GetProperty("health");
                Assert.Equal("healthy", Required(health, "startupHealth"));
                Assert.True(health.GetProperty("profileCleaned").GetBoolean());
                Assert.False(health.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, health.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, health.GetProperty("networkServiceCrashes").GetInt32());
            }
            Assert.Equal(cases.Keys.Order(StringComparer.Ordinal), host.GetProperty("cases").EnumerateArray().Select(row => Required(row, "id")).Order(StringComparer.Ordinal));
            foreach (JsonElement row in host.GetProperty("cases").EnumerateArray())
            {
                Assert.Equal(Required(cases[Required(row, "id")], "nativeSourceSha256"), Required(row, "nativeInputSha256"));
                JsonElement pngHashes = row.GetProperty("freshPngSha256s");
                Assert.Equal(3, pngHashes.GetArrayLength());
                foreach (JsonElement hash in pngHashes.EnumerateArray()) Assert.Equal(pngHashes[0].GetString(), hash.GetString());
                byte[] png = File.ReadAllBytes(Path.Combine(Folder(), Required(row, "reference")));
                Assert.Equal(Required(row, "referenceSha256"), Convert.ToHexStringLower(SHA256.HashData(png)));
                Assert.Equal(Required(row, "rgbaSha256"), Convert.ToHexStringLower(SHA256.HashData(PngRgbaDecoder.Decode(png, 32_768).Pixels)));
            }
        }
    }

    public static IEnumerable<object[]> FragmentStageCases() => CaptureCases("fragment-stages.json");

    [Theory]
    [MemberData(nameof(FragmentStageCases))]
    [Trait("Layer", "Rendering")]
    public Task Text_fragment_stage_controls_match_independent_vector_drawings(string id)
        => AssertNativeImage("fragment-stages.json", id);

    public static IEnumerable<object[]> SourceRectangleCases() => CaptureCases("precision-source-images.json");

    [Theory]
    [MemberData(nameof(SourceRectangleCases))]
    [Trait("Layer", "Rendering")]
    public Task Source_rectangles_and_pattern_origins_retain_each_original_native_image(string id)
        => AssertNativeImage("precision-source-images.json", id);

    public static IEnumerable<object[]> DecorationInkCases() => CaptureCases("decoration-ink-images.json");

    [Theory]
    [MemberData(nameof(DecorationInkCases))]
    [Trait("Layer", "Rendering")]
    public Task Decoration_extents_and_ink_exclusions_preserve_glyph_layout(string id)
        => AssertNativeImage("decoration-ink-images.json", id);

    private static IEnumerable<object[]> CaptureCases(string file)
    {
        foreach (string id in Banks[file].CaseIds) yield return [id];
    }

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Pattern_matrix_originals_retain_native_context_fonts_and_stable_platform_triples()
        => AssertPreciseSourceCapture("precision-pattern-images.json", 26, 13);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Shared_paint_stages_retain_native_context_fonts_and_stable_platform_triples()
        => AssertPreciseSourceCapture("paint-stage-images.json", 10, 5);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Core_math_companions_preserve_original_inputs_and_independent_healthy_native_triples()
    {
        string directory = Path.Combine(ResearchCorpus.Root, "native-math-profiles");
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "image-capture.json")));
        JsonElement root = capture.RootElement;
        byte[] originalBytes = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Folder(), "precision-pattern-images.json"))
            .Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(Required(root, "originalCaptureLfSha256"), Convert.ToHexStringLower(SHA256.HashData(originalBytes)));
        using JsonDocument original = JsonDocument.Parse(originalBytes);
        Dictionary<string, JsonElement> cases = original.RootElement.GetProperty("cases").EnumerateArray()
            .ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
        Assert.Equal(new[] { "spacing-paint-453-candidate", "spacing-paint-453-control", "spacing-paint-455-candidate", "spacing-paint-455-control" },
            root.GetProperty("cases").EnumerateObject().Select(row => row.Name).Order(StringComparer.Ordinal));
        foreach (JsonProperty row in root.GetProperty("cases").EnumerateObject())
        {
            JsonElement source = cases[row.Name], current = row.Value;
            Assert.Equal(Required(source, "sourceSha256"), Required(current, "sourceSha256"));
            Assert.Equal(Required(source, "nativeSourceSha256"), Required(current, "nativeInputSha256"));
            JsonElement reference = current.GetProperty("reference");
            byte[] png = File.ReadAllBytes(Path.Combine(directory, Required(reference, "path")));
            Assert.Equal(Required(reference, "sha256"), Convert.ToHexStringLower(SHA256.HashData(png)));
            RgbaImage decoded = PngRgbaDecoder.Decode(png, 32_768);
            Assert.Equal(240, decoded.Width); Assert.Equal(120, decoded.Height);
            Assert.Equal(Required(reference, "rgbaSha256"), Convert.ToHexStringLower(SHA256.HashData(decoded.Pixels)));
            Assert.Equal(3, current.GetProperty("captures").GetArrayLength());
            foreach (JsonElement process in current.GetProperty("captures").EnumerateArray())
            {
                Assert.Equal(Required(reference, "sha256"), Required(process, "pngSha256"));
                Assert.Equal("healthy", Required(process, "startupHealth"));
                Assert.True(process.GetProperty("profileCleaned").GetBoolean());
                Assert.False(process.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, process.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, process.GetProperty("networkServiceCrashes").GetInt32());
                Assert.Equal(0, process.GetProperty("terminalExitCode").GetInt32());
            }
        }
    }

    private static void AssertPreciseSourceCapture(string file, int caseCount, int pairCount)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), file)));
        JsonElement root = data.RootElement;
        Assert.Equal(caseCount, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(pairCount, root.GetProperty("pairs").GetArrayLength());
        Dictionary<string, JsonElement> cases = root.GetProperty("cases").EnumerateArray()
            .ToDictionary(row => Required(row, "id"), StringComparer.Ordinal);
        Dictionary<string, byte[]> programs = new(StringComparer.Ordinal);
        foreach (JsonElement font in root.GetProperty("nativeCapture").GetProperty("fonts").EnumerateArray())
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Folder(), Required(font, "path")));
            Assert.Equal(font.GetProperty("bytes").GetInt32(), bytes.Length);
            Assert.Equal(Required(font, "sha256"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            programs.Add(Required(font, "family"), bytes);
        }
        Assert.Equal(2, root.GetProperty("nativeCapture").GetProperty("fonts").GetArrayLength());
        byte[] css = System.Text.Encoding.UTF8.GetBytes("<style>@font-face{font-family:ReviewFont;font-weight:400;src:url(data:font/ttf;base64,"
            + Convert.ToBase64String(programs["ReviewFont"]) + ")}</style><style>@font-face{font-family:ReviewMono;font-weight:700;src:url(data:font/ttf;base64,"
            + Convert.ToBase64String(programs["ReviewMono"]) + ")}</style>");
        Assert.Equal(cases.Keys.Order(StringComparer.Ordinal), root.GetProperty("pairs").EnumerateArray()
            .SelectMany(pair => new[] { Required(pair, "candidate"), Required(pair, "control") }).Order(StringComparer.Ordinal));
        foreach (JsonElement row in cases.Values)
        {
            byte[] source = File.ReadAllBytes(Path.Combine(Folder(), Required(row, "input")));
            Assert.Equal(Required(row, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(source)));
            Assert.Equal(1.5, row.GetProperty("outputScale").GetDouble());
            Assert.True(row.GetProperty("font").GetBoolean());
            string native = System.Text.Encoding.UTF8.GetString(source)
                .Replace("font-family='serif'", "font-family='ReviewFont'", StringComparison.Ordinal)
                .Replace("font-family='monospace'", "font-family='ReviewMono'", StringComparison.Ordinal);
            int insertion = native.IndexOf('>') + 1;
            Assert.True(insertion > 0);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(native[..insertion]));
            hash.AppendData(css);
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(native[insertion..]));
            Assert.Equal(Required(row, "nativeSourceSha256"), Convert.ToHexStringLower(hash.GetHashAndReset()));
            Assert.NotEqual(Required(row.GetProperty("references").GetProperty("windows"), "rgbaSha256"),
                Required(row.GetProperty("references").GetProperty("linux"), "rgbaSha256"));
        }
        foreach (string host in new[] { "windows", "linux" })
        {
            foreach (JsonElement row in cases.Values)
            {
                JsonElement image = row.GetProperty("references").GetProperty(host);
                byte[] png = File.ReadAllBytes(Path.Combine(Folder(), Required(image, "path")));
                Assert.Equal(Required(image, "sha256"), Convert.ToHexStringLower(SHA256.HashData(png)));
                RgbaImage decoded = PngRgbaDecoder.Decode(png, 32_768);
                Assert.Equal(Required(image, "rgbaSha256"), Convert.ToHexStringLower(SHA256.HashData(decoded.Pixels)));
                Assert.Equal(240, decoded.Width);
                Assert.Equal(120, decoded.Height);
                JsonElement hashes = image.GetProperty("processPngSha256");
                Assert.Equal(3, hashes.GetArrayLength());
                foreach (JsonElement hash in hashes.EnumerateArray()) Assert.Equal(Required(image, "sha256"), hash.GetString());
            }
            JsonElement launches = root.GetProperty("nativeCapture").GetProperty("launches").GetProperty(host);
            Assert.Equal(3, launches.GetArrayLength());
            foreach (JsonElement launch in launches.EnumerateArray())
            {
                Assert.Equal("healthy", Required(launch, "startupHealth"));
                Assert.True(launch.GetProperty("profileCleaned").GetBoolean());
                Assert.False(launch.GetProperty("stderrTruncated").GetBoolean());
                Assert.Equal(0, launch.GetProperty("sandboxAccessFailures").GetInt32());
                Assert.Equal(0, launch.GetProperty("networkServiceCrashes").GetInt32());
            }
            foreach (JsonElement pair in root.GetProperty("pairs").EnumerateArray())
            {
                JsonElement candidate = cases[Required(pair, "candidate")], control = cases[Required(pair, "control")];
                Assert.Equal("candidate", Required(candidate, "role"));
                Assert.Equal("control", Required(control, "role"));
                Assert.Equal(Required(candidate, "originalName"), Required(control, "originalName"));
                bool equal = Required(candidate.GetProperty("references").GetProperty(host), "rgbaSha256")
                    == Required(control.GetProperty("references").GetProperty(host), "rgbaSha256");
                Assert.Equal(pair.TryGetProperty("nativeEqual", out JsonElement expected) && expected.GetProperty(host).GetBoolean(), equal);
            }
        }

    }

    private static string Required(JsonElement value, string property)
        => value.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing native capture value.");

    private static async Task AssertNativeImage(string captureFile, string id)
    {
        JsonElement row = Banks[captureFile].GetCase(id);
        JsonElement image = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        string referenceDirectory = Folder();
        using JsonDocument? coreMath = NativeMathProfile.Atan2 == NativeAtan2Profile.CoreMath && (captureFile is "precision-pattern-images.json" or "precision-screening.json")
            ? JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, "native-math-profiles", "image-capture.json"))) : null;
        if (coreMath is {} current && current.RootElement.GetProperty("cases").TryGetProperty(id, out JsonElement companion))
        {
            Assert.Equal(Required(row, "sourceSha256"), Required(companion, "sourceSha256"));
            Assert.Equal(Required(row, "nativeSourceSha256"), Required(companion, "nativeInputSha256"));
            image = companion.GetProperty("reference");
            referenceDirectory = Path.Combine(ResearchCorpus.Root, "native-math-profiles");
        }
        byte[] input = captureFile == "precision-screening.json"
            ? CreateScreeningSource(row.GetProperty("screenIndex").GetInt32(), Required(row, "role"))
            : File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        byte[]? reference = captureFile == "precision-screening.json" && !image.TryGetProperty("path", out _)
            ? null : File.ReadAllBytes(Path.Combine(referenceDirectory, Required(image, "path")));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        if (reference is not null)
            Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        SvgFontPackCatalog catalog = NotoPaintPrograms.Catalog;
        NotoPaintPrograms provider = new();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        RgbaImage decoded = PngRgbaDecoder.Decode(actual, 1_048_576);
        if (reference is not null)
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), decoded).IsExact, id);
        else
        {
            Assert.Equal(image.GetProperty("width").GetInt32(), decoded.Width);
            Assert.Equal(image.GetProperty("height").GetInt32(), decoded.Height);
            // Match PixelComparer: RGB under zero alpha is unspecified.
            for (int offset = 0; offset < decoded.Pixels.Length; offset += 4)
                if (decoded.Pixels[offset + 3] == 0) decoded.Pixels.AsSpan(offset, 3).Clear();
            Assert.Equal(Required(image, "rgbaSha256"), Convert.ToHexStringLower(SHA256.HashData(decoded.Pixels)));
        }
    }

    private static string Folder() => Path.Combine(ResearchCorpus.Root, "text-paint-bounds");

}
