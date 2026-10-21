using BenchmarkDotNet.Attributes;
using System.Text;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures retained scene storage and stylesheet preparation for many small shapes.</summary>
[MemoryDiagnoser]
public class SvgSceneBenchmarks
{
    private byte[] _rectangles = [];
    private byte[] _inlineRectangles = [];
    private byte[] _inlineUses = [];

    [GlobalSetup]
    public void SetUp()
    {
        StringBuilder svg = new("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><style>rect{fill:red}</style>");
        StringBuilder uses = new("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><defs><rect id='r' width='6' height='6'/></defs>");
        for (int index = 0; index < 256; index++)
        {
            svg.Append("<rect x='").Append(index % 16 * 8).Append("' y='").Append(index / 16 * 8)
                .Append("' width='6' height='6'/>");
            uses.Append("<use href='#r' style='fill:#0755a0;stroke:none;opacity:.75' x='").Append(index % 16 * 8)
                .Append("' y='").Append(index / 16 * 8).Append("'/>");
        }
        _rectangles = System.Text.Encoding.UTF8.GetBytes(svg.Append("</svg>").ToString());
        _inlineRectangles = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(_rectangles)
            .Replace("<style>rect{fill:red}</style>", string.Empty, StringComparison.Ordinal)
            .Replace("<rect ", "<rect style='fill:red' ", StringComparison.Ordinal));
        _inlineUses = System.Text.Encoding.UTF8.GetBytes(uses.Append("</svg>").ToString());
    }

    [Benchmark]
    public int ConvertStyledRectangles() => SvgConverter.ConvertToPng(_rectangles, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertInlineStyledRectangles() => SvgConverter.ConvertToPng(_inlineRectangles, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertInlineStyledUses() => SvgConverter.ConvertToPng(_inlineUses, SvgRenderOptions.Default, CancellationToken.None).Length;
}
