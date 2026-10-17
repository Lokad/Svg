namespace Lokad.Svg;

using Lokad.Svg.Fonts;

/// <summary>Defines immutable options for one SVG conversion.</summary>
public sealed class SvgRenderOptions
{
    /// <summary>Initializes conversion options.</summary>
    public SvgRenderOptions(SvgViewport? viewport, double outputScale, SvgColor backgroundColor, SvgRenderLimits limits)
        : this(viewport, outputScale, backgroundColor, limits, null)
    {
    }

    /// <summary>Initializes conversion options with an optional caller-owned prepared-font snapshot.</summary>
    public SvgRenderOptions(SvgViewport? viewport, double outputScale, SvgColor backgroundColor, SvgRenderLimits limits, SvgFontSnapshot? fontSnapshot)
        : this(viewport, outputScale, backgroundColor, limits, fontSnapshot, null)
    {
    }

    /// <summary>Initializes conversion options with optional caller-owned fonts and opt-in raster fitting bounds.</summary>
    public SvgRenderOptions(SvgViewport? viewport, double outputScale, SvgColor backgroundColor, SvgRenderLimits limits, SvgFontSnapshot? fontSnapshot, SvgRasterBounds? rasterBounds)
    {
        if (!double.IsFinite(outputScale) || outputScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputScale));
        }

        ArgumentNullException.ThrowIfNull(limits);
        Viewport = viewport;
        OutputScale = outputScale;
        BackgroundColor = backgroundColor;
        Limits = limits;
        FontSnapshot = fontSnapshot;
        RasterBounds = rasterBounds;
    }

    /// <summary>Gets default options, including transparent background and scale one.</summary>
    public static SvgRenderOptions Default { get; } = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);

    public SvgViewport? Viewport { get; }
    public double OutputScale { get; }
    public SvgColor BackgroundColor { get; }
    public SvgRenderLimits Limits { get; }

    /// <summary>Gets the caller-owned prepared-font snapshot retained by the caller until conversion completes, or <see langword="null"/>.</summary>
    public SvgFontSnapshot? FontSnapshot { get; }

    /// <summary>Gets opt-in raster bounds, or null to retain ordinary output sizing and hard-limit rejection.</summary>
    public SvgRasterBounds? RasterBounds { get; }

    internal SvgRasterSize? ResolvedRasterSize { get; private init; }

    internal SvgRenderOptions WithRasterSize(SvgRasterSize size)
        => new(Viewport, size.OutputScale, BackgroundColor, Limits, FontSnapshot, RasterBounds) { ResolvedRasterSize = size };

    internal SvgRenderOptions WithFontSnapshot(SvgFontSnapshot snapshot)
        => new(Viewport, OutputScale, BackgroundColor, Limits, snapshot, RasterBounds) { ResolvedRasterSize = ResolvedRasterSize };
}
