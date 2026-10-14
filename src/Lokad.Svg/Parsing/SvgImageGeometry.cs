using Lokad.Svg.Encoding;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns decoded image pixels and retains their independent placement and paint maps.</summary>
/// <remarks>The scene is the sole owner. Raster operations borrow immutable pixels;
/// a clipped successor transfers ownership during single-threaded preparation.</remarks>
internal sealed class SvgImageGeometry : IDisposable
{
    private DecodedPngRaster? _raster;

    internal SvgImageGeometry(DecodedPngRaster raster, int width, int height, SvgRectangle box,
        SvgShaderMatrix referenceMap, SvgShaderMatrix localMap, byte opacity)
    {
        _raster = raster;
        Width = width;
        Height = height;
        Box = box;
        ReferenceMap = referenceMap;
        LocalMap = localMap;
        Opacity = opacity;
    }

    internal byte[] Pixels => (_raster ?? throw new ObjectDisposedException(nameof(SvgImageGeometry))).Pixels;
    internal int Width { get; }
    internal int Height { get; }
    internal SvgRectangle Box { get; }
    internal SvgShaderMatrix ReferenceMap { get; }
    internal SvgShaderMatrix LocalMap { get; }
    internal byte Opacity { get; }

    internal SvgImageGeometry ClipTo(SvgClipBox clip)
    {
        DecodedPngRaster raster = _raster ?? throw new ObjectDisposedException(nameof(SvgImageGeometry));
        SvgImageGeometry successor = new(raster, Width, Height, clip.ClipRectangle(Box), ReferenceMap, LocalMap, Opacity);
        _raster = null;
        return successor;
    }

    public void Dispose()
    {
        _raster?.Dispose();
        _raster = null;
    }
}
