using BenchmarkDotNet.Attributes;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Security.Cryptography;
using System.Text;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures a retained public-font label with acquisition outside the timed complete conversion.</summary>
[MemoryDiagnoser]
public class SvgSuppliedLabelBenchmarks
{
    private byte[] _svg = [];
    private SvgFontSnapshot? _snapshot;
    private SvgRenderOptions _options = SvgRenderOptions.Default;

    [GlobalSetup]
    public async Task SetUp()
    {
        string directory = Path.Combine(FindRepository(), "tests", "Research", "supplied-font-labels");
        _svg = File.ReadAllBytes(Path.Combine(directory, "pair-and-space.svg"));
        SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(directory, "font-pack.json")), "public-noto-sans");
        MemoryProvider provider = new(File.ReadAllBytes(Path.Combine(directory, "fonts", "NotoSans-Regular.ttf")));
        _snapshot = await SvgFontSnapshot.CreateAsync(_svg, catalog, provider, CancellationToken.None);
        _options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, _snapshot);

        static string FindRepository()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                for (DirectoryInfo? candidate = new(start); candidate is not null; candidate = candidate.Parent)
                    if (File.Exists(Path.Combine(candidate.FullName, "Lokad.Svg.slnx"))) return candidate.FullName;
            throw new DirectoryNotFoundException("Run label benchmarks from a Lokad.Svg checkout.");
        }
    }

    [Benchmark]
    public int ConvertPublicLabel() => SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None).Length;

    [GlobalCleanup]
    public void CleanUp()
    {
        _snapshot?.Dispose();
        _snapshot = null;
    }

    /// <summary>Returns immutable caller-owned test font bytes, observes cancellation and is safe for concurrent requests; no I/O or host discovery occurs.</summary>
    private sealed class MemoryProvider(byte[] bytes) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (file.RelativePath != "fonts/NotoSans-Regular.ttf") throw new InvalidDataException("Unexpected benchmark font request.");
            return ValueTask.FromResult<ISvgFontProgramSource?>(new SvgMemoryFontProgramSource("public-noto-sans", bytes));
        }
    }
}

/// <summary>Measures supplied-font text acquisition, lowering and end-to-end preparation without including input setup.</summary>
[MemoryDiagnoser]
public class SvgTextBenchmarks
{
    // Synthetic triangle font bytes (342 bytes covering A), embedded so benchmarks share the
    // admitted test font without duplicating its builders or touching private assets.
    private const string TriangleFontBase64 = "AAEAAAAHAAAAAAAAaGVhZAAAAAAAAAB8AAAANm1heHAAAAAAAAAAsgAAAAZoaGVhAAAAAAAAALgAAAAkaG10eAAAAAAAAADcAAAACmNtYXAAAAAAAAAA5gAAACxsb2NhAAAAAAAAARIAAAAQZ2x5ZgAAAAAAAAEiAAAANAAAAAAAAAAAAAAAAAAAAAAAAAPoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgH0AAACWAAAAAAAAAABAAMAAQAAAAwABAAgAAAABAAAAAAAAABB//8AAABB////wAABAAAAAAAAAAAAAAAAAAAAIAAAADQAAQAAAAAAAAAAAAIAADczJwoKCgoKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly TextBenchmarkState _state = TextBenchmarkState.Create();
    private readonly byte[] _fallbackSvg = "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><text font-family='Example' font-size='800' x='8' y='48'>A</text></svg>"u8.ToArray();

    [GlobalSetup(Target = nameof(ConvertFallbackLabel))]
    public void VerifyFallbackRoute()
    {
        RenderBudget budget = new(long.MaxValue);
        using SvgRoot root = SvgRootParser.Parse(_fallbackSvg, SvgRenderLimits.Default, budget, _state.Snapshot, CancellationToken.None);
        SvgGeometry geometry = root.Geometries.Single();
        SvgGlyphRasterSource source = geometry.Glyph.Source ?? throw new InvalidOperationException("Expected a supplied-font glyph source.");
        bool fitted = source.HasGlyphMap || source.UsesDeviceFitting(budget, CancellationToken.None);
        bool native = OperatingSystem.IsLinux() && fitted
            || OperatingSystem.IsWindows() && fitted && (source.HasGlyphMap || source.HasTrueTypePrograms(budget, CancellationToken.None));
        if (native)
            throw new InvalidOperationException("This font selects native coverage on this platform; filter out ConvertFallbackLabel when measuring native routes.");
    }

