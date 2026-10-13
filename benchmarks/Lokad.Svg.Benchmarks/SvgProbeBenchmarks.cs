using BenchmarkDotNet.Attributes;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures real unarmed probe entries with empty or unrelated populated execution contexts.</summary>
[MemoryDiagnoser]
public class SvgProbeBenchmarks
{
    private const int Entries = 10_000;
    private static readonly AsyncLocal<string?> Ambient = new();

    [Params(false, true)]
    public bool PopulatedContext { get; set; }

    [GlobalSetup]
    public void SetUp() => Ambient.Value = PopulatedContext ? "benchmark-host-context" : null;

    [GlobalCleanup]
    public void CleanUp() => Ambient.Value = null;

    [Benchmark(OperationsPerInvoke = Entries)]
    public void GlyphCoverageEntries()
    {
        for (int index = 0; index < Entries; index++) GlyphCoverageProbe.Started();
    }

    [Benchmark(OperationsPerInvoke = Entries)]
    public void PolygonFanEntries()
    {
        for (int index = 0; index < Entries; index++) SvgPolygonCoverageProbe.FanEvaluated();
    }

    [Benchmark(OperationsPerInvoke = Entries)]
    public void GlyphOutlineEntries()
    {
        for (int index = 0; index < Entries; index++) GlyphOutlineProbe.SegmentTransformed();
    }
}
