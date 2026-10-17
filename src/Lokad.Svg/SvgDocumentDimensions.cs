namespace Lokad.Svg;

/// <summary>Describes root sizing without retaining SVG bytes, fonts or a rendered scene.</summary>
public sealed class SvgDocumentDimensions
{
    internal SvgDocumentDimensions(SvgDocumentLength? width, SvgDocumentLength? height, SvgDocumentViewBox? viewBox,
        SvgViewport logicalViewport, SvgDimensionSource widthSource, SvgDimensionSource heightSource,
        bool widthUsesFallbackBase, bool heightUsesFallbackBase)
    {
        Width = width;
        Height = height;
        ViewBox = viewBox;
        LogicalViewport = logicalViewport;
        WidthSource = widthSource;
        HeightSource = heightSource;
        WidthUsesFallbackBase = widthUsesFallbackBase;
        HeightUsesFallbackBase = heightUsesFallbackBase;
    }

    /// <summary>Gets the winning root width after CSS cascade; absolute units are expressed in CSS pixels.</summary>
    public SvgDocumentLength? Width { get; }
    /// <summary>Gets the winning root height after CSS cascade; absolute units are expressed in CSS pixels.</summary>
    public SvgDocumentLength? Height { get; }
    /// <summary>Gets the parsed viewBox and aspect mapping at the renderer's numeric precision, or null when absent.</summary>
    public SvgDocumentViewBox? ViewBox { get; }
    /// <summary>Gets the resolved logical CSS-pixel viewport, before output scaling or raster limits.</summary>
    public SvgViewport LogicalViewport { get; }
    /// <summary>Gets how the logical width was chosen.</summary>
    public SvgDimensionSource WidthSource { get; }
    /// <summary>Gets how the logical height was chosen.</summary>
    public SvgDimensionSource HeightSource { get; }
    /// <summary>Gets whether width resolution used a fallback percentage base, directly or through aspect derivation.</summary>
    public bool WidthUsesFallbackBase { get; }
    /// <summary>Gets whether height resolution used a fallback percentage base, directly or through aspect derivation.</summary>
    public bool HeightUsesFallbackBase { get; }
}

/// <summary>Represents an effective root length in CSS pixels or percent; absence is represented by a nullable value.</summary>
public readonly record struct SvgDocumentLength
{
    internal SvgDocumentLength(double value, bool isPercentage) { Value = value; IsPercentage = isPercentage; }
    public double Value { get; }
    public bool IsPercentage { get; }
}

/// <summary>Identifies the source of one resolved logical viewport dimension.</summary>
public enum SvgDimensionSource
{
    /// <summary>A root dimension declaration supplies the value.</summary>
    Declaration,
    /// <summary>The caller's logical viewport overrides a missing or absolute declaration.</summary>
    Viewport,
    /// <summary>The viewBox ratio derives this dimension from the other axis.</summary>
    AspectRatio,
    /// <summary>The missing dimension uses the 300-wide or 150-high default.</summary>
    Fallback
}

/// <summary>Describes a viewBox and its preserveAspectRatio mapping.</summary>
public readonly record struct SvgDocumentViewBox
{
    internal SvgDocumentViewBox(double x, double y, double width, double height,
        SvgAspectRatioAlignment horizontalAlignment, SvgAspectRatioAlignment verticalAlignment, SvgAspectRatioFit fit)
    {
        X = x; Y = y; Width = width; Height = height;
        HorizontalAlignment = horizontalAlignment; VerticalAlignment = verticalAlignment; Fit = fit;
    }

    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public SvgAspectRatioAlignment HorizontalAlignment { get; }
    public SvgAspectRatioAlignment VerticalAlignment { get; }
    public SvgAspectRatioFit Fit { get; }
}

/// <summary>Identifies viewBox alignment along one logical viewport axis.</summary>
public enum SvgAspectRatioAlignment { Min, Mid, Max, None }
/// <summary>Identifies the viewBox meet/slice/none fitting rule.</summary>
public enum SvgAspectRatioFit { Meet, Slice, None }
