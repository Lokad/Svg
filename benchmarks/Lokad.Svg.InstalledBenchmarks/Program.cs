using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Characteristics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;
using BenchmarkDotNet.Validators;
using Lokad.Svg.Fonts;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lokad.Svg.Benchmarks;

internal static class Program
{
    private static async Task Main(string[] arguments)
    {
        if (arguments is ["--process-probe", string probeDocument, string conversions, string destination])
        {
            await RunProcessProbeAsync(probeDocument, conversions, destination);
            return;
        }
        if (arguments is ["--verify", string output])
        {
            output = Path.GetFullPath(output);
            if (Directory.Exists(output)) throw new IOException("Use a new verification directory.");
            Directory.CreateDirectory(output);
            List<object> cases = [];
            foreach (string document in InstalledDocumentBenchmarks.Documents)
            {
                InstalledDocumentBenchmarks fixture = new() { Document = document };
                try
                {
                    await fixture.SetUp();
                    ReadOnlyMemory<byte> first = fixture.Render();
                    ReadOnlyMemory<byte> second = fixture.Render();
                    if (!first.Span.SequenceEqual(second.Span)) throw new InvalidDataException("Repeated PNGs differ.");
                    File.WriteAllBytes(Path.Combine(output, document + ".png"), first.Span);
                    cases.Add(new { id = document, sourceSha256 = fixture.SourceSha256,
                        pngSha256 = Convert.ToHexStringLower(SHA256.HashData(first.Span)), bytes = first.Length });
                }
                finally { fixture.CleanUp(); }
            }
            string library = typeof(SvgConverter).Assembly.Location;
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                librarySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(library))),
                cases,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (arguments.Any(value => value.Equals("--runtimes", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Select the host with LokadSvgHostFramework when building this installed benchmark.");
        FileInfo project = new(Path.Combine(BenchmarkCorpus.FindRepository(), "benchmarks",
            "Lokad.Svg.InstalledBenchmarks", "Lokad.Svg.InstalledBenchmarks.csproj"));
        if (!project.Exists) throw new FileNotFoundException("The installed benchmark project is missing.", project.FullName);
        CoreRuntime runtime = AppContext.TargetFrameworkName switch
        {
            ".NETCoreApp,Version=v10.0" => CoreRuntime.Core10_0,
            ".NETCoreApp,Version=v11.0" => CoreRuntime.Core11_0,
            _ => throw new InvalidOperationException("Unqualified benchmark host framework."),
        };
        ManualConfig discovery = ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default.WithRuntime(runtime));
        BenchmarkRunInfo info = BenchmarkConverter.TypeToBenchmarks(typeof(InstalledDocumentBenchmarks), discovery);
        IToolchain original = runtime.GetDefaultToolchain(info.BenchmarksCases[0]);
        if (original.Builder is not CsProjBuilder builder) throw new InvalidOperationException("Expected the managed project builder.");
        InstalledToolchain toolchain = new(original, new InstalledBuilder(builder.Settings, project));
        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance).AddJob(Job.Default.WithToolchain(toolchain).AsMutator());
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(arguments, config);

        static async Task RunProcessProbeAsync(string document, string conversions, string destination)
        {
            if (!int.TryParse(conversions, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
                throw new ArgumentException("Pass a positive conversion count.", nameof(conversions));
            destination = Path.GetFullPath(destination);
            if (Directory.Exists(destination)) throw new IOException("Use a new process-probe directory.");
            Directory.CreateDirectory(destination);
            long start = Stopwatch.GetTimestamp();
            using Process process = Process.GetCurrentProcess();
            List<ProcessSnapshot> snapshots = [Capture("beforeSetup", start, process)];
            InstalledDocumentBenchmarks fixture = new() { Document = document };
            double[] milliseconds = new double[count];
            long[] allocations = new long[count];
            string sourceSha256, pngSha256;
            int bytes;
            double setupMilliseconds;
            try
            {
                long setup = Stopwatch.GetTimestamp();
                await fixture.SetUp();
                setupMilliseconds = Stopwatch.GetElapsedTime(setup).TotalMilliseconds;
                sourceSha256 = fixture.SourceSha256;
                snapshots.Add(Capture("afterSetup", start, process));
                ReadOnlyMemory<byte> first = ReadOnlyMemory<byte>.Empty;
                for (int index = 0; index < count; index++)
                {
                    long allocated = GC.GetTotalAllocatedBytes(true);
                    long conversion = Stopwatch.GetTimestamp();
                    ReadOnlyMemory<byte> png = fixture.Render();
                    milliseconds[index] = Stopwatch.GetElapsedTime(conversion).TotalMilliseconds;
                    allocations[index] = GC.GetTotalAllocatedBytes(true) - allocated;
                    if (index == 0)
                    {
                        first = png;
                        snapshots.Add(Capture("afterFirstConversion", start, process));
                        Console.WriteLine("SVG_PROCESS_FIRST_CONVERSION " + JsonSerializer.Serialize(new
                        {
                            document, probeElapsedMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                            conversionMilliseconds = milliseconds[0],
                        }));
                    }
                    else if (!png.Span.SequenceEqual(first.Span)) throw new InvalidDataException("Repeated PNGs differ.");
                }
                snapshots.Add(Capture("afterConversions", start, process));
                bytes = first.Length;
                pngSha256 = Convert.ToHexStringLower(SHA256.HashData(first.Span));
                File.WriteAllBytes(Path.Combine(destination, document + ".png"), first.Span);
            }
            finally { fixture.CleanUp(); }
            snapshots.Add(Capture("afterFixtureCleanup", start, process));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            snapshots.Add(Capture("afterForcedCollection", start, process));
            using FileStream library = File.OpenRead(typeof(SvgConverter).Assembly.Location);
            File.WriteAllText(Path.Combine(destination, "report.json"), JsonSerializer.Serialize(new
            {
                document, conversions = count, sourceSha256, pngSha256, bytes,
                librarySha256 = Convert.ToHexStringLower(SHA256.HashData(library)),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                setupMilliseconds, conversionMilliseconds = milliseconds, allocatedBytesPerConversion = allocations,
                snapshots,
                scope = "Fresh process probe, no warmup. Setup includes input/font I/O and acquisition; each conversion includes parse/raster/PNG. Exactness comparison, snapshots and output I/O are outside individual conversion timers. First PNG remains retained for repeat checks. Process resident/peak bytes include runtime, fonts, input, output and observer overhead; managed heap estimates and cumulative allocation are different metrics. Explicit cleanup/full-GC snapshots observe the live probe frame and establish neither minimal retained heap nor native release. No warmed throughput, IPC-only or governed-budget claim.",
            }, new JsonSerializerOptions { WriteIndented = true }));

            static ProcessSnapshot Capture(string phase, long start, Process process)
            {
                process.Refresh();
                return new(phase, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    process.WorkingSet64, process.PeakWorkingSet64, GC.GetTotalMemory(false),
                    GC.GetTotalAllocatedBytes(true), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            }
        }
    }

    /// <summary>Substitutes only builder locations; retains the selected runtime, original executor
    /// and validation. It introduces no resource ownership or cancellation behavior.</summary>
    private sealed class InstalledToolchain(IToolchain original, InstalledBuilder builder)
        : Toolchain("Explicit installed project", original.Runtime, builder, original.Executor)
    {
        /// <summary>Forwards every original validation diagnostic without filtering or lifecycle changes.</summary>
        public override IAsyncEnumerable<ValidationError> ValidateAsync(BenchmarkCase benchmarkCase, IResolver resolver)
            => original.ValidateAsync(benchmarkCase, resolver);


    }

    private readonly record struct ProcessSnapshot(string Phase, double ProbeElapsedMilliseconds,
        long ResidentBytes, long PeakResidentBytes, long ManagedHeapEstimateBytes,
        long TotalManagedAllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections);

    /// <summary>Bounds project discovery/output to this fixture's project. Generation, cancellation,
    /// compilation, execution and cleanup retain BenchmarkDotNet's original behavior. It owns no
    /// buffers or handles; immutable path queries are safe concurrently.</summary>
    private sealed class InstalledBuilder(DotNetCliSettings settings, FileInfo project) : CsProjBuilder(settings)
    {
        /// <summary>Returns this project's ignored generated-output path; performs no I/O or accounting.</summary>
        protected override string GetBuildArtifactsDirectoryPath(BuildPartition partition, string programName)
            => Path.Combine(project.DirectoryName ?? throw new InvalidOperationException("Missing project directory."), ".bdn", programName);

        /// <summary>Returns the validated original project without recursive discovery or ownership transfer.</summary>
        protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger) => project;
    }
}

/// <summary>Measures warmed public conversion through one explicitly selected installed package asset.</summary>
/// <remarks>Input I/O, manifest validation, options and supplied-font acquisition are outside timing.
/// Conversion includes parsing, rasterization, PNG encoding and returning bare memory. The caller
/// retains only the returned length; cumulative managed allocation is distinct from live ownership.</remarks>
[MemoryDiagnoser]
public class InstalledDocumentBenchmarks
{
    private byte[] _svg = [];
    private SvgRenderOptions _options = SvgRenderOptions.Default;
    private SvgFontSnapshot? _snapshot;

    [ParamsSource(nameof(Documents))]
    public string Document { get; set; } = string.Empty;

    internal string SourceSha256 { get; private set; } = string.Empty;

    public static IEnumerable<string> Documents
    {
        get
        {
            string research = Path.Combine(BenchmarkCorpus.FindRepository(), "tests", "Research");
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string bank in new[] { "upstream-static-documents", "benchmark-workloads" })
            {
                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(research, bank, "manifest.json")));
                foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
                {
                    string id = item.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing case ID.");
                    if (Path.GetFileName(id) != id || !seen.Add(id)) throw new InvalidDataException("Invalid or duplicate case ID.");
                    yield return id;
                }
            }
        }
    }

    [GlobalSetup]
    public async Task SetUp()
    {
        CleanUp();
        try
        {
            string research = Path.Combine(BenchmarkCorpus.FindRepository(), "tests", "Research");
            string directory = Path.Combine(research,
                Document is "icon-sheet" or "wordmark" or "chart" ? "benchmark-workloads" : "upstream-static-documents");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
            JsonElement root = manifest.RootElement;
            JsonElement item = root.GetProperty("cases").EnumerateArray().Single(value => Required(value, "id") == Document);
            _svg = File.ReadAllBytes(Path.Combine(directory, Required(item, "input")));
            SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(_svg));
            if (SourceSha256 != Required(item, "sourceSha256")) throw new InvalidDataException("Benchmark source changed.");
            if (item.GetProperty("scale").GetDouble() != 1 || Required(item, "background") != "transparent")
                throw new InvalidDataException("This benchmark requires unit scale and a transparent background.");
            SvgViewport viewport = new(item.GetProperty("width").GetInt32(), item.GetProperty("height").GetInt32());
            _options = new(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default);
            if (item.GetProperty("fonts").GetArrayLength() != 0)
            {
                SvgFontPackCatalog catalog = SvgFontPackCatalog.Parse(
                    File.ReadAllBytes(Path.Combine(research, "upstream-static-documents", "font-pack.json")), "upstream-noto-sans");
                List<SvgFontProgramResolution> resolutions = [];
                foreach (JsonElement name in item.GetProperty("fonts").EnumerateArray())
                {
                    string file = name.GetString() ?? throw new InvalidDataException("Missing font name.");
                    JsonElement program = root.GetProperty("fontPrograms").EnumerateArray()
                        .Single(value => Path.GetFileName(Required(value, "file")) == file);
                    byte[] bytes = File.ReadAllBytes(Path.Combine(directory, Required(program, "file")));
                    if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != Required(program, "sha256"))
                        throw new InvalidDataException("Benchmark font changed.");
                    SvgFontFaceRequest request = new(Required(program, "family"), program.GetProperty("weight").GetInt32(),
                        program.GetProperty("italic").GetBoolean());
                    resolutions.Add(catalog.Bind(catalog.Resolve(request), new SvgMemoryFontProgramSource(file, bytes)));
                }
                _snapshot = await SvgFontSnapshot.CreateAsync(resolutions, CancellationToken.None);
                _options = new(viewport, 1, SvgColor.Transparent, SvgRenderLimits.Default, _snapshot);
            }
            Console.WriteLine("SVG_BENCHMARK_INPUT " + JsonSerializer.Serialize(new
            {
                document = Document,
                sourceSha256 = SourceSha256,
                librarySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(SvgConverter).Assembly.Location))),
            }));
        }
        catch { CleanUp(); throw; }

        static string Required(JsonElement item, string name)
            => item.GetProperty(name).GetString() ?? throw new InvalidDataException(name + " is missing.");
    }

    [Benchmark]
    public int ConvertToPng() => Render().Length;

    internal ReadOnlyMemory<byte> Render() => SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None);

    [GlobalCleanup]
    public void CleanUp()
    {
        _snapshot?.Dispose();
        _snapshot = null;
    }
}
