namespace Lokad.Svg.Fonts;

/// <summary>Identifies one normalized style/position fragment for ordered text paint without duplicating its glyph paint or font; glyph storage owns the bounded array.</summary>
internal readonly record struct SvgTextPaintFragment(int Start, int End, double Width);
