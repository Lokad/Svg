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
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    public static IEnumerable<object[]> CascadeCases()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Research", "root-resource-cascade");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString() ?? throw new InvalidDataException()];
    }

    [Theory]
    [InlineData("remaining-label-path-sheet")]
    [InlineData("nested-list-rotation")]
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

    private static async Task ValidateDeclared(string directory, string identity, SvgFontFaceRequest[] requests)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        JsonElement root = manifest.RootElement;
        JsonElement item = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
        byte[] unchanged = input.ToArray();
        Assert.Equal(Required(item, "sourceSha256"), Convert.ToHexStringLower(SHA256.HashData(input)));
        Assert.Equal("CC0-1.0", Required(item, "license"));
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
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, Required(root, "fontPack"))), "public-noto-layout");
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
