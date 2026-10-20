using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.VisualTests;

internal static class SvgPreparationTestHelper
{
    internal static (double Horizontal, double Vertical) GetOutputScales(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, CancellationToken token)
    {
        using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, new(options.Limits.MaximumWorkUnits), token);
        SvgAffineTransform map = preparation.GetOutputMap(options);
        return (map.A, map.D);
    }
}
