namespace Lokad.Svg.Rendering;

/// <summary>Identifies a renderer-owned allocation family for budget diagnostics.</summary>
internal enum SvgMemoryCategory
{
    Parser,
    Scene,
    Geometry,
    Surface,
    Resource,
    Png,
    Output,
    Temporary
}
