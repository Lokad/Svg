using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Keeps SVG placement and its viewBox canvas boundary until device placement.</summary>
/// <remarks>Immutable values own no storage. Scene arrays account for retained values;
/// callers observe cancellation during matrix preparation. Source is the
/// unrounded scene map; child maps are resolved before native canvas concatenation.</remarks>
internal readonly record struct SvgViewportDrawMap(SvgAffineTransform Parent, SvgAffineTransform ViewBox, SvgAffineTransform Source)
{
    internal SvgAffineTransform Prepare(SvgAffineTransform output, SvgAffineTransform target)
    {
        // Admission belongs to the complete map. Native canvas intermediates
        // can exceed that range before the viewBox restores the admitted scale.
        output.ComposeChecked(target);
        SvgAffineTransform result = output.Compose(Parent).ComposeNative(ViewBox);
        if (target == Source) return result;
        if (!Source.TryInvert(out SvgAffineTransform inverse)) return output.ComposeChecked(target);
        return result.ComposeNative(inverse.Compose(target));
    }
}
