namespace Lokad.Svg.Parsing;

/// <summary>Retains validated source opacity alongside its existing byte quantization.</summary>
/// <remarks>The byte value belongs to ordinary raster composition; the source value belongs to full-canvas solid-paint analysis and gradient stop sampling. An encoded color alpha byte does not imply a decimal opacity of one half.</remarks>
internal readonly record struct SvgPaintOpacity(byte Alpha, float Value)
{
    internal static SvgPaintOpacity Opaque { get; } = new(255, 1);
}

/// <summary>Retains a solid color and native floating-point alpha before layer opacity.</summary>
internal readonly record struct SvgSolidPaint(SvgColor Color, float Alpha);
