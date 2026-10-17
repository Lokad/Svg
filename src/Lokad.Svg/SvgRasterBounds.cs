namespace Lokad.Svg;

/// <summary>Opts into uniform raster fitting without changing the logical SVG viewport.</summary>
public sealed class SvgRasterBounds
{
    /// <summary>Sets positive raster bounds. At least one bound must be supplied; null leaves that bound to the render limits.</summary>
    public SvgRasterBounds(int? maximumWidth, int? maximumHeight, long? maximumPixels)
    {
        if (maximumWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        if (maximumHeight <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHeight));
        if (maximumPixels <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPixels));
        if (maximumWidth is null && maximumHeight is null && maximumPixels is null)
            throw new ArgumentException("At least one raster bound must be supplied.");
        MaximumWidth = maximumWidth;
        MaximumHeight = maximumHeight;
        MaximumPixels = maximumPixels;
    }

    public int? MaximumWidth { get; }
    public int? MaximumHeight { get; }
    public long? MaximumPixels { get; }
}

/// <summary>Describes planned integer raster dimensions and their uniform logical-to-device scale.</summary>
public readonly record struct SvgRasterSize
{
    internal SvgRasterSize(int width, int height, double outputScale)
    {
        Width = width; Height = height; OutputScale = outputScale;
    }

    public int Width { get; }
    public int Height { get; }
    public double OutputScale { get; }
    public long PixelCount => (long)Width * Height;
}
