using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects the complete CC0 document, supplied Mono Bold positioning and exact output contracts.</summary>
public sealed class SvgSuppliedFontDocumentTests
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "Research", "supplied-font-documents");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Declared_document_and_controls_preserve_source_through_twelve_apis(string identity)
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
            using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog(), Provider(), new(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default, null), CancellationToken.None);
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
            PixelComparison comparison = PixelComparer.Compare(PngRgbaDecoder.Decode(reference, 16_777_216), PngRgbaDecoder.Decode(png, 16_777_216));
            Assert.True(comparison.DimensionsMatch);
            Assert.True(comparison.IsExact);
        }
    }

    [Fact]
    public void Public_font_bank_pins_evaluation_data_and_frozen_native_reference_triples()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.Equal(8, root.GetProperty("cases").GetArrayLength());
        Assert.Equal(1, root.GetProperty("cases").EnumerateArray().Count(c => Required(c, "role") == "font-contract-fixture"));
        Assert.Equal(Required(root, "fontProgramSha256"), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSansMono-Bold.ttf")))));
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

    private static ValueTask<SvgFontSnapshot> Prepare(byte[] input) => SvgFontSnapshot.CreateAsync(input, Catalog(), Provider(), CancellationToken.None);

    [Theory]
    [InlineData("scalar-x-reset", "<text x='8' y='48'>A</text><text x='32' y='48'>A</text>")]
    [InlineData("scalar-y-reset", "<text x='8' y='48'>A</text>")]
    [InlineData("nested-absolute-relative", "<text x='20' y='48'>A</text>")]
    [InlineData("continued-current-position", "<text x='8' y='48'>A</text><text x='25.6' y='48'>A</text>")]
    [InlineData("empty-path-body", "<text x='8' y='48'>A</text>")]
    public async Task Span_and_empty_path_mechanisms_match_independently_positioned_text(string identity, string expectedBody)
    {
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, identity + ".svg"));
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgPreparedFont font = Assert.Single(snapshot.Fonts);
        Assert.Equal("Noto Sans Mono", font.Resolution.Resolution.Face.ResolvedFamily);
        SvgRenderOptions options = new(new SvgViewport(64, 64), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] expected = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' font-family='monospace' font-weight='bold' font-size='16'>" + expectedBody + "</svg>");

        Assert.Equal(SvgConverter.ConvertToPng(expected, options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task The_complete_document_renders_and_releases_scene_owners_while_pixel_acceptance_remains_separate()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, "svg-translate-textpath.svg"));
        using SvgFontSnapshot snapshot = await Prepare(input);
        Assert.Single(snapshot.Fonts);
        RenderBudget budget = new(long.MaxValue);
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
        {
            Assert.False(scene.HasUnsupportedChild);
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(new SvgViewport(512, 256), 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] target = Enumerable.Repeat((byte)0xCC, 64).ToArray();
        Assert.Equal(SvgErrorCode.InsufficientDestination, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, target, options, CancellationToken.None)).ErrorCode);
        Assert.All(target, value => Assert.Equal(0xCC, value));
        using MemoryStream stream = new();
        int written = SvgConverter.ConvertToPng(input, stream, options, CancellationToken.None);
        Assert.Equal(written, stream.Length);
        byte[] png = stream.ToArray();
        Assert.Equal(png, SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
        RgbaImage image = PngRgbaDecoder.Decode(png, 16_777_216);
        Assert.Equal(512, image.Width);
        Assert.Equal(256, image.Height);
    }

    [Fact]
    public void Missing_corrupt_and_substituted_embeddings_prove_the_declared_face_is_used()
    {
        using JsonDocument proof = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-use-proof", "qualification.json")));
        foreach (JsonElement platform in proof.RootElement.GetProperty("platforms").EnumerateArray())
        {
            Assert.Equal(3, platform.GetProperty("cases").GetArrayLength());
            foreach (JsonElement item in platform.GetProperty("cases").EnumerateArray())
            {
                Assert.True(item.GetProperty("changedPixelsFromIntendedFont").GetInt32() > 0);
                string reference = Required(item, "reference");
                string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-use-proof", reference))));
                JsonElement captures = item.GetProperty("captures");
                Assert.Equal(3, captures.GetArrayLength());
                foreach (JsonElement capture in captures.EnumerateArray())
                {
                    Assert.Equal(actual, Required(capture, "pngSha256"));
                    Assert.Equal("healthy", Required(capture, "startupHealth"));
                    Assert.True(capture.GetProperty("profileCleaned").GetBoolean());
                }
            }
        }
    }

    private static SvgFontPackCatalog Catalog()
        => SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "font-pack.json")), "public-noto-sans-mono");

    private static MemoryProvider Provider()
        => new(File.ReadAllBytes(Path.Combine(DirectoryPath, "fonts", "NotoSansMono-Bold.ttf")));

    private static string Required(JsonElement item, string name)
        => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name);

    /// <summary>Returns the immutable caller-owned public test program, observes cancellation and performs no I/O or host discovery; safe for concurrent requests.</summary>
    private sealed class MemoryProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (file.RelativePath != "fonts/NotoSansMono-Bold.ttf") throw new InvalidDataException("Unexpected test font request.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans-mono", bytes));
        }
    }
}
