using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures the unchanged retained rainbow checker, including arbitrary/repeated stops and patterned replay.</summary>
[MemoryDiagnoser]
public class SvgGradientDocumentBenchmarks
{
    private byte[] _svg = [];
    private readonly SvgRenderOptions _options = new(new SvgViewport(512, 512), 1, SvgColor.Transparent, SvgRenderLimits.Default);

    [GlobalSetup]
    public void SetUp()
    {
        _svg = File.ReadAllBytes(Path.Combine(FindRepository(), "tests", "Fixtures", "candidates", "inv-140.svg"));

        static string FindRepository()
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                for (DirectoryInfo? candidate = new(start); candidate is not null; candidate = candidate.Parent)
                    if (File.Exists(Path.Combine(candidate.FullName, "Lokad.Svg.slnx"))) return candidate.FullName;
            throw new DirectoryNotFoundException("Run gradient benchmarks from a Lokad.Svg checkout.");
        }
    }

    [Benchmark]
    public int ConvertRetainedGradientDocument() => SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None).Length;
}

/// <summary>Measures complete owned-memory conversions of every frozen corporate-cohort variant.</summary>
/// <remarks>Input loading and option construction are outside the timed conversion. Known pixel
/// differences remain in the workload. Allocation includes the returned PNG and renderer temporaries.</remarks>
[MemoryDiagnoser]
public class SvgCorporateCohortBenchmarks
{
    private byte[] _svg = [];
    private SvgRenderOptions _options = SvgRenderOptions.Default;

    [ParamsSource(nameof(VariantIds))]
    public string VariantId { get; set; } = string.Empty;

    public IEnumerable<string> VariantIds
    {
        get
        {
            using JsonDocument manifest = ReadManifest(FindRepository());
            foreach (JsonElement document in manifest.RootElement.GetProperty("documents").EnumerateArray())
                foreach (JsonElement variant in document.GetProperty("variants").EnumerateArray())
                    yield return Required(variant, "id");
        }
    }

    [GlobalSetup]
    public void SetUp()
    {
        string repository = FindRepository();
        using JsonDocument manifest = ReadManifest(repository);
        JsonElement variant = manifest.RootElement.GetProperty("documents").EnumerateArray()
            .SelectMany(document => document.GetProperty("variants").EnumerateArray())
            .Single(item => Required(item, "id") == VariantId);
        _svg = File.ReadAllBytes(Path.Combine(repository, Required(variant, "renderInput")));
        if (Convert.ToHexStringLower(SHA256.HashData(_svg)) != Required(variant, "renderInputSha256"))
            throw new InvalidDataException("Frozen renderer input changed: " + VariantId);
        JsonElement viewport = variant.GetProperty("logicalViewport");
        SvgColor background = Required(variant, "background") switch
        {
            "transparent" => SvgColor.Transparent,
            "white" => new(255, 255, 255, 255),
            "black" => new(0, 0, 0, 255),
            _ => throw new InvalidDataException("Unknown frozen background: " + VariantId),
        };
        _options = new(new SvgViewport(viewport.GetProperty("width").GetDouble(), viewport.GetProperty("height").GetDouble()),
            variant.GetProperty("outputScale").GetDouble(), background, SvgRenderLimits.Default);
        ReadOnlySpan<byte> png = SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None).Span;
        if (png.Length < 24 || BinaryPrimitives.ReadUInt32BigEndian(png.Slice(16, 4)) != variant.GetProperty("width").GetInt32()
            || BinaryPrimitives.ReadUInt32BigEndian(png.Slice(20, 4)) != variant.GetProperty("height").GetInt32())
            throw new InvalidDataException("Candidate dimensions differ from the frozen variant: " + VariantId);
    }

    [Benchmark]
    public int ConvertToOwnedMemory() => SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None).Length;

    private static JsonDocument ReadManifest(string repository) => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(repository, "tests", "References", "inventory", "corporate-cohort-v1.json")));

    private static string Required(JsonElement element, string property) => element.GetProperty(property).GetString()
        ?? throw new InvalidDataException("Missing cohort property: " + property);

    private static string FindRepository()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Lokad.Svg.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run cohort benchmarks from a Lokad.Svg checkout.");
    }
}