    /// <summary>Measures complete conversion through the verified fallback path; source and caller-supplied font acquisition are outside timing.</summary>
    [Benchmark]
    public int ConvertFallbackLabel()
        => SvgConverter.ConvertToPng(_fallbackSvg, new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default, _state.Snapshot), CancellationToken.None).Length;

    [Benchmark]
    public async Task<int> CreateSnapshotAsync()
    {
        using SvgFontSnapshot snapshot = await SvgFontSnapshot.CreateAsync(_state.Svg, _state.Catalog, _state.Provider, CancellationToken.None);
        return snapshot.Fonts.Count;
    }

    [Benchmark]
    public int LowerStyledText()
    {
        using SvgRoot root = SvgRootParser.Parse(_state.Svg, SvgRenderLimits.Default, new RenderBudget(1_048_576), _state.Snapshot, CancellationToken.None);
        return root.Geometries.Length;
    }

    [Benchmark]
    public async Task<int> ConvertPreparedStyledTextAsync()
    {
        using SvgPreparedDocument document = await SvgPreparedDocument.CreateAsync(_state.Svg, _state.Catalog, _state.Provider, _state.Options, CancellationToken.None);
        return document.ConvertToPng(CancellationToken.None).Length;
    }

    [Benchmark]
    public int DecodeGlyphContours()
    {
        using OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(_state.Font.Bytes, _state.Glyph.GlyphIndex, _state.Font.Metrics, new RenderBudget(long.MaxValue), CancellationToken.None);
        return decoded.Segments.Length;
    }

    [Benchmark]
    public int TransformAndCopyContours()
    {
        using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(_state.Font, _state.Glyph, 16, _state.Decoded, new RenderBudget(long.MaxValue), CancellationToken.None);
        return contours.Contours.Length;
    }

    [Benchmark]
    public int DecodeTransformAndCopyContours()
    {
        using SvgGlyphContourStorage contours = SvgGlyphOutline.GetContourStorage(_state.Font, _state.Glyph, 16, new RenderBudget(long.MaxValue), CancellationToken.None);
        return contours.Contours.Length;
    }

    [GlobalCleanup]
    public void CleanUp()
    {
        _state.Decoded.Dispose();
        _state.Snapshot.Dispose();
    }

    private sealed class TextBenchmarkState(byte[] svg, SvgFontPackCatalog catalog, SingleSourceProvider provider, SvgFontSnapshot snapshot, SvgRenderOptions options, SvgPreparedFont font, SvgPositionedGlyph glyph, OpenTypeGlyphContourStorage decoded)
    {
        public byte[] Svg = svg;
        public SvgFontPackCatalog Catalog = catalog;
        public SingleSourceProvider Provider = provider;
        public SvgFontSnapshot Snapshot = snapshot;
        public SvgRenderOptions Options = options;
        public SvgPreparedFont Font = font;
        public SvgPositionedGlyph Glyph = glyph;
        public OpenTypeGlyphContourStorage Decoded = decoded;

        public static TextBenchmarkState Create()
        {
            byte[] font = Convert.FromBase64String(TriangleFontBase64);
            string hash = Convert.ToHexString(SHA256.HashData(font));
            string manifest = "{ \"packId\": \"bench-pack\", \"files\": [{ \"relativePath\": \"files/triangle.ttf\", \"byteSize\": " + font.Length + ", \"sha256\": \"" + hash + "\" }], \"families\": [{ \"requestedFamily\": \"Example\", \"resolvedFamily\": \"Example\", \"relativeFontFile\": \"files/triangle.ttf\" }] }";
            SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(manifest), "bench-pack");
            SingleSourceProvider provider = new(new SvgMemoryFontProgramSource("bench-triangle", font));
            StringBuilder svg = new("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64' font-family='Example' font-size='16' fill='red'><style>.label { white-space: pre; }</style><g style='letter-spacing: 1'>");
            for (int index = 0; index < 8; index++)
            {
                svg.Append("<text style='white-space: normal' x='1' y='2'>A<tspan xml:space='preserve'>A</tspan><tspan class='label'>A</tspan></text>");
            }

            svg.Append("</g></svg>");
            byte[] svgBytes = System.Text.Encoding.UTF8.GetBytes(svg.ToString());
            SvgFontSnapshot snapshot = SvgFontSnapshot.CreateAsync(svgBytes, catalog, provider, CancellationToken.None).GetAwaiter().GetResult();
            SvgRenderOptions options = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);
            SvgPreparedFont face = snapshot.Fonts[0];
            SvgPositionedGlyph glyph = SvgGlyphLayout.Layout(face, "A", 16, 0, 0, CancellationToken.None)[0];
            OpenTypeGlyphContourStorage decoded = OpenTypeFont.GetGlyphContours(face.Bytes, glyph.GlyphIndex, face.Metrics, new RenderBudget(long.MaxValue), CancellationToken.None);
            return new TextBenchmarkState(svgBytes, catalog, provider, snapshot, options, face, glyph, decoded);
        }
    }

    private sealed class SingleSourceProvider(ISvgFontProgramSource source) : ISvgFontProgramSourceProvider
    {
        public ValueTask<ISvgFontProgramSource?> GetSourceAsync(SvgFontPackFile file, CancellationToken cancellationToken) => ValueTask.FromResult<ISvgFontProgramSource?>(source);
    }
}
