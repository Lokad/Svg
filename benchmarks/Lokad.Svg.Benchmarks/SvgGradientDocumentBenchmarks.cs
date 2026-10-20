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
