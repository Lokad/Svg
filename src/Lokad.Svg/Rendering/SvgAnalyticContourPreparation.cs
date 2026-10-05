namespace Lokad.Svg.Rendering;

using Lokad.Svg.Parsing;

/// <summary>Borrows one immutable mapped fill and caches its clip-independent classifications.</summary>
/// <remarks>The caller keeps the same points, contours, votes and retained verbs alive and unchanged throughout this single-threaded scope. No storage is allocated or owned here. Each classification is charged once by Prepare; each raster context still charges and owns its edge construction and row state. The preparation cannot escape into a returned walker.</remarks>
internal ref struct SvgAnalyticContourPreparation
{
    internal readonly ReadOnlySpan<SvgDevicePoint> Points;
    internal readonly ReadOnlySpan<int> Ends;
    internal readonly ReadOnlySpan<int> NestingSigns;
    internal readonly bool IsEvenOdd;
    internal readonly int[]? Signs;
    internal readonly SvgPolygonMapping Mapping;
    internal readonly SvgQuadSpan[] QuadSpans;
    internal readonly SvgCubicSpan[] CubicSpans;
    internal readonly SvgAnalyticClipBounds PathBounds;
    internal readonly int OutputWidth;
    internal readonly int OutputHeight;
    /// <summary>Complete horizontal raw path bounds for clip-wrapper selection, distinct from clamped storage.</summary>
    internal double LeftWithControls;
    internal double RightWithControls;
    /// <summary>Raw points represented by admitted retained verbs, cached independently of the construction clip.</summary>
    internal long? NativePointCount;
    internal bool BasicChecked;
    internal bool PointValuesEligible;
    internal bool PointsFitCanvas;
    internal int? KeptContours;
    internal bool? HasArea;
    internal SvgContourFlags? Structure;
    internal bool? TransversalUnion;
    internal bool? Convex;
    internal bool DesnapBox;
    internal bool UseCurveSpans;

    /// <summary>Native mask/run dispatch uses the whole path, independent of bounded row storage.</summary>
    internal readonly bool UsesRunBlitter => !(PathBounds.Right - PathBounds.Left <= 32 && ((PathBounds.Right - PathBounds.Left + 3L) & ~3L) * (PathBounds.Bottom - PathBounds.Top) <= 1024);

    internal SvgAnalyticContourPreparation(ReadOnlySpan<SvgDevicePoint> points, ReadOnlySpan<int> ends, ReadOnlySpan<int> nestingSigns, bool isEvenOdd, int[]? signs, SvgPolygonMapping mapping, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgAnalyticClipBounds pathBounds, int outputWidth, int outputHeight)
    {
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        Points = points;
        Ends = ends;
        NestingSigns = nestingSigns;
        IsEvenOdd = isEvenOdd;
        Signs = signs;
        Mapping = mapping;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
        PathBounds = pathBounds;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
    }
}
