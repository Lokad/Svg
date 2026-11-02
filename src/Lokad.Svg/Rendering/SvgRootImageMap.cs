using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Separates root layout, recorded-image projection and destination placement.</summary>
/// <remarks>Pure values, with no ownership or cache. Logical sizing and text layout remain
/// with the document. The image layout uses CSS subpixel extents; projection normalizes
/// the natural image size and uniform output density before recorded scene narrowing.
/// The outer destination retains source-rectangle float rounding. The image boundary
/// is independent of bitmap allocation.</remarks>
internal readonly record struct SvgRootImageMap(SvgAffineTransform Layout, SvgAffineTransform Projection,
    SvgAffineTransform Destination, SvgClipBox? Boundary)
{
    internal SvgDrawingMap Drawing => new(Projection.Compose(Layout), Boundary is null ? null : Destination);
    /// <summary>Maps CSS box coordinates without composing or canceling the SVG viewBox.</summary>
    internal SvgDrawingMap BoxDrawing => new(SvgAffineTransform.FromScale(OutputScale, OutputScale), Boundary is null ? null : Destination);
    private double OutputScale { get; init; }
    internal SvgClipBox? ContentViewport { get; init; }
    /// <summary>Retains the snapped CSS background/border paint rectangle, before SVG content projection.</summary>
    internal SvgClipBox? BoxPaintBounds { get; init; }
    /// <summary>Retains the snapped CSS padding box, inside border paint and outside SVG content clipping.</summary>
    internal SvgClipBox? BorderInnerBounds { get; init; }

    internal static SvgRootImageMap Resolve(SvgViewBox? viewBox, SvgViewport logical, double outputScale, SvgRootBoxLayout box)
    {
        SvgRootImageMap image = Resolve(viewBox, logical, outputScale);
        if (!box.HasInsets) return image;
        double width = Math.Floor((float)logical.Width * 64d) / 64;
        double height = Math.Floor((float)logical.Height * 64d) / 64;
        if (width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height))
            (width, height) = (logical.Width, logical.Height);
        double containingWidth = Math.Round(logical.Width, MidpointRounding.AwayFromZero);
        SvgClipBox content = new(box.Border.Left.Width + SvgRootBoxLayout.Resolve(box.Left, containingWidth),
            box.Border.Top.Width + SvgRootBoxLayout.Resolve(box.Top, containingWidth), width, height);
        double boxWidth = width + content.X + box.Border.Right.Width + SvgRootBoxLayout.Resolve(box.Right, containingWidth);
        double boxHeight = height + content.Y + box.Border.Bottom.Width + SvgRootBoxLayout.Resolve(box.Bottom, containingWidth);
        double snappedWidth = Math.Round(boxWidth, MidpointRounding.AwayFromZero), snappedHeight = Math.Round(boxHeight, MidpointRounding.AwayFromZero);
        double left = Math.Round(content.X, MidpointRounding.AwayFromZero), top = Math.Round(content.Y, MidpointRounding.AwayFromZero);
        SvgClipBox clip = new(left, top, Math.Round(content.Right, MidpointRounding.AwayFromZero) - left,
            Math.Round(content.Bottom, MidpointRounding.AwayFromZero) - top);
        // SVGRootPainter adjusts document content through the snapped CSS border
        // box. CSS box paint and its pre-snapped content clip do not use that map.
        return image with
        {
            Layout = SvgAffineTransform.FromTranslation(content.X, content.Y).Compose(image.Layout),
            Projection = SvgAffineTransform.FromScale(outputScale * ((float)Math.Round(boxWidth, MidpointRounding.AwayFromZero) / (float)boxWidth),
                outputScale * ((float)Math.Round(boxHeight, MidpointRounding.AwayFromZero) / (float)boxHeight)),
            ContentViewport = clip,
            BoxPaintBounds = image.BoxPaintBounds is null ? null : new(0, 0,
                snappedWidth, snappedHeight),
            BorderInnerBounds = new(box.Border.Left.Width, box.Border.Top.Width,
                Math.Max(0, snappedWidth - box.Border.Left.Width - box.Border.Right.Width),
                Math.Max(0, snappedHeight - box.Border.Top.Width - box.Border.Bottom.Width))
        };
    }

    internal static SvgRootImageMap Resolve(SvgViewBox? viewBox, SvgViewport logical, double outputScale)
    {
        double naturalWidth = Math.Round(logical.Width, MidpointRounding.AwayFromZero);
        double naturalHeight = Math.Round(logical.Height, MidpointRounding.AwayFromZero);
        double layoutWidth = Math.Floor((float)logical.Width * 64d) / 64;
        double layoutHeight = Math.Floor((float)logical.Height * 64d) / 64;
        // Tiny fitted canvases and finite dimensions outside CSS float layout retain
        // the existing numeric admission and empty-viewport behavior.
        if (naturalWidth <= 0 || naturalHeight <= 0 || layoutWidth <= 0 || layoutHeight <= 0
            || !double.IsFinite(layoutWidth) || !double.IsFinite(layoutHeight))
        {
            SvgAffineTransform layout = Map(viewBox, logical.Width, logical.Height, logical);
            return new(layout, SvgAffineTransform.FromScale(outputScale, outputScale), SvgAffineTransform.Identity, null) { OutputScale = outputScale };
        }
        // ImagePainter remaps a clipped source rectangle before SVGImage applies
        // its residual scale. Even identical snapped rectangles retain the two
        // float operations, on both axes, when the viewport truncates the image.
        bool mapVisibleSource = layoutWidth > naturalWidth || layoutHeight > naturalHeight;
        return new(Map(viewBox, layoutWidth, layoutHeight, new((float)logical.Width, (float)logical.Height)),
            SvgAffineTransform.FromScale(outputScale * (float)(naturalWidth / layoutWidth), outputScale * (float)(naturalHeight / layoutHeight)),
            SvgAffineTransform.FromScale(DestinationScale((float)layoutWidth, (float)naturalWidth, mapVisibleSource),
                DestinationScale((float)layoutHeight, (float)naturalHeight, mapVisibleSource)),
            new(0, 0, naturalWidth * outputScale, naturalHeight * outputScale))
            { OutputScale = outputScale, BoxPaintBounds = new(0, 0, naturalWidth, naturalHeight) };

        static float DestinationScale(float layout, float natural, bool mapVisibleSource)
        {
            float source = mapVisibleSource ? natural * (layout / natural) : layout;
            source *= natural / layout;
            return natural / source;
        }

        static SvgAffineTransform Map(SvgViewBox? viewBox, double width, double height, SvgViewport logical)
        {
            if (viewBox is not SvgViewBox box)
                return SvgAffineTransform.FromScale(width / logical.Width, height / logical.Height);
            (double x, double y, double tx, double ty) = SvgGeometrySyntax.MapViewBox(box, width, height);
            return SvgAffineTransform.FromScaleOffset(x, y, tx, ty);
        }
    }
}
