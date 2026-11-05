using BenchmarkDotNet.Attributes;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Text;

namespace Lokad.Svg.Benchmarks;

/// <summary>Separates complete conversion from rasterization of already prepared scenes.</summary>
[MemoryDiagnoser]
public class SvgSceneBenchmarks
{
    private byte[] _rectangles = [];
    private byte[] _inlineRectangles = [];
    private byte[] _inlineUses = [];
    private SvgRoot? _preparedScene;

    [GlobalSetup(Targets = [nameof(ConvertStyledRectangles), nameof(ConvertInlineStyledRectangles), nameof(ConvertInlineStyledUses)])]
    public void SetUp()
    {
        CleanUp();
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

    [GlobalSetup(Target = nameof(RasterizeStyledRectangles))]
    public void SetUpStyledRectangleRaster()
    {
        SetUp();
        _preparedScene = SvgRootParser.Parse(_rectangles, SvgRenderLimits.Default, CancellationToken.None);
    }

    [GlobalSetup(Target = nameof(RasterizeInlineStyledRectangles))]
    public void SetUpInlineRectangleRaster()
    {
        SetUp();
        _preparedScene = SvgRootParser.Parse(_inlineRectangles, SvgRenderLimits.Default, CancellationToken.None);
    }

    [GlobalSetup(Target = nameof(RasterizeInlineStyledUses))]
    public void SetUpInlineUseRaster()
    {
        SetUp();
        _preparedScene = SvgRootParser.Parse(_inlineUses, SvgRenderLimits.Default, CancellationToken.None);
    }

    [GlobalCleanup(Targets = [nameof(RasterizeStyledRectangles), nameof(RasterizeInlineStyledRectangles), nameof(RasterizeInlineStyledUses)])]
    public void CleanUp()
    {
        _preparedScene?.Dispose();
        _preparedScene = null;
    }

    [Benchmark]
    public int ConvertStyledRectangles() => SvgConverter.ConvertToPng(_rectangles, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertInlineStyledRectangles() => SvgConverter.ConvertToPng(_inlineRectangles, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int ConvertInlineStyledUses() => SvgConverter.ConvertToPng(_inlineUses, SvgRenderOptions.Default, CancellationToken.None).Length;

    [Benchmark]
    public int RasterizeStyledRectangles() => RasterizeScene(_preparedScene);

    [Benchmark]
    public int RasterizeInlineStyledRectangles() => RasterizeScene(_preparedScene);

    [Benchmark]
    public int RasterizeInlineStyledUses() => RasterizeScene(_preparedScene);

    private static int RasterizeScene(SvgRoot? root)
    {
        if (root is not {} scene) throw new InvalidOperationException("Run scene benchmark setup before rasterization.");
        using SvgConverter.RasterizedSurface surface = SvgConverter.Rasterize(scene, SvgRenderOptions.Default, new RenderBudget(), CancellationToken.None);
        return surface.Pixels.Length;
    }
}
