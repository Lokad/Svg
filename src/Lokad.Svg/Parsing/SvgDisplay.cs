namespace Lokad.Svg.Parsing;

/// <summary>Retains a supported display declaration; source inheritance resolves at its consumer.</summary>
internal enum SvgDisplay : byte
{
    Inline,
    Block,
    None,
    Inherit,
}
