namespace Lokad.Svg;

/// <summary>Represents an explicit logical CSS-pixel viewport.</summary>
public readonly record struct SvgViewport
{
    /// <summary>Initializes an explicit logical CSS-pixel viewport.</summary>
    public SvgViewport(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (!double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Height = height;
    }

    /// <summary>Gets the viewport width in CSS pixels.</summary>
    public double Width { get; }

    /// <summary>Gets the viewport height in CSS pixels.</summary>
    public double Height { get; }
}
