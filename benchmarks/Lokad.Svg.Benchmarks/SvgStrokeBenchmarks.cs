using BenchmarkDotNet.Attributes;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures the straight-stroke outline conversion hot path without including input setup.</summary>
public class SvgStrokeBenchmarks
{
    private byte[] _overlappingBands = [];
    private byte[] _manyContours = [];
    private byte[] _dashedStroke = [];
    private byte[] _miterV = [];

    [GlobalSetup]
    public void SetUp()
    {
        System.Text.StringBuilder bands = new("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='");
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int x = 2 + col * 12;
                int y = 2 + row * 12;
                bands.Append("M").Append(x).Append(",").Append(y).Append(" H").Append(x + 16).Append(" V").Append(y + 16).Append(" H").Append(x).Append(" Z ");
            }
        }
        bands.Append("' fill='none' stroke='blue' stroke-width='2'/></svg>");
        _overlappingBands = System.Text.Encoding.UTF8.GetBytes(bands.ToString());
        _manyContours = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='72' height='72'><path fill='#FF0' stroke='#00F' stroke-width='2' d='m1,1h69v69H1zm45,0v23H23m0,23H46v23'/></svg>");
        _dashedStroke = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><path d='M2,2 H14 V14 H2 Z' fill='none' stroke='#000' stroke-width='2' stroke-dasharray='4 4' stroke-dashoffset='2.5'/></svg>");
        _miterV = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><path d='M8,40 L32,8 L56,40' fill='none' stroke='red' stroke-width='8' stroke-linejoin='miter' stroke-miterlimit='10'/></svg>");
    }

    [Benchmark]
    public int ConvertOverlappingBands() => SvgConverter.ConvertToPng(_overlappingBands, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertManyContours() => SvgConverter.ConvertToPng(_manyContours, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertDashedStroke() => SvgConverter.ConvertToPng(_dashedStroke, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertMiterV() => SvgConverter.ConvertToPng(_miterV, SvgRenderOptions.Default, CancellationToken.None).Length;
}