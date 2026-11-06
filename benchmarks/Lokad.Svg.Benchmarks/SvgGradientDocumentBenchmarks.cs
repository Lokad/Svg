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
        _svg = File.ReadAllBytes(Path.Combine(BenchmarkCorpus.FindRepository(), "tests", "Fixtures", "candidates", "inv-140.svg"));
    }

    [Benchmark]
    public int ConvertRetainedGradientDocument() => SvgConverter.ConvertToPng(_svg, _options, CancellationToken.None).Length;
}
