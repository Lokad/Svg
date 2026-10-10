using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects public-font admission, layout and exact outputs; the selected document and two controls are exact on both platforms, while remaining control and composite promises stay open.</summary>
public sealed class SvgSuppliedFontLabelTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-labels");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray()) yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Supplied_public_face_layout_matches_independent_font_unit_metrics(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        Assert.Equal("Noto Sans", font.Resolution.Resolution.Face.ResolvedFamily);
        using JsonDocument independent = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "metrics.json")));
        JsonElement metrics = independent.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        JsonElement first = metrics.GetProperty("glyphs")[0];
        SvgPositionedGlyph[] glyphs = SvgGlyphLayout.Layout(font, Required(metrics, "text"), metrics.GetProperty("fontSize").GetDouble(), first.GetProperty("x").GetDouble(), first.GetProperty("y").GetDouble(), CancellationToken.None);
        Assert.Equal(metrics.GetProperty("glyphs").GetArrayLength(), glyphs.Length);
        for (int index = 0; index < glyphs.Length; index++)
        {
            JsonElement expected = metrics.GetProperty("glyphs")[index];
            Assert.Equal(expected.GetProperty("glyphIndex").GetUInt16(), glyphs[index].GlyphIndex);
            Assert.Equal(expected.GetProperty("x").GetDouble(), glyphs[index].X, 10);
            Assert.Equal(expected.GetProperty("y").GetDouble(), glyphs[index].Y, 10);
            Assert.Equal(expected.GetProperty("advance").GetDouble(), glyphs[index].Advance, 10);
        }
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Public_font_output_contracts_preserve_source_and_existing_composite_boundary(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        byte[] unchanged = input.ToArray();
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        if (identity == "composite-accents")
        {
            byte[] untouched = Enumerable.Repeat((byte)0xCC, 64).ToArray();
            byte[] target = untouched.ToArray();
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, target, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(untouched, target);
            using MemoryStream rejected = new();
            Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, rejected, options, CancellationToken.None)).ErrorCode);
            Assert.Equal(0, rejected.Length);
            return;
        }
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
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog(), Provider(), SvgRenderOptions.Default, CancellationToken.None);
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
        if (OperatingSystem.IsLinux() || identity is "original-logo-live-text-64x64-transparent" or "pair-and-space" or "small-fractional-origin")
        {
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, OperatingSystem.IsLinux() ? "linuxReference" : "reference")));
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 16_777_216), PngRgbaDecoder.Decode(png, 16_777_216)).IsExact);
        }
    }

    [Fact]
    public void Public_font_bank_pins_evaluation_data_and_frozen_native_reference_triples()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(8, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(1, root.GetProperty("cases").EnumerateArray().Count(c => Required(c, "role") == "document-output"));
        Assert.Equal(Required(root, "fontProgramSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSans-Regular.ttf")))));
        Assert.True(File.ReadAllText(Path.Combine(DirectoryPath, "fonts", "OFL.txt")).Contains("SIL OPEN FONT LICENSE", StringComparison.Ordinal));
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

    private static ValueTask<SvgFontSnapshot> Prepare(byte[] input)
        => SvgFontSnapshot.CreateAsync(input, Catalog(), Provider(), CancellationToken.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fits_at_output_size_and_maps_device_contours_once(bool useViewBox)
    {
        string root = useViewBox ? "width=\"128\" height=\"128\" viewBox=\"0 0 64 64\"" : "width=\"64\" height=\"64\"";
        byte[] input = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" " + root + "><text font-family=\"Example\" font-size=\"48\" x=\"4\" y=\"52\">A</text></svg>");
        double scale = useViewBox ? 1 : 2;
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] png = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.Equal(128, PngRgbaDecoder.Decode(png, 16_384).Width);
        SvgRenderOptions preparationOptions = new(null, scale, SvgColor.Transparent, SvgRenderLimits.Default, null);
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog(), Provider(), preparationOptions, CancellationToken.None);
        Assert.Equal(png, prepared.ConvertToPng(CancellationToken.None).ToArray());
        if (OperatingSystem.IsLinux())
        {
            // The existing frozen A96 output has the same mapped size and origin.
            byte[] reference = File.ReadAllBytes(Path.Combine(DirectoryPath, "references", "linux64", "large-outline.png"));
            Assert.True(PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 16_384), PngRgbaDecoder.Decode(png, 16_384)).IsExact);
        }
    }

    [Fact]
    public async Task Positioned_source_releases_denied_scratch_and_borrows_the_font_lifetime()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "original", "logo-live-text.svg"));
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        byte[] original = font.Bytes.ToArray();
        var budget = new RenderBudget(200);
        using (SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, new(36, 8, 48, 30.672), 48, budget))
        {
            Assert.Equal(128, budget.Snapshot().CurrentBytes);
            Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => source.CreateLatinLightPath(1, 0, 1, 0, budget, new(long.MaxValue), CancellationToken.None)).ErrorCode);
            Assert.Equal(128, budget.Snapshot().CurrentBytes);
            Assert.Equal(original, font.Bytes.ToArray());
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        Assert.Equal(original, font.Bytes.ToArray());
        var retryBudget = new RenderBudget(long.MaxValue);
        SvgGlyphRasterSource retry = SvgGlyphRasterSource.Create(font, new(36, 8, 48, 30.672), 48, retryBudget);
        using (SvgAuthoredPath path = retry.CreateLatinLightPath(1, 0, 1, 0, retryBudget, new(long.MaxValue), CancellationToken.None)) Assert.True(path.Commands.Length > 0);
        Assert.Equal(128, retryBudget.Snapshot().CurrentBytes);
        retry.Dispose();
        Assert.Equal(0, retryBudget.Snapshot().CurrentBytes);
        Assert.Throws<InvalidOperationException>(() => retry.CreateLatinLightPath(1, 0, 1, 0, retryBudget, new(long.MaxValue), CancellationToken.None));
        Assert.Equal(original, font.Bytes.ToArray());
    }

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
                PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(positive, 4_096), PngRgbaDecoder.Decode(negative, 4_096));
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
        => new(File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSans-Regular.ttf")));

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
