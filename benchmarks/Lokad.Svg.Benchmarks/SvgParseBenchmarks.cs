using BenchmarkDotNet.Attributes;
using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;

namespace Lokad.Svg.Benchmarks;

/// <summary>Measures style-heavy admission paths without including input setup or font acquisition.</summary>
[MemoryDiagnoser]
public class SvgParseBenchmarks
{
    private byte[] _attributes = [];
    private byte[] _styledText = [];
    private byte[] _definitionUses = [];
    private byte[] _styledShapes = [];

    [GlobalSetup]
    public void SetUp()
    {
        System.Text.StringBuilder soup = new("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'>");
        for (int index = 0; index < 64; index++)
        {
            int cell = index % 8;
            int row = index / 8;
            soup.Append("<rect id='cell").Append(index).Append("' class='cell' x='").Append(cell * 8).Append("' y='").Append(row * 8).Append("' width='8' height='8' rx='0' ry='0' fill='steelblue' fill-opacity='0.5' stroke='none' display='inline' visibility='visible' style='stroke: none'/>");
        }

        soup.Append("</svg>");
        _attributes = System.Text.Encoding.UTF8.GetBytes(soup.ToString());
        System.Text.StringBuilder definitions = new("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><defs>");
        for (int index = 0; index < 64; index++)
            definitions.Append("<rect id='tile").Append(index).Append("' width='8' height='8' class='tile' style='fill: steelblue; fill-opacity: .5; stroke: none'/> ");
        definitions.Append("</defs>");
        for (int index = 0; index < 64; index++)
            definitions.Append("<use href='#tile").Append(index).Append("' x='").Append(index % 8 * 8).Append("' y='").Append(index / 8 * 8).Append("'/>");
        definitions.Append("</svg>");
        _definitionUses = System.Text.Encoding.UTF8.GetBytes(definitions.ToString());
        System.Text.StringBuilder styled = new("<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'><style>");
        for (int index = 0; index < 32; index++)
            styled.Append(".shade").Append(index).Append(" { fill: steelblue; } g .shade").Append(index).Append(" { fill-opacity: .5; }");
        styled.Append("</style><g>");
        for (int index = 0; index < 64; index++)
            styled.Append("<rect id='cell").Append(index).Append("' class='cell shade").Append(index % 32).Append("' x='").Append(index % 8 * 8).Append("' y='").Append(index / 8 * 8).Append("' width='8' height='8'/>");
        styled.Append("</g></svg>");
        _styledShapes = System.Text.Encoding.UTF8.GetBytes(styled.ToString());
        System.Text.StringBuilder text = new("<svg xmlns='http://www.w3.org/2000/svg' font-family='Example' font-size='16' fill='red'><style>.label { white-space: pre; }</style><g font-style='italic' style='letter-spacing: 1'>");
        for (int index = 0; index < 8; index++)
        {
            text.Append("<text font-weight='700' style='white-space: normal' x='1' y='2'>Hello <tspan xml:space='preserve'>World</tspan><tspan class='label'>!</tspan></text>");
        }

        text.Append("</g></svg>");
        _styledText = System.Text.Encoding.UTF8.GetBytes(text.ToString());
    }

    [Benchmark]
    public int ConvertAttributeHeavyShapes() => SvgConverter.ConvertToPng(_attributes, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ParseAttributeHeavyShapes()
    {
        using SvgRoot root = SvgRootParser.Parse(_attributes, SvgRenderLimits.Default, CancellationToken.None);
        return root.Geometries.Length;
    }

    [Benchmark]
    public int ParseDefinitionUses()
    {
        using SvgRoot root = SvgRootParser.Parse(_definitionUses, SvgRenderLimits.Default, CancellationToken.None);
        return root.Geometries.Length;
    }

    [Benchmark]
    public int DiscoverStyledTextRequests() => SvgFontRequestDiscovery.Discover(_styledText, SvgRenderLimits.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ParseStyledShapes()
    {
        using SvgRoot root = SvgRootParser.Parse(_styledShapes, SvgRenderLimits.Default, CancellationToken.None);
        return root.Geometries.Length;
    }

    [Benchmark]
    public int ConvertStyledShapes() => SvgConverter.ConvertToPng(_styledShapes, SvgRenderOptions.Default, CancellationToken.None).Length;
}
