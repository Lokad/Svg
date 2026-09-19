namespace Lokad.Svg;

/// <summary>Defines immutable options for one SVG conversion.</summary>
public sealed class SvgRenderOptions
{
    /// <summary>Initializes conversion options.</summary>
    public SvgRenderOptions(SvgViewport? viewport, double outputScale, SvgColor backgroundColor, SvgRenderLimits limits)
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
    }

    /// <summary>Gets default options, including transparent background and scale one.</summary>
    public static SvgRenderOptions Default { get; } = new(null, 1, SvgColor.Transparent, SvgRenderLimits.Default);

    public SvgViewport? Viewport { get; }
    public double OutputScale { get; }
    public SvgColor BackgroundColor { get; }
    public SvgRenderLimits Limits { get; }
}
