using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using Lokad.Svg.VisualDiff;

namespace Lokad.Svg.VisualTests;

/// <summary>Protects the unchanged local text-path document, separate controls and caller-supplied font contract.</summary>
public sealed class SvgSuppliedFontTextPathTests
{
    private static string DirectoryPath => Path.Combine(ResearchCorpus.Root, "supplied-font-text-paths");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            if (Required(item, "role") != "font-contract-fixture") yield return new object[] { Required(item, "id") };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Complete_text_path_document_and_controls_match_frozen_references_through_twelve_apis(string identity)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "manifest.json")));
        JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => Required(c, "id") == identity);
        byte[] input = File.ReadAllBytes(Path.Combine(DirectoryPath, Required(item, "input")));
        byte[] unchanged = input.ToArray();
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
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
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
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
        Assert.Equal(1, root.GetProperty("cases").EnumerateArray().Count(c => Required(c, "role") == "font-contract-fixture"));
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

    private static string FontDirectory => Path.Combine(ResearchCorpus.Root, "supplied-font-labels");

    private static ValueTask<SvgFontSnapshot> Prepare(byte[] input) => SvgFontSnapshot.CreateAsync(input, Catalog(), Provider(), CancellationToken.None);

    [Theory]
    [InlineData("M8 48")]
    [InlineData("")]
    [InlineData("M8 48 H9")]
    public async Task Empty_or_short_paths_suppress_glyphs_without_host_font_fallback(string path)
    {
        byte[] input = Document("<defs><path id='p' d='" + path + "'/></defs><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath></text>");
        using SvgFontSnapshot snapshot = await Prepare(input);
        Assert.Single(snapshot.Fonts);
        byte[] png = SvgConverter.ConvertToPng(input, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot), CancellationToken.None).ToArray();
        RgbaImage image = PngRgbaDecoder.Decode(png, 4_096);
        Assert.All(image.Pixels, value => Assert.Equal(0, value));
        Assert.Equal(SvgErrorCode.UnsupportedFeature, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData("<text font-family='Example' font-size='16'><textPath href='#missing'>A</textPath></text>", SvgErrorCode.UnresolvedResource)]
    [InlineData("<defs><rect id='p' width='40' height='40'/></defs><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath></text>", SvgErrorCode.UnresolvedResource)]
    [InlineData("<defs><path id='p' d='M8 48 Q32 16 56 48 Z M8 48 H16'/></defs><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath></text>", SvgErrorCode.UnsupportedFeature)]
    public async Task Reference_and_layout_failures_release_queued_runs_and_leave_output_untouched(string body, SvgErrorCode category)
    {
        byte[] input = Document(body);
        using SvgFontSnapshot snapshot = await Prepare(input);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        SvgException failure = Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None));
        Assert.Equal(category, failure.ErrorCode);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        byte[] target = Enumerable.Repeat((byte)0xCC, 64).ToArray();
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(category, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, target, options, CancellationToken.None)).ErrorCode);
        Assert.All(target, value => Assert.Equal(0xCC, value));
        using MemoryStream output = new();
        Assert.Equal(category, Assert.Throws<SvgException>(() => SvgConverter.ConvertToPng(input, output, options, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData("<defs><path id='p' d='M8 48 H56'/></defs><text font-family='Example' font-size='16'>A<textPath href='#p'>A</textPath></text>")]
    [InlineData("<defs><path id='p' d='M8 48 H56'/></defs><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath>A</text>")]
    public async Task Original_mixed_path_inputs_render_at_independent_initial_and_resumed_positions(string body)
    {
        byte[] input = Document(body), unchanged = input.ToArray();
        using SvgFontSnapshot snapshot = await Prepare(input);
        string reference = body.Replace("A<textPath href='#p'>A</textPath>", "A</text><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath>", StringComparison.Ordinal)
            .Replace("</textPath>A</text>", "</textPath></text><text font-family='Example' font-size='16' x='56' y='48'>A</text>", StringComparison.Ordinal);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(1_000_000));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.Equal(SvgConverter.ConvertToPng(Document(reference), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(root, options, budget, CancellationToken.None).ToArray());
        Assert.Equal(0, budget.Snapshot().CurrentBytes); Assert.Equal(unchanged, input);
    }

    [Fact]
    public async Task Closed_contour_keeps_the_original_curved_prefix_and_releases_queued_runs()
    {
        // This original valid input previously asserted UnsupportedFeature.
        byte[] input = Document("<defs><path id='p' d='M8 48 Q32 16 56 48 Z'/></defs><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath></text>");
        byte[] open = Document("<defs><path id='p' d='M8 48 Q32 16 56 48'/></defs><text font-family='Example' font-size='16'><textPath href='#p'>A</textPath></text>");
        using SvgFontSnapshot snapshot = await Prepare(input);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, snapshot, CancellationToken.None))
            Assert.True(budget.Snapshot().CurrentBytes > 0);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        byte[] actual = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        Assert.Equal(SvgConverter.ConvertToPng(open, options, CancellationToken.None).ToArray(), actual);
        Assert.Contains(PngRgbaDecoder.Decode(actual, 4_096).Pixels, value => value != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forward_path_runs_preserve_paint_order_opacity_and_later_reference_slots(bool gradientFirst)
    {
        const string textPath = "<text font-family='Example' font-size='16' y='12'><textPath href='#p'>AA</textPath></text>";
        const string directText = "<text font-family='Example' font-size='16' x='8' y='48'>AA</text>";
        const string gradient = "<rect x='5' y='35' width='35' height='10' fill='url(#g)' stroke='red' stroke-width='2'/>";
        const string rest = "<use href='#r'/><text font-family='Example' font-size='16' x='24' dy='-4'><textPath href='#p'>AA</textPath></text>";
        const string definitions = "<defs><path id='p' d='M8 48 H56'/><rect id='r' x='10' y='37' width='12' height='6' fill='green'/><linearGradient id='g'><stop offset='0' stop-color='blue'/><stop offset='1' stop-color='white'/></linearGradient></defs>";
        string actual = "<g opacity='0.5'>" + (gradientFirst ? gradient + textPath : textPath + gradient) + rest + "</g>" + definitions;
        string expected = "<g opacity='0.5'>" + (gradientFirst ? gradient + directText : directText + gradient) + rest.Replace("x='24' dy='-4'><textPath href='#p'>AA</textPath>", "x='32' y='44'>AA", StringComparison.Ordinal) + "</g>" + definitions;
        byte[] input = Document(actual);
        using SvgFontSnapshot snapshot = await Prepare(input);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, snapshot);
        Assert.Equal(SvgConverter.ConvertToPng(Document(expected), options, CancellationToken.None).ToArray(), SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray());
    }

    [Fact]
    public async Task Queued_run_budget_denial_work_denial_and_retry_preserve_borrowed_font_bytes()
    {
        byte[] input = Document("<text font-family='Example' font-size='16'><textPath href='#p'>AA</textPath></text><defs><path id='p' d='M8 48 H56'/></defs>");
        using SvgFontSnapshot snapshot = await Prepare(input);
        byte[] original = Assert.Single(snapshot.Fonts).Bytes.ToArray();
        RenderBudget accounting = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        using (SvgRoot root = SvgRootParser.Parse(input, SvgRenderLimits.Default, accounting, snapshot, CancellationToken.None)) Assert.True(accounting.Snapshot().CurrentBytes > 0);
        long peak = accounting.Snapshot().PeakBytes;
        Assert.Equal(0, accounting.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(peak - 1));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, snapshot, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        RenderBudget cancelledBudget = new RenderBudget(new HostMemoryBudget(long.MaxValue));
        Assert.Throws<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, cancelledBudget, snapshot, cancelled.Token));
        Assert.Equal(0, cancelledBudget.Snapshot().CurrentBytes);
        using SvgRoot retried = SvgRootParser.Parse(input, SvgRenderLimits.Default, accounting, snapshot, CancellationToken.None);
        Assert.Equal(original, Assert.Single(snapshot.Fonts).Bytes.ToArray());
    }

    private static byte[] Document(string body) => System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>" + body + "</svg>");

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
        => SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(FontDirectory, "font-pack.json")), "public-noto-sans");

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
