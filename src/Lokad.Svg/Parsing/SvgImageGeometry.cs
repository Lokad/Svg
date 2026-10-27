using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains the requested sampling rectangle in immutable decoded-pixel coordinates.</summary>
/// <remarks>Placement admits finite bounds within the decoded raster. Destination clipping and
/// transforms do not change this value. It owns no storage, budget or cancellation state.</remarks>
internal readonly record struct SvgImageSourceRect(float Left, float Top, float Right, float Bottom)
{
    internal bool IsFinite => float.IsFinite(Left) && float.IsFinite(Top) && float.IsFinite(Right) && float.IsFinite(Bottom);
    internal bool IsEmpty => Right <= Left || Bottom <= Top;
    internal SvgAnalyticClipBounds PixelBounds => new((int)MathF.Floor(Left), (int)MathF.Floor(Top),
        (int)MathF.Ceiling(Right), (int)MathF.Ceiling(Bottom));
}

/// <summary>Owns decoded image pixels and retains their independent placement and paint maps.</summary>
/// <remarks>The scene is the sole owner. Raster operations borrow immutable pixels;
/// a clipped successor transfers ownership during single-threaded preparation.</remarks>
internal sealed class SvgImageGeometry : ISvgGeometryContent
{
    private DecodedPngRaster? _raster;

    internal SvgImageGeometry(DecodedPngRaster raster, int width, int height, SvgRectangle box, SvgImageSourceRect sourceRect,
        SvgShaderMatrix referenceMap, SvgShaderMatrix localMap, byte opacity)
        : this(raster, null, width, height, box, sourceRect, referenceMap, localMap, opacity) { }

    /// <summary>Retains a uniform decoded source before independent bitmap paint opacity, without a pixel array.</summary>
    internal SvgImageGeometry(SvgColor color, int width, int height, SvgRectangle box, SvgImageSourceRect sourceRect,
        SvgShaderMatrix referenceMap, SvgShaderMatrix localMap, byte opacity)
        : this(null, new((byte)((color.Red * color.Alpha + 127) / 255),
            (byte)((color.Green * color.Alpha + 127) / 255), (byte)((color.Blue * color.Alpha + 127) / 255), color.Alpha),
            width, height, box, sourceRect, referenceMap, localMap, opacity) { }

    private SvgImageGeometry(DecodedPngRaster? raster, SvgConstantShaderPaint? uniformSample, int width, int height,
        SvgRectangle box, SvgImageSourceRect sourceRect, SvgShaderMatrix referenceMap, SvgShaderMatrix localMap, byte opacity)
    {
        _raster = raster;
        UniformSample = uniformSample;
        Width = width;
        Height = height;
        Box = box;
        SourceRect = sourceRect;
        ReferenceMap = referenceMap;
        LocalMap = localMap;
        Opacity = opacity;
    }

    internal byte[] Pixels => (_raster ?? throw new ObjectDisposedException(nameof(SvgImageGeometry))).Pixels;
    internal SvgConstantShaderPaint? UniformSample { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal SvgRectangle Box { get; }
    internal SvgImageSourceRect SourceRect { get; }
    internal SvgShaderMatrix ReferenceMap { get; }
    internal SvgShaderMatrix LocalMap { get; }
    internal byte Opacity { get; }

    internal SvgImageGeometry ClipTo(SvgClipBox clip)
    {
        if (_raster is null && UniformSample is null) throw new ObjectDisposedException(nameof(SvgImageGeometry));
        SvgImageGeometry successor = new(_raster, UniformSample, Width, Height, clip.ClipRectangle(Box), SourceRect, ReferenceMap, LocalMap, Opacity);
        _raster = null;
        return successor;
    }

    public void Dispose()
    {
        _raster?.Dispose();
        _raster = null;
    }
}
