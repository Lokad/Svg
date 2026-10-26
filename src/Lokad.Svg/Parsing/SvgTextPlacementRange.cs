using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Borrows one normalized UTF-16 placement interval after shared shaping; a following direct range may retain an endpoint transfer origin.</summary>
/// <remarks>Values own no fonts, paths or arrays. The parser owns ranges under the current budget and resolves their path references before layout.</remarks>
internal readonly record struct SvgTextPlacementRange(int Start, int End, SvgTextPathReference? Path, SvgDevicePoint? AfterPathOrigin);
