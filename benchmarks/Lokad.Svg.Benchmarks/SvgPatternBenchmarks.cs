using BenchmarkDotNet.Attributes;
using System.Text;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures complete pattern conversion with input construction outside timing.</summary>
[MemoryDiagnoser]
public class SvgPatternBenchmarks
{
    private byte[] _stroke = [];
    private byte[] _identical = [];
    private byte[] _different = [];

    [GlobalSetup]
    public void SetUp()
    {
        const string start = "<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><defs><pattern id='p' width='16' height='16' patternUnits='userSpaceOnUse'><rect width='16' height='16' fill='#0755a0'/><circle cx='8' cy='8' r='6' fill='#ed6817' fill-opacity='.7'/></pattern></defs>";
        _stroke = System.Text.Encoding.UTF8.GetBytes(start + "<rect x='8' y='8' width='112' height='112' fill='none' stroke='url(#p)' stroke-width='8'/></svg>");
        StringBuilder identical = new(start);
        StringBuilder different = new(start.Replace("</defs>", "<pattern id='q' width='20' height='12' patternUnits='userSpaceOnUse' patternTransform='scale(1.5)'><rect width='20' height='12' fill='#0755a0'/><circle cx='10' cy='6' r='5' fill='#ed6817' fill-opacity='.7'/></pattern></defs>", StringComparison.Ordinal));
        for (int index = 0; index < 16; index++)
        {
            int x = index % 4 * 32, y = index / 4 * 32;
            identical.Append("<rect x='").Append(x).Append("' y='").Append(y).Append("' width='24' height='24' fill='url(#p)'/>");
            different.Append("<rect x='").Append(x).Append("' y='").Append(y).Append("' width='24' height='24' fill='url(#").Append(index % 2 == 0 ? 'p' : 'q').Append(")'/>");
        }
        identical.Append("</svg>");
        different.Append("</svg>");
        _identical = System.Text.Encoding.UTF8.GetBytes(identical.ToString());
        _different = System.Text.Encoding.UTF8.GetBytes(different.ToString());
    }

    [Benchmark]
    public int ConvertPatternedStroke() => SvgConverter.ConvertToPng(_stroke, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertIdenticalInstances() => SvgConverter.ConvertToPng(_identical, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertDifferentInstances() => SvgConverter.ConvertToPng(_different, SvgRenderOptions.Default, CancellationToken.None).Length;
}
