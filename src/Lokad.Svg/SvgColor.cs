namespace Lokad.Svg;

/// <summary>Represents a straight-alpha sRGB color.</summary>
public readonly record struct SvgColor(byte Red, byte Green, byte Blue, byte Alpha)
{
    /// <summary>Gets a fully transparent black color.</summary>
    public static SvgColor Transparent { get; } = new(0, 0, 0, 0);
}
