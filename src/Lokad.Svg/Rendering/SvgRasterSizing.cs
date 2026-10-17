namespace Lokad.Svg.Rendering;

/// <summary>Chooses raster dimensions before scene or glyph preparation, retaining one uniform output map.</summary>
internal static class SvgRasterSizing
{
    internal static SvgRasterSize Resolve(SvgViewport viewport, double outputScale, SvgRasterBounds? bounds, SvgRenderLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (!double.IsFinite(outputScale) || outputScale <= 0) throw new ArgumentOutOfRangeException(nameof(outputScale));
        int widthLimit = Math.Min(bounds?.MaximumWidth ?? limits.MaximumDimension, limits.MaximumDimension);
        int heightLimit = Math.Min(bounds?.MaximumHeight ?? limits.MaximumDimension, limits.MaximumDimension);
        long pixelLimit = Math.Min(bounds?.MaximumPixels ?? limits.MaximumPixels, Math.Min(limits.MaximumPixels, int.MaxValue / 4));
        if (TryDimensions(outputScale, out int width, out int height)) return new(width, height, outputScale);
        if (bounds is null)
        {
            double w = Math.Round(viewport.Width * outputScale, MidpointRounding.AwayFromZero);
            double h = Math.Round(viewport.Height * outputScale, MidpointRounding.AwayFromZero);
            if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0)
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Output dimensions must be positive and finite.");
            throw new SvgException(SvgErrorCode.LimitExceeded, "Output dimensions or pixel count exceed the configured limits.");
        }

        double scale = Math.Min(outputScale, Math.Min(widthLimit / viewport.Width, heightLimit / viewport.Height));
        double area = viewport.Width * viewport.Height;
        double pixelScale = double.IsFinite(area) && area > 0 ? Math.Sqrt(pixelLimit / area)
            : Math.Sqrt(pixelLimit) / Math.Sqrt(viewport.Width) / Math.Sqrt(viewport.Height);
        scale = Math.Min(scale, pixelScale);
        if (!double.IsFinite(scale) || scale <= 0)
            throw new SvgException(SvgErrorCode.LimitExceeded, "Bounded raster scaling exceeds the supported numeric range.");
        if (TryDimensions(scale, out width, out height)) return new(width, height, scale);

        // Rounded dimensions are monotone in scale. Find a conservative fit when
        // rounding both axes up would exceed the integer pixel allowance.
        double lower = 0, upper = scale;
        for (int iteration = 0; iteration < 64; iteration++)
        {
            double middle = lower + (upper - lower) * .5;
            if (middle == lower || middle == upper) break;
            if (TryDimensions(middle, out _, out _)) lower = middle;
            else upper = middle;
        }
        if (lower <= 0 || !TryDimensions(lower, out width, out height))
            throw new SvgException(SvgErrorCode.LimitExceeded, "The requested raster bounds cannot accommodate a positive output.");
        return new(width, height, lower);

        bool TryDimensions(double candidateScale, out int candidateWidth, out int candidateHeight)
        {
            double w = Math.Round(viewport.Width * candidateScale, MidpointRounding.AwayFromZero);
            double h = Math.Round(viewport.Height * candidateScale, MidpointRounding.AwayFromZero);
            if (bounds is not null) { w = Math.Max(1, w); h = Math.Max(1, h); }
            candidateWidth = 0; candidateHeight = 0;
            if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0 || w > widthLimit || h > heightLimit) return false;
            candidateWidth = (int)w; candidateHeight = (int)h;
            return (long)candidateWidth * candidateHeight <= pixelLimit;
        }
    }
}
