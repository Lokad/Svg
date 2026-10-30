using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Separates a root image's layout map, destination projection and image boundary.</summary>
/// <remarks>Pure values, with no ownership or cache. Logical sizing and text layout remain
/// with the document. The image layout uses CSS subpixel extents; projection normalizes
/// the natural image size before applying the caller's uniform output scale.</remarks>
internal readonly record struct SvgRootImageMap(SvgAffineTransform Layout, SvgAffineTransform Projection, SvgClipBox? Boundary)
{
    internal SvgAffineTransform Combined => Projection.Compose(Layout);

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
            return new(layout, SvgAffineTransform.FromScale(outputScale, outputScale), null);
        }
        return new(Map(viewBox, layoutWidth, layoutHeight, new((float)logical.Width, (float)logical.Height)),
            SvgAffineTransform.FromScale(outputScale * naturalWidth / layoutWidth, outputScale * naturalHeight / layoutHeight),
            new(0, 0, naturalWidth * outputScale, naturalHeight * outputScale));

        static SvgAffineTransform Map(SvgViewBox? viewBox, double width, double height, SvgViewport logical)
        {
            if (viewBox is not SvgViewBox box)
                return SvgAffineTransform.FromScale(width / logical.Width, height / logical.Height);
            (double x, double y, double tx, double ty) = SvgGeometrySyntax.MapViewBox(box, width, height);
            return SvgAffineTransform.FromScaleOffset(x, y, tx, ty);
        }
    }
}
