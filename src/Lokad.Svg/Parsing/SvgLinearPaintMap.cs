using Lokad.Svg.Rendering;
using System.Runtime.CompilerServices;

namespace Lokad.Svg.Parsing;

/// <summary>Retains source gradient endpoints and separate local and referencing transforms.</summary>
internal readonly record struct SvgLinearPaintMapData(double X1, double Y1, double X2, double Y2,
    SvgGradientTransform Local, SvgAffineTransform Referencing, bool IsMapped);

/// <summary>Shares immutable source paint coordinates owned by the enclosing scene.</summary>
/// <remarks>The scene reserves storage before construction and releases it on failure, cancellation
/// or disposal. Native float concatenation keeps local and reference matrices separate; the cached
/// double inverse supplies the finite fallback. Instances are immutable and safe for concurrent draws.</remarks>
internal sealed class SvgLinearPaintMap
{
    internal static long RetainedStorageBytes => 64L + Unsafe.SizeOf<SvgLinearPaintMapData>() + Unsafe.SizeOf<SvgGradientTransform>();

    internal SvgLinearPaintMap(SvgLinearPaintMapData data)
    {
        Data = data;
        SvgAffineTransform map = data.Referencing;
        if (!SvgGradientTransform.Compose(new(map.A, map.B, map.C, map.D, map.E, map.F), data.Local).TryInvert(out SvgGradientTransform inverse) && data.IsMapped)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Singular linear paint referencing maps are not implemented.");
        _inverse = inverse;
    }

    private readonly SvgGradientTransform _inverse;
    internal SvgLinearPaintMapData Data { get; }

    internal double Position(double x, double y)
    {
        (double localX, double localY) = _inverse.Apply(x, y);
        double dx = Data.X2 - Data.X1, dy = Data.Y2 - Data.Y1;
        return ((localX - Data.X1) * dx + (localY - Data.Y1) * dy) / (dx * dx + dy * dy);
    }
}
