using BenchmarkDotNet.Attributes;
using Lokad.Svg.Fonts;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures repeated font-program integrity verification without including input setup.</summary>
[MemoryDiagnoser]
public class FontIntegrityBenchmarks
{
    private byte[] _program = [];

    [Params(4096, 1048576)]
    public int ProgramBytes { get; set; }

    [GlobalSetup]
    public void SetUp()
    {
        _program = new byte[ProgramBytes];
        for (int index = 0; index < _program.Length; index++)
        {
            _program[index] = (byte)(index * 31);
        }
    }

    [Benchmark]
    public int VerifyProgramHash() => SvgFontProgramIntegrity.ComputeSha256(_program, null, CancellationToken.None).Length;
}
