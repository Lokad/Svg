namespace Lokad.Svg.Fonts;

/// <summary>Identifies one normalized style/position fragment for ordered text paint without duplicating its glyph paint or font; glyph storage owns the bounded array.</summary>
internal readonly record struct SvgTextPaintFragment(int Start, int End, double Width)
{
    /// <summary>Retains the scaled-font source baseline before paint-origin truncation; transient decoration fragments may omit it.</summary>
    internal Rendering.SvgDevicePoint? SourceOrigin { get; init; }
    /// <summary>Retains the scaled-font inline extent, including trailing letter spacing.</summary>
    internal float? SourceWidth { get; init; }
    /// <summary>Retains a normalized UTF-16 source item, which may own zero glyphs after cross-node shaping.</summary>
    internal int SourceStart { get; init; }
    internal int SourceEnd { get; init; }
}
