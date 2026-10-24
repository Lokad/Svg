using Lokad.Svg.Parsing;

namespace Lokad.Svg.Rendering;

/// <summary>Borrows one draw's native bitmap origins and source placement for shader sampling.</summary>
/// <remarks>Immutable and allocation-free. Clip metadata belongs to the caller's raster scope;
/// the context must not outlive it. Callers charge pixel work and observe cancellation.</remarks>
internal readonly record struct SvgShaderSamplingContext(SvgCpuRasterGrid Grid, SvgAnalyticClipBounds Layer,
    SvgCpuClipCoverage? LayerClip, SvgPaintBounds? NativeSourceBounds, SvgCpuBitmapOrigin? DitherOrigin,
    SvgAffineTransform SourceInverse, double ScaleX, double ScaleY, double OffsetX, double OffsetY)
{
    internal SvgCpuBitmapOrigin Origin(int x, int y)
    {
        if (NativeSourceBounds is {} native)
            return new(checked((int)((native.Left - 1) * ScaleX + OffsetX)), checked((int)((native.Top - 1) * ScaleY + OffsetY)));
        SvgCpuBitmapOrigin tile = Grid.GetBitmapOrigin(x, y);
        int left = Math.Max(tile.X, Layer.Left), top = Math.Max(tile.Y, Layer.Top);
        if (LayerClip is {} coverage && coverage.TryGetBounds(tile, out SvgAnalyticClipBounds bounds))
        {
            left = Math.Max(left, bounds.Left); top = Math.Max(top, bounds.Top);
        }
        return new(left, top);
    }

    internal SvgDevicePoint SourcePoint(int x, int y)
    {
        double px = (x + .5 - OffsetX) / ScaleX, py = (y + .5 - OffsetY) / ScaleY;
        return SourceInverse == SvgAffineTransform.Identity ? new(px, py) : SourceInverse.Apply(px, py);
    }

    internal int Dither(int x, int y)
    {
        SvgCpuBitmapOrigin origin = DitherOrigin ?? Origin(x, y);
        int localX = x - origin.X, localY = y - origin.Y, mixedY = localY ^ localX;
        return ((mixedY & 1) << 5) | ((localX & 1) << 4)
            | ((mixedY & 2) << 2) | ((localX & 2) << 1)
            | ((mixedY & 4) >> 1) | ((localX & 4) >> 2);
    }
}
