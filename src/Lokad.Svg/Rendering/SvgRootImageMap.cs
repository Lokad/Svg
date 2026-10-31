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
            return new(layout, SvgAffineTransform.FromScale(outputScale, outputScale), SvgAffineTransform.Identity, null);
        }
        return new(Map(viewBox, layoutWidth, layoutHeight, new((float)logical.Width, (float)logical.Height)),
            SvgAffineTransform.FromScale(outputScale * (float)(naturalWidth / layoutWidth), outputScale * (float)(naturalHeight / layoutHeight)),
            SvgAffineTransform.FromScale(DestinationScale((float)layoutWidth, (float)naturalWidth),
                DestinationScale((float)layoutHeight, (float)naturalHeight)),
            new(0, 0, naturalWidth * outputScale, naturalHeight * outputScale));

        static float DestinationScale(float layout, float natural)
        {
            float source = layout * (natural / layout);
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
