using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Tests;

internal static class SvgPreparationTestHelper
{
    internal static SvgAffineTransform GetOutputMap(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget, CancellationToken token)
    {
        using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, new(options.Limits.MaximumWorkUnits), token);
        return preparation.GetOutputMap(options);
    }
}
