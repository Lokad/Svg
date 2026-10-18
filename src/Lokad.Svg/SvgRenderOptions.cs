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

    /// <summary>Gets the host policy used by prepared-document acquisition. Null preserves legacy catalog selection; raw conversion uses its supplied snapshot.</summary>
    public SvgFontSelectionPolicy? FontSelectionPolicy { get; private init; }

    /// <summary>Returns options using an immutable policy for prepared-document font acquisition; the caller still owns fonts, I/O and caches.</summary>
    public SvgRenderOptions WithFontSelectionPolicy(SvgFontSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(Viewport, OutputScale, BackgroundColor, Limits, FontSnapshot, RasterBounds)
            { ResolvedRasterSize = ResolvedRasterSize, FontSelectionPolicy = policy };
    }

    internal SvgRasterSize? ResolvedRasterSize { get; private init; }

    internal SvgRenderOptions WithRasterSize(SvgRasterSize size)
        => new(Viewport, size.OutputScale, BackgroundColor, Limits, FontSnapshot, RasterBounds) { ResolvedRasterSize = size, FontSelectionPolicy = FontSelectionPolicy };

    internal SvgRenderOptions WithFontSnapshot(SvgFontSnapshot snapshot)
        => new(Viewport, OutputScale, BackgroundColor, Limits, snapshot, RasterBounds) { ResolvedRasterSize = ResolvedRasterSize, FontSelectionPolicy = FontSelectionPolicy };
}
