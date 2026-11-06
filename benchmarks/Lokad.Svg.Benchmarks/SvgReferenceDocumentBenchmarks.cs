using BenchmarkDotNet.Attributes;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures licensed reference and authored documents with input/font acquisition outside timing.</summary>
[MemoryDiagnoser]
public class SvgReferenceDocumentBenchmarks
{
    public enum ReferenceDocument
    {
        Blur, BlendMultiply, CompositeIn, MergeColorSpaces, FilterMaskClip,
        PatternAffine, EmbeddedPng, TextPath, RealItalic,
        IconSheet, Wordmark, Chart
    }

    [ParamsAllValues]
    public ReferenceDocument Document { get; set; }

    private byte[] _svg = [];
    private SvgFontSnapshot? _snapshot;
    private SvgRoot? _scene;
    private SvgRenderOptions _options = SvgRenderOptions.Default;

    [GlobalSetup(Target = nameof(ConvertDocument))]
    public async Task SetUpConversion()
    {
        CleanUp();
        try
        {
            string research = Path.Combine(BenchmarkCorpus.FindRepository(), "tests", "Research");
            string directory = Path.Combine(research,
                Document is ReferenceDocument.IconSheet or ReferenceDocument.Wordmark or ReferenceDocument.Chart
                    ? "benchmark-workloads" : "upstream-static-documents");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
            string id = Document switch
            {
                ReferenceDocument.Blur => "blur",
                ReferenceDocument.BlendMultiply => "blend-multiply",
                ReferenceDocument.CompositeIn => "composite-in",
                ReferenceDocument.MergeColorSpaces => "merge-color-spaces",
                ReferenceDocument.FilterMaskClip => "filter-mask-clip",
                ReferenceDocument.PatternAffine => "pattern-affine",
                ReferenceDocument.EmbeddedPng => "embedded-png",
                ReferenceDocument.TextPath => "text-path",
                ReferenceDocument.RealItalic => "text-real-italic",
                ReferenceDocument.IconSheet => "icon-sheet",
                ReferenceDocument.Wordmark => "wordmark",
                ReferenceDocument.Chart => "chart",
                _ => throw new InvalidOperationException("Unknown benchmark document.")
            };
            JsonElement item = manifest.RootElement.GetProperty("cases").EnumerateArray()
                .Single(value => value.GetProperty("id").GetString() == id);
            _svg = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
            if (Convert.ToHexStringLower(SHA256.HashData(_svg)) != Required(item, "sourceSha256"))
                throw new InvalidDataException("Benchmark document does not match its committed source hash.");
            int width = item.GetProperty("width").GetInt32(), height = item.GetProperty("height").GetInt32();
            if (item.GetProperty("scale").GetDouble() != 1 || Required(item, "background") != "transparent")
                throw new InvalidDataException("Benchmark document requires a different capture recipe.");
            SvgViewport viewport = new(width, height);
            _options = new(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default);
            if (item.GetProperty("fonts").GetArrayLength() != 0)
            {
                SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(File.ReadAllBytes(Path.Combine(research, "upstream-static-documents", "font-pack.json")), "upstream-noto-sans");
                List<SvgFontProgramResolution> resolutions = [];
                foreach (JsonElement font in item.GetProperty("fonts").EnumerateArray())
                {
                    string name = font.GetString() ?? throw new InvalidDataException("Missing benchmark font name.");
                    JsonElement program = manifest.RootElement.GetProperty("fontPrograms").EnumerateArray()
                        .Single(value => Path.GetFileName(Required(value, "file")) == name);
                    string path = Required(program, "file");
                    byte[] bytes = File.ReadAllBytes(Path.Combine(directory, path));
                    SvgFontFaceRequest request = new(Required(program, "family"), program.GetProperty("weight").GetInt32(), program.GetProperty("italic").GetBoolean());
                    resolutions.Add(catalog.Bind(catalog.Resolve(request), new SvgMemoryFontProgramSource("benchmark:" + path, bytes)));
                }
                _snapshot = await SvgFontSnapshot.CreateAsync(resolutions, CancellationToken.None);
                _options = new(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default, _snapshot);
            }
        }
        catch { CleanUp(); throw; }

        static string Required(JsonElement item, string name)
            => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
    }

    [GlobalSetup(Target = nameof(RasterizeDocument))]
    public async Task SetUpRaster()
    {
        await SetUpConversion();
        try { _scene = SvgRootParser.Parse(_svg, SvgRenderLimits.Default, new RenderBudget(), _snapshot, CancellationToken.None); }
        catch { CleanUp(); throw; }
    }

    [Benchmark]
    public int ConvertDocument() => SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None).Length;

    [Benchmark]
    public int RasterizeDocument()
    {
        if (_scene is not {} scene) throw new InvalidOperationException("Run raster benchmark setup before rasterization.");
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, _options, new RenderBudget(), CancellationToken.None);
        return surface.Pixels.Length;
    }

    [GlobalCleanup]
    public void CleanUp()
    {
        _scene?.Dispose();
        _scene = null;
        _options = SvgRenderOptions.Default;
        _snapshot?.Dispose();
        _snapshot = null;
        _svg = [];
    }
}
