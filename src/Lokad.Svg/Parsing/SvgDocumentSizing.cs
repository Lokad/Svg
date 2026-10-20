using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Shares root sizing between metadata inspection, output preparation and rendering.</summary>
internal static class SvgDocumentSizing
{
    internal static SvgDocumentDimensions Resolve(SvgViewport? viewport, SvgRootLength? width, SvgRootLength? height, SvgViewBox? viewBox)
    {
        double logicalWidth, logicalHeight;
        SvgDimensionSource widthSource, heightSource;
        bool widthFallback = width?.IsPercentage == true && viewport is null;
        bool heightFallback = height?.IsPercentage == true && viewport is null;
        if (viewport is SvgViewport supplied)
        {
            logicalWidth = width?.IsPercentage == true ? width.Value.Resolve(supplied.Width, 300) : supplied.Width;
            logicalHeight = height?.IsPercentage == true ? height.Value.Resolve(supplied.Height, 150) : supplied.Height;
            widthSource = width?.IsPercentage == true ? SvgDimensionSource.Declaration : SvgDimensionSource.Viewport;
            heightSource = height?.IsPercentage == true ? SvgDimensionSource.Declaration : SvgDimensionSource.Viewport;
        }
        else
        {
            logicalWidth = width?.Resolve(null, 300) ?? 300;
            logicalHeight = height?.Resolve(null, 150) ?? 150;
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
        ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        using SvgDocumentPreparation preparation = SvgDocumentPreparation.Create(svg, options.Limits, budget, workCounter, cancellationToken);
        return preparation.InspectDimensions(options);
    }
}
