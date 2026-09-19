using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Lokad.Svg.Benchmarks;

internal static class Program
{
    private static void Main(string[] arguments) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(arguments);
}

/// <summary>Measures the current supported conversion pipeline without including input setup.</summary>
[MemoryDiagnoser]
public class SvgConversionBenchmarks
{
    private byte[] _svg = [];
    private byte[] _destination = [];
    private MemoryStream _stream = new();

    [Params(1, 64, 512)]
    public int EdgeLength { get; set; }

    [GlobalSetup]
    public void SetUp()
    {
        _svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='" + EdgeLength + "' height='" + EdgeLength + "'><rect width='" + EdgeLength + "' height='" + EdgeLength + "' fill='red'/></svg>");
        _destination = new byte[checked(EdgeLength * EdgeLength * 4 + 262_144)];
        _stream = new MemoryStream(_destination.Length);
    }

    [IterationSetup(Target = nameof(ConvertToStream))]
    public void ResetStream()
    {
        _stream.Position = 0;
        _stream.SetLength(0);
    }

    [IterationSetup(Target = nameof(ConvertToStreamAsync))]
    public void ResetAsyncStream() => ResetStream();

    [Benchmark]
    public int ConvertToOwnedMemory() => SvgConverter.ConvertToPng(_svg, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertToCallerMemory() => SvgConverter.ConvertToPng(_svg, _destination, SvgRenderOptions.Default, CancellationToken.None);

    [Benchmark]
    public int ConvertToStream() => SvgConverter.ConvertToPng(_svg, _stream, SvgRenderOptions.Default, CancellationToken.None);

    [Benchmark]
    public Task<int> ConvertToStreamAsync() => SvgConverter.ConvertToPngAsync(_svg, _stream, SvgRenderOptions.Default, CancellationToken.None);
}
