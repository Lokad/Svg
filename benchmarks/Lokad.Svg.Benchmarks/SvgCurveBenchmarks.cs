using BenchmarkDotNet.Attributes;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures the curve-heavy conversion hot path without including input setup.</summary>
[MemoryDiagnoser]
public class SvgCurveBenchmarks
{
    private byte[] _wordmark = [];
    private byte[] _paintClip = [];
    private byte[] _opacity = [];

    [GlobalSetup]
    public void SetUp()
    {
        string glyph = "<path d='M1,0 C4,0 7,1 7,4 C7,7 4,8 1,8 C0,8 0,7 0,4 C0,1 0,0 1,0 Z M3,2 C4,2 5,3 5,4 C5,5 4,6 3,6 C2,6 2,5 2,4 C2,3 2,2 3,2 Z' fill='red' fill-rule='evenodd'/>";
        System.Text.StringBuilder wordmark = new("<svg xmlns='http://www.w3.org/2000/svg' width='204' height='8'>");
        for (int index = 0; index < 20; index++) wordmark.Append("<g transform='translate(").Append(index * 10).Append(",0)'>").Append(glyph).Append("</g>");
        wordmark.Append("</svg>");
        _wordmark = System.Text.Encoding.UTF8.GetBytes(wordmark.ToString());
        _paintClip = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><clipPath id='c'><rect x='2' y='2' width='12' height='12'/></clipPath></defs><polygon points='0,0 16,0 0,16' fill='url(#g)' clip-path='url(#c)'/></svg>");
        _opacity = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'><g opacity='0.5'><rect x='0' y='0' width='12' height='12' fill='red'/><rect x='4' y='4' width='12' height='12' fill='blue'/></g></svg>");
    }

    [Benchmark]
    public int ConvertOutlinedWordmark() => SvgConverter.ConvertToPng(_wordmark, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertGradientClip() => SvgConverter.ConvertToPng(_paintClip, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertIsolatedOpacity() => SvgConverter.ConvertToPng(_opacity, SvgRenderOptions.Default, CancellationToken.None).Length;
}
