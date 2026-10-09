namespace Lokad.Svg.Parsing;

/// <summary>Retains known viewport extents in user units, independently of shape transforms and output pixels.</summary>
internal readonly record struct SvgViewportLengthBases(double? Width, double? Height)
{
    internal double? NormalizedDiagonal
    {
        get
        {
            if (Width is not {} width || Height is not {} height) return null;
            double x = (float)width, y = (float)height;
            return (float)Math.Sqrt((x * x + y * y) / 2);
        }
    }
}
