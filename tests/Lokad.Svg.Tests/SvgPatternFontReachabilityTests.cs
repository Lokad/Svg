using System.Security.Cryptography;
using System.Text.Json;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

public sealed class SvgPatternFontReachabilityTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestData", "pattern-source-selection");
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "text-paint-bounds", "sans.ttf"));
    private static readonly SvgFontPackCatalog Catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(Folder, "font-pack.json")), "public-noto-sans");
    private static readonly IReadOnlyDictionary<string, JsonElement> Rows;

    static SvgPatternFontReachabilityTests()
    {
        using JsonDocument capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder, "capture.json")));
        Rows = capture.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(
            row => row.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case ID."), row => row.Clone());
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (string id in Rows.Keys) yield return [id];
    }

    [Fact]
    public async Task Geometry_typography_stays_inert_during_pattern_selection()
    {
        byte[] input = File.ReadAllBytes(Path.Combine(Folder, "inert-typography.svg"));
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, Catalog, provider, CancellationToken.None);
        Assert.Equal(0, provider.Requests);
        byte[] direct = SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog, provider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(0, provider.Requests);
        Assert.Equal(direct, prepared.ConvertToPng(CancellationToken.None).ToArray());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Only_sampled_pattern_sources_acquire_font_programs(string id)
    {
        JsonElement row = Rows[id];
        byte[] input = ReadInput(row), unchanged = input.ToArray();
        Assert.Equal(row.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(input)));
        int expectedPrograms = row.GetProperty("expectedFontPrograms").GetInt32();
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, Catalog, provider, CancellationToken.None);
        Assert.Equal(expectedPrograms, provider.Requests);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        RenderBudget parser = new RenderBudget(new HostMemoryBudget(8_000_000)), raster = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, options.Limits, parser, fonts, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, options, raster, CancellationToken.None))
            Assert.Equal(NativeHash(row), Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
        Assert.Equal(0, parser.Snapshot().CurrentBytes);
        Assert.Equal(0, raster.Snapshot().CurrentBytes);

        byte[] direct = SvgConverter.ConvertToPng(input, options, CancellationToken.None).ToArray();
        if (expectedPrograms == 0)
            Assert.Equal(direct, SvgConverter.ConvertToPng(input, SvgRenderOptions.Default, CancellationToken.None).ToArray());
        FontProvider preparedProvider = new();
        using SvgPreparedDocument prepared = await SvgPreparedDocument.CreateAsync(input, Catalog, preparedProvider, SvgRenderOptions.Default, CancellationToken.None);
        Assert.Equal(expectedPrograms, preparedProvider.Requests);
        Assert.Equal(direct, prepared.ConvertToPng(CancellationToken.None).ToArray());
        Assert.Equal(unchanged, input);
        Assert.Equal(row.GetProperty("nativeFontSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(FontBytes)));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Source_visits_release_owned_storage_after_denial_and_cancellation(string id)
    {
        JsonElement row = Rows[id];
        byte[] input = ReadInput(row);
        FontProvider provider = new();
        using SvgFontSnapshot fonts = await SvgFontSnapshot.CreateAsync(input, Catalog, provider, CancellationToken.None);
        RenderBudget budget = new RenderBudget(new HostMemoryBudget(8_000_000));
        using (SvgRoot scene = SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, CancellationToken.None)) { }
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        RenderBudget denied = new RenderBudget(new HostMemoryBudget(Math.Max(1, budget.Snapshot().PeakBytes / 2)));
        Assert.Equal(SvgErrorCode.LimitExceeded, Assert.Throws<SvgException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, denied, fonts, CancellationToken.None)).ErrorCode);
        Assert.Equal(0, denied.Snapshot().CurrentBytes);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(cancellation.Token, Assert.ThrowsAny<OperationCanceledException>(() => SvgRootParser.Parse(input, SvgRenderLimits.Default, budget, fonts, cancellation.Token)).CancellationToken);
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
        SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, fonts);
        using (SvgRoot retry = SvgRootParser.Parse(input, options.Limits, budget, fonts, CancellationToken.None))
        using (SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(retry, options, budget, CancellationToken.None))
            Assert.Equal(NativeHash(row), Convert.ToHexStringLower(SHA256.HashData(surface.Pixels)));
        Assert.Equal(0, budget.Snapshot().CurrentBytes);
    }

    private static byte[] ReadInput(JsonElement row)
        => File.ReadAllBytes(Path.Combine(Folder, row.GetProperty("input").GetString() ?? throw new InvalidDataException("Missing input.")));

    private static string NativeHash(JsonElement row)
        => row.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "windows" : "linux")
            .GetProperty("rgbaSha256").GetString() ?? throw new InvalidDataException("Missing native pixels.");

    private sealed class FontProvider : ISvgFontProgramSourceProvider
    {
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);

        /// <summary>Returns immutable borrowed OFL bytes through snapshot disposal, observes cancellation and permits concurrent requests.</summary>
        /// <remarks>The test retains the shared byte array; each returned source owns no storage.</remarks>
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requests);
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", FontBytes));
        }
    }
}
