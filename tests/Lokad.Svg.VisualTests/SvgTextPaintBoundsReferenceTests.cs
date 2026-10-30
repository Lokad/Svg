using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

public sealed class SvgTextPaintBoundsReferenceTests
{
    public static IEnumerable<object[]> Cases()
        => CaptureCases("image-capture.json");

    [Theory]
    [MemberData(nameof(Cases))]
    public Task Text_paint_boxes_match_native_fill_stroke_and_user_space_controls(string id)
        => AssertNativeImage("image-capture.json", id, true);

    public static IEnumerable<object[]> SharedLayoutCases()
        => CaptureCases("shared-layout-images.json");

    [Theory]
    [MemberData(nameof(SharedLayoutCases))]
    [Trait("Layer", "Rendering")]
    public Task Shared_layout_matches_each_native_source_image(string id)
        => AssertNativeImage("shared-layout-images.json", id, false);

    public static IEnumerable<object[]> FilterSourceCases()
        => CaptureCases("filter-source-images.json");

    [Theory]
    [MemberData(nameof(FilterSourceCases))]
    [Trait("Layer", "Rendering")]
    public Task Filter_source_recording_matches_each_native_source_image(string id)
        => AssertNativeImage("filter-source-images.json", id, false);

    public static IEnumerable<object[]> PatternMatrixCases()
        => CaptureCases("precision-pattern-images.json");

    [Theory]
    [MemberData(nameof(PatternMatrixCases))]
    [Trait("Layer", "Rendering")]
    public Task Glyph_pattern_matrices_match_each_original_native_image(string id)
        => AssertNativeImage("precision-pattern-images.json", id, false);

    public static IEnumerable<object[]> PaintStageCases() => CaptureCases("paint-stage-images.json");

    [Theory]
    [MemberData(nameof(PaintStageCases))]
    [Trait("Layer", "Rendering")]
    public Task Shared_paint_stages_match_each_native_image(string id)
        => AssertNativeImage("paint-stage-images.json", id, false);

    private static IEnumerable<object[]> CaptureCases(string file)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), file)));
        foreach (JsonElement row in data.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing native case id.")];
    }

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Pattern_matrix_originals_retain_native_context_fonts_and_stable_platform_triples()
        => AssertPreciseSourceCapture("precision-pattern-images.json", 26, 13);

    [Fact]
    [Trait("Layer", "Integrity")]
    public void Shared_paint_stages_retain_native_context_fonts_and_stable_platform_triples()
        => AssertPreciseSourceCapture("paint-stage-images.json", 10, 5);

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

        static string Required(JsonElement value, string property)
            => value.GetProperty(property).GetString() ?? throw new InvalidDataException("Missing pattern capture value.");
    }

    private static async Task AssertNativeImage(string captureFile, string id, bool replayPrepared)
    {
        using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), captureFile)));
        JsonElement row = data.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        JsonElement image = row.GetProperty("references").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux");
        byte[] input = File.ReadAllBytes(Path.Combine(Folder(), row.GetProperty("input").GetString() ?? throw new InvalidDataException()));
        byte[] reference = File.ReadAllBytes(Path.Combine(Folder(), image.GetProperty("path").GetString() ?? throw new InvalidDataException()));
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal(image.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reference)));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(Folder(), "font-pack.json")), "r99-fonts");
        FontPrograms provider = new();
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(input, catalog, provider, CancellationToken.None);
        SvgRenderOptions options = new(null, row.GetProperty("outputScale").GetDouble(), SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 1_048_576), PngRgbaDecoder.Decode(actual, 1_048_576)).IsExact, id);
        if (!replayPrepared) return;
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(input, catalog, provider,
            new(null, options.OutputScale, SvgColor.Transparent, SvgRenderLimits.Default), CancellationToken.None);
        Assert.Equal(actual, document.ConvertToPng(CancellationToken.None).ToArray());
    }

    private static string Folder() => Path.Combine(ResearchCorpus.Root, "text-paint-bounds");

    private sealed class FontPrograms : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = file.RelativePath == "sans.ttf" ? "supplied-font-labels" : "supplied-font-documents";
            string font = file.RelativePath == "sans.ttf" ? "NotoSans-Regular.ttf" : "NotoSansMono-Bold.ttf";
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource(file.RelativePath,
                File.ReadAllBytes(Path.Combine(ResearchCorpus.Root, name, "fonts", font))));
        }
    }
}
