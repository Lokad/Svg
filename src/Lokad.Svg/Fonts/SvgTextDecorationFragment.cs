namespace Lokad.Svg.Fonts;

/// <summary>Identifies one normalized style/position fragment without duplicating its glyph paint or font; the glyph storage owns the bounded array.</summary>
internal readonly record struct SvgTextDecorationFragment(int Start, int End, double Width);
