using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Shares root sizing between metadata inspection, output preparation and rendering.</summary>
internal static class SvgDocumentSizing
{
    /// <summary>Resolves one logical viewport axis before aspect derivation; a supplied side overrides absent or absolute declarations.</summary>
    internal static double ResolveViewportSide(SvgRootLength? length, double? suppliedSide, double defaultSide)
        => suppliedSide is not null && length?.IsPercentage != true
            ? suppliedSide.Value : length?.Resolve(suppliedSide, defaultSide) ?? defaultSide;

    internal static SvgDocumentDimensions Resolve(SvgViewport? viewport, SvgRootLength? width, SvgRootLength? height, SvgViewBox? viewBox)
    {
        double logicalWidth, logicalHeight;
        SvgDimensionSource widthSource, heightSource;
        bool widthFallback = width?.IsPercentage == true && viewport is null;
        bool heightFallback = height?.IsPercentage == true && viewport is null;
        if (viewport is SvgViewport supplied)
        {
            logicalWidth = ResolveViewportSide(width, supplied.Width, 300);
            logicalHeight = ResolveViewportSide(height, supplied.Height, 150);
            widthSource = width?.IsPercentage == true ? SvgDimensionSource.Declaration : SvgDimensionSource.Viewport;
            heightSource = height?.IsPercentage == true ? SvgDimensionSource.Declaration : SvgDimensionSource.Viewport;
        }
        else
        {
            logicalWidth = ResolveViewportSide(width, null, 300);
            logicalHeight = ResolveViewportSide(height, null, 150);
            widthSource = width is null ? SvgDimensionSource.Fallback : SvgDimensionSource.Declaration;
            heightSource = height is null ? SvgDimensionSource.Fallback : SvgDimensionSource.Declaration;
            if (viewBox is SvgViewBox box)
            {
                if (width is not null && height is null)
                {
                    logicalHeight = SvgGeometrySyntax.MultiplyDivide(logicalWidth, box.Height, box.Width);
                    heightSource = SvgDimensionSource.AspectRatio;
                    heightFallback = widthFallback;
                }
                else if (height is not null && width is null)
                {
                    logicalWidth = SvgGeometrySyntax.MultiplyDivide(logicalHeight, box.Width, box.Height);
                    widthSource = SvgDimensionSource.AspectRatio;
                    widthFallback = heightFallback;
                }
            }
        }
        if (!double.IsFinite(logicalWidth) || !double.IsFinite(logicalHeight))
            throw new SvgException(SvgErrorCode.LimitExceeded, "Logical viewport dimensions exceed the supported numeric range.");
        if (logicalWidth <= 0 || logicalHeight <= 0)
            throw new SvgException(SvgErrorCode.MalformedInput, "Logical viewport dimensions must be positive.");
        SvgDocumentLength? publicWidth = width is SvgRootLength w ? new(w.Value, w.IsPercentage) : null;
        SvgDocumentLength? publicHeight = height is SvgRootLength h ? new(h.Value, h.IsPercentage) : null;
        SvgDocumentViewBox? publicBox = viewBox is SvgViewBox b
            ? new(b.X, b.Y, b.Width, b.Height, (SvgAspectRatioAlignment)b.HorizontalAlignment,
                (SvgAspectRatioAlignment)b.VerticalAlignment, (SvgAspectRatioFit)b.Fit) : null;
        return new(publicWidth, publicHeight, publicBox, new(logicalWidth, logicalHeight), widthSource, heightSource, widthFallback, heightFallback);
    }

    /// <summary>Reads bounded XML and the ordinary stylesheet cascade without lowering drawing content or accessing fonts.</summary>
    internal static SvgDocumentDimensions Inspect(ReadOnlyMemory<byte> svg, SvgRenderOptions options, RenderBudget budget,
CancellationToken cancellationToken)
    {
        using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, cancellationToken);
        return preparation.InspectDimensions(options);
    }
}
