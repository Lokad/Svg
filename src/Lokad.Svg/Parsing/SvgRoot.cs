namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Runtime.CompilerServices;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(SvgRootLength? Width, SvgRootLength? Height, SvgViewBox? ViewBox, SvgGeometry[] Geometries, SvgDrawCommand[] Commands, bool HasUnsupportedChild, long ParserWorkUnits, SvgSceneStorage? SceneStorage) : IDisposable
{
    /// <summary>Materializes visible rectangle bounds only for parser characterization tests.</summary>
    internal SvgRectangle[] Rectangles => SvgGeometry.ToRectangles(Geometries);

    public void Dispose() => SceneStorage?.Dispose();
}

/// <summary>Represents the supported root viewBox-to-viewport mapping.</summary>
internal readonly record struct SvgViewBox(double X, double Y, double Width, double Height, SvgViewBoxAlignment HorizontalAlignment, SvgViewBoxAlignment VerticalAlignment, SvgViewBoxFit Fit);

internal enum SvgViewBoxAlignment { Min, Mid, Max, None }

internal enum SvgViewBoxFit { Meet, Slice, None }

internal readonly record struct SvgRectangle(double X, double Y, double Width, double Height, SvgColor Color, bool HasFill, SvgStroke? Stroke)
{
    /// <summary>Gets a separate paint clip without changing the original fill edges or paint-server box.</summary>
    internal SvgClipBox? Clip { get; init; }
}

internal readonly record struct SvgStroke(SvgColor Color, double Width);

/// <summary>Represents the supported axis-aligned line before backend rasterization.</summary>
internal readonly record struct SvgAxisAlignedLine(double X1, double Y1, double X2, double Y2, SvgColor Color, bool IsPainted, int StrokeWidth)
{
    internal SvgRectangle ToStrokeBand()
    {
        double halfWidth = StrokeWidth / 2d;
        return X1 != X2
            ? new SvgRectangle(Math.Min(X1, X2), Y1 - halfWidth, Math.Abs(X2 - X1), StrokeWidth, Color, IsPainted, null)
            : new SvgRectangle(X1 - halfWidth, Math.Min(Y1, Y2), StrokeWidth, Math.Abs(Y2 - Y1), Color, IsPainted, null);
    }
}

internal enum SvgGeometryKind { Invalid, Rectangle, AxisAlignedLine, OrthogonalBand, GlyphContours, PatternFill, GradientFill, GradientStroke, PolygonFill, RadialGradient, GradientPolygon, RadialPolygon, PatternPolygon }

/// <summary>Represents retained scene geometry independently from its raster fast path.</summary>
internal readonly struct SvgGeometry
{
    /// <summary>Gets source paint only for native solid rectangle draws without an opacity layer. Generated bands, images and paint-server fallbacks remain distinct.</summary>
    internal SvgSolidPaint? SolidPaint { get; init; }

    private readonly SvgGeometryKind _kind;
    private readonly SvgRectangle _rectangle;
    private readonly SvgAxisAlignedLine _axisAlignedLine;
    private readonly SvgGlyphGeometry? _glyph;
    private readonly SvgPatternGeometry? _pattern;
    private readonly SvgGradientGeometry? _gradient;
    private readonly SvgGradientStrokeGeometry? _gradientStroke;
    private readonly SvgRadialGeometry? _radial;
    private readonly SvgPolygonFill? _polygon;
    private readonly SvgGradientPolygon? _gradientPolygon;
    private readonly SvgRadialPolygon? _radialPolygon;
    private readonly SvgPatternPolygon? _patternPolygon;

    private SvgGeometry(SvgGeometryKind kind, SvgRectangle rectangle)
    {
        _kind = kind;
        _rectangle = rectangle;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgAxisAlignedLine axisAlignedLine)
    {
        _kind = SvgGeometryKind.AxisAlignedLine;
        _rectangle = default;
        _axisAlignedLine = axisAlignedLine;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgGlyphGeometry glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        _kind = SvgGeometryKind.GlyphContours;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = glyph;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgPatternGeometry pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _kind = SvgGeometryKind.PatternFill;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = pattern;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgGradientGeometry gradient)
    {
        ArgumentNullException.ThrowIfNull(gradient);
        _kind = SvgGeometryKind.GradientFill;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = gradient;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgGradientStrokeGeometry gradientStroke)
    {
        ArgumentNullException.ThrowIfNull(gradientStroke);
        _kind = SvgGeometryKind.GradientStroke;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = gradientStroke;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgRadialGeometry radial)
    {
        ArgumentNullException.ThrowIfNull(radial);
        _kind = SvgGeometryKind.RadialGradient;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = radial;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgPolygonFill polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        _kind = SvgGeometryKind.PolygonFill;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = polygon;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgGradientPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        _kind = SvgGeometryKind.GradientPolygon;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = polygon;
        _radialPolygon = null;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgRadialPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        _kind = SvgGeometryKind.RadialPolygon;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = polygon;
        _patternPolygon = null;
    }

    private SvgGeometry(SvgPatternPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        _kind = SvgGeometryKind.PatternPolygon;
        _rectangle = default;
        _axisAlignedLine = default;
        _glyph = null;
        _pattern = null;
        _gradient = null;
        _gradientStroke = null;
        _radial = null;
        _polygon = null;
        _gradientPolygon = null;
        _radialPolygon = null;
        _patternPolygon = null;
        _patternPolygon = polygon;
    }

    internal SvgGeometryKind Kind => _kind;

    internal SvgRectangle Rectangle
        => _kind is SvgGeometryKind.Rectangle or SvgGeometryKind.OrthogonalBand
            ? _rectangle
            : throw new InvalidOperationException("This SVG geometry does not contain a rectangle.");

    internal SvgAxisAlignedLine AxisAlignedLine
        => _kind == SvgGeometryKind.AxisAlignedLine
            ? _axisAlignedLine
            : throw new InvalidOperationException("This SVG geometry does not contain an axis-aligned line.");

    internal SvgGlyphGeometry Glyph
        => _kind == SvgGeometryKind.GlyphContours
            ? _glyph ?? throw new InvalidOperationException("This SVG geometry does not contain glyph contours.")
            : throw new InvalidOperationException("This SVG geometry does not contain glyph contours.");

    internal static SvgGeometry FromRectangle(SvgRectangle rectangle) => new(SvgGeometryKind.Rectangle, rectangle);

    internal static SvgGeometry FromAxisAlignedLine(SvgAxisAlignedLine line) => new(line);

    internal static SvgGeometry FromOrthogonalBand(SvgRectangle band) => new(SvgGeometryKind.OrthogonalBand, band);

    internal static SvgGeometry FromGlyphContours(SvgGlyphGeometry glyph) => new(glyph);

    internal static SvgGeometry FromPatternFill(SvgPatternGeometry pattern) => new(pattern);

    internal static SvgGeometry FromGradientFill(SvgGradientGeometry gradient) => new(gradient);

    internal static SvgGeometry FromGradientStroke(SvgGradientStrokeGeometry gradientStroke) => new(gradientStroke);

    internal static SvgGeometry FromRadialFill(SvgRadialGeometry radial) => new(radial);

    internal static SvgGeometry FromPolygonFill(SvgPolygonFill polygon) => new(polygon);

    internal static SvgGeometry FromGradientPolygon(SvgGradientPolygon polygon) => new(polygon);

    internal static SvgGeometry FromRadialPolygon(SvgRadialPolygon polygon) => new(polygon);

    internal static SvgGeometry FromPatternPolygon(SvgPatternPolygon polygon) => new(polygon);

    internal SvgGradientGeometry Gradient
        => _kind == SvgGeometryKind.GradientFill
            ? _gradient ?? throw new InvalidOperationException("This SVG geometry does not contain gradient stops.")
            : throw new InvalidOperationException("This SVG geometry does not contain gradient stops.");

    internal SvgPatternGeometry Pattern
        => _kind == SvgGeometryKind.PatternFill
            ? _pattern ?? throw new InvalidOperationException("This SVG geometry does not contain pattern tile content.")
            : throw new InvalidOperationException("This SVG geometry does not contain pattern tile content.");

    internal SvgGradientStrokeGeometry GradientStroke
        => _kind == SvgGeometryKind.GradientStroke
            ? _gradientStroke ?? throw new InvalidOperationException("This SVG geometry does not contain gradient stroke bands.")
            : throw new InvalidOperationException("This SVG geometry does not contain gradient stroke bands.");

    internal SvgRadialGeometry Radial
        => _kind == SvgGeometryKind.RadialGradient
            ? _radial ?? throw new InvalidOperationException("This SVG geometry does not contain radial stops.")
            : throw new InvalidOperationException("This SVG geometry does not contain radial stops.");

    internal SvgPolygonFill Polygon
        => _kind == SvgGeometryKind.PolygonFill
            ? _polygon ?? throw new InvalidOperationException("This SVG geometry does not contain polygon contours.")
            : throw new InvalidOperationException("This SVG geometry does not contain polygon contours.");

    internal SvgGradientPolygon GradientPolygon
        => _kind == SvgGeometryKind.GradientPolygon
            ? _gradientPolygon ?? throw new InvalidOperationException("This SVG geometry does not contain gradient polygon contours.")
            : throw new InvalidOperationException("This SVG geometry does not contain gradient polygon contours.");

    internal SvgRadialPolygon RadialPolygon
        => _kind == SvgGeometryKind.RadialPolygon
            ? _radialPolygon ?? throw new InvalidOperationException("This SVG geometry does not contain radial polygon contours.")
            : throw new InvalidOperationException("This SVG geometry does not contain radial polygon contours.");

    internal SvgPatternPolygon PatternPolygon
        => _kind == SvgGeometryKind.PatternPolygon
            ? _patternPolygon ?? throw new InvalidOperationException("This SVG geometry does not contain pattern polygon contours.")
            : throw new InvalidOperationException("This SVG geometry does not contain pattern polygon contours.");

    internal void Dispose()
    {
        if (_kind == SvgGeometryKind.GlyphContours) _glyph?.Dispose();
        if (_kind == SvgGeometryKind.PatternFill) _pattern?.Dispose();
        if (_kind == SvgGeometryKind.GradientFill) _gradient?.Dispose();
        if (_kind == SvgGeometryKind.GradientStroke) _gradientStroke?.Dispose();
        if (_kind == SvgGeometryKind.PolygonFill) _polygon?.Dispose();
        if (_kind == SvgGeometryKind.GradientPolygon) _gradientPolygon?.Dispose();
        if (_kind == SvgGeometryKind.RadialPolygon) _radialPolygon?.Dispose();
        if (_kind == SvgGeometryKind.PatternPolygon) _patternPolygon?.Dispose();
        if (_kind == SvgGeometryKind.RadialGradient) _radial?.Dispose();
    }

    internal static SvgRectangle[] ToRectangles(ReadOnlySpan<SvgGeometry> geometries)
    {
        SvgRectangle[] rectangles = new SvgRectangle[geometries.Length];
        for (int index = 0; index < geometries.Length; index++)
        {
            rectangles[index] = geometries[index].Kind switch
            {
                SvgGeometryKind.Rectangle => geometries[index].Rectangle,
                SvgGeometryKind.AxisAlignedLine => geometries[index].AxisAlignedLine.ToStrokeBand(),
                SvgGeometryKind.OrthogonalBand => geometries[index].Rectangle,
                _ => throw new SvgException(SvgErrorCode.MalformedInput, "SVG scene contains unsupported geometry.")
            };
            if (rectangles[index].Clip is {} clip)
            {
                SvgRectangle rectangle = rectangles[index];
                double left = Math.Max(rectangle.X, clip.X), top = Math.Max(rectangle.Y, clip.Y);
                double right = Math.Min(rectangle.X + rectangle.Width, clip.Right), bottom = Math.Min(rectangle.Y + rectangle.Height, clip.Bottom);
                rectangles[index] = rectangle with { X = left, Y = top, Width = Math.Max(0, right - left), Height = Math.Max(0, bottom - top), Clip = null };
            }
        }
        return rectangles;
    }
}

/// <summary>Selects the output-mapping discipline for one polygon fill.</summary>
internal enum SvgPolygonMapping
{
    /// <summary>Straight integer geometry under the rectangle quarter-pixel discipline.</summary>
    QuarterEdges,
    /// <summary>Straight fractional geometry mapped finite, exact under any output transform.</summary>
    FiniteEdges,
    /// <summary>Flattened or offset content needing uniform output mapping.</summary>
    FlattenedCurves,
    /// <summary>Native float open-stroke contours whose directed winding overlaps are retained through scan conversion.</summary>
    NativeStrokeEdges,
}

/// <summary>Retains one admitted diagonal solid polygon with separated simple contours and its scene-memory reservation until the scene is disposed.</summary>
/// <remarks>
/// <para>Points are device-space doubles with the caller transform and viewport shift applied; contour ends
/// partition them exactly and even-odd nesting signs ride alongside when the fill rule needs them. Flattened
/// curves ride the same arrays with a marker, since non-uniform output mapping distorts them while straight
/// edges map exactly. Straight edges map under the rectangle quarter-pixel discipline while flattened curve vertices,
/// never on a calibration grid, map finite. The optional
/// clip box stays in the same space and intersects raster bounds there. Scene storage covers all three arrays
/// exactly; callers charge raster work per evaluated pixel, matching rectangle bands.</para>
/// </remarks>
internal sealed class SvgPolygonFill : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    private SvgPolygonFill(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation)
    {
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
        Color = color;
        HasFill = hasFill;
        IsEvenOdd = isEvenOdd;
        Mapping = mapping;
        Clip = clip;
        _reservation = reservation;
    }

    internal SvgDevicePoint[] Points { get; }

    internal int[] ContourEnds { get; }

    /// <summary>Per-contour combination weights resolved at retention: parity signs for even-odd fills, winding roles for nonzero fills; the rasterizer falls back to signed accumulation when absent.</summary>
    internal int[]? Signs { get; }

    /// <summary>Retained quadratic spans in retained-point index space; empty when the fill carries no analytic curves.</summary>
    internal SvgQuadSpan[] QuadSpans { get; }

    /// <summary>Retained cubic spans in retained-point index space; empty when the fill carries no analytic cubics.</summary>
    internal SvgCubicSpan[] CubicSpans { get; }

    internal SvgColor Color { get; }

    internal bool HasFill { get; }

    internal bool IsEvenOdd { get; }

    internal SvgPolygonMapping Mapping { get; }

    internal SvgClipBox? Clip { get; }

    /// <summary>Creates one polygon fill; adopts the caller scene reservation when supplied, which must cover exactly points, contourEnds and signs, and otherwise reserves exact scene storage. Ownership transfers to the fill in both cases.</summary>
    /// <summary>Creates one polygon fill without recorded curves; quadratic spans stay empty on this route.</summary>
    internal static SvgPolygonFill Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        return Create(points, contourEnds, signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), color, hasFill, isEvenOdd, mapping, clip, reservation, budget);
    }

    /// <summary>Creates one polygon fill carrying retained quadratic spans; adopts the caller scene reservation when supplied, which must cover exactly points, contourEnds, signs, quad spans and cubic spans, and otherwise reserves exact scene storage. Ownership transfers to the fill in both cases.</summary>
    /// <summary>Creates one polygon fill carrying retained quadratic spans but no cubic spans.</summary>
    internal static SvgPolygonFill Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        return Create(points, contourEnds, signs, quadSpans, Array.Empty<SvgCubicSpan>(), color, hasFill, isEvenOdd, mapping, clip, reservation, budget);
    }

    /// <summary>Creates one polygon fill carrying retained quadratic and cubic spans.</summary>
    internal static SvgPolygonFill Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgColor color, bool hasFill, bool isEvenOdd, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        ArgumentNullException.ThrowIfNull(budget);
        if (isEvenOdd) ArgumentNullException.ThrowIfNull(signs);
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(contourEnds.Length) + (signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(signs.Length)) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        if (reservation is null)
        {
            reservation = bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
        }
        return new SvgPolygonFill(points, contourEnds, signs, quadSpans, cubicSpans, color, hasFill, isEvenOdd, mapping, clip, reservation);
    }

    /// <summary>Stamps one polygon fill with a mask value inside its mask region, transferring scene ownership to the stamped fill.</summary>
    /// <remarks><para>Contour storage is shared with the source fill and the source reservation transfers to the result. The caller drops the source without disposing it; disposing the dropped source stays a safe no-op.</para></remarks>
    internal SvgPolygonFill Stamped(byte value, SvgClipBox region)
    {
        SvgColor stamped = new(Color.Red, Color.Green, Color.Blue, SvgReferenceResolver.MultiplyAlpha(Color.Alpha, value));
        SvgClipBox clipped = Clip is null ? region : Clip.Value.Intersect(region);
        RenderBudget.BudgetReservation? reservation = _reservation;
        _reservation = null;
        return new SvgPolygonFill(Points, ContourEnds, Signs, QuadSpans, CubicSpans, stamped, HasFill, IsEvenOdd, Mapping, clipped, reservation);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained linear-gradient polygon contour arrays with their paint box and scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgGradientPolygon : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    private SvgGradientPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgLinearGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation)
    {
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
        Gradient = gradient;
        Box = box;
        HasFill = hasFill;
        IsEvenOdd = isEvenOdd;
        Opacity = opacity;
        Mapping = mapping;
        Clip = clip;
        _reservation = reservation;
    }

    internal SvgDevicePoint[] Points { get; }

    internal int[] ContourEnds { get; }

    /// <summary>Per-contour combination weights resolved at retention: parity signs for even-odd fills, winding roles for nonzero fills; the rasterizer falls back to signed accumulation when absent.</summary>
    internal int[]? Signs { get; }

    /// <summary>Retained quadratic spans in retained-point index space; empty when the outline carries no analytic curves.</summary>
    internal SvgQuadSpan[] QuadSpans { get; }

    /// <summary>Retained cubic spans in retained-point index space; empty when the outline carries no analytic cubics.</summary>
    internal SvgCubicSpan[] CubicSpans { get; }

    internal SvgLinearGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal bool HasFill { get; }

    internal bool IsEvenOdd { get; }

    internal byte Opacity { get; }

    internal SvgPolygonMapping Mapping { get; }

    internal SvgClipBox? Clip { get; }

    /// <summary>Creates one linear-gradient polygon fill; adopts the caller scene reservation when supplied, which must cover exactly points, contourEnds and signs, and otherwise reserves exact scene storage. Ownership transfers to the fill in both cases.</summary>
    /// <summary>Creates one gradient outline without recorded curves; spans stay empty on this route.</summary>
    internal static SvgGradientPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgLinearGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        return Create(points, contourEnds, signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation, budget);
    }

    /// <summary>Creates one gradient outline carrying retained spans with exact scene storage.</summary>
    internal static SvgGradientPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgLinearGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        ArgumentNullException.ThrowIfNull(budget);
        if (isEvenOdd) ArgumentNullException.ThrowIfNull(signs);
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(contourEnds.Length) + (signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(signs.Length)) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        if (reservation is null)
        {
            reservation = bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
        }
        return new SvgGradientPolygon(points, contourEnds, signs, quadSpans, cubicSpans, gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation);
    }

    /// <summary>Repaints one solid outline as a linear-gradient outline, rendering single-stop and degenerate paints as solid end-stop fills. Callers admit the paint once for identical errors.</summary>
    internal static SvgGeometry RepaintOutline(SvgPolygonFill solid, SvgLinearGradient paint, SvgRectangle box, byte opacity, RenderBudget budget)
    {
        if (paint.Stops.Length == 1 || (paint.X1 == paint.X2 && paint.Y1 == paint.Y2 && !paint.IsEmpty))
        {
            if (paint.Stops.Length > 1 && paint.Spread != SvgGradientSpread.Pad) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only pad-spread degenerate linear gradient strokes are not implemented.");
            return SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, SvgPaintResolver.ApplyOpacity(paint.End, opacity), solid.HasFill, solid.IsEvenOdd, solid.Mapping, solid.Clip, null, budget));
        }
        return SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, paint, box, solid.HasFill, solid.IsEvenOdd, opacity, solid.Mapping, solid.Clip, null, budget));
    }

    /// <summary>Stamps one gradient polygon with a mask value inside its mask region, transferring scene ownership to the stamped outline.</summary>
    /// <remarks><para>Contour storage is shared with the source outline and the source reservation transfers to the result. The caller drops the source without disposing it; disposing the dropped source stays a safe no-op.</para></remarks>
    internal SvgGradientPolygon Stamped(byte value, SvgClipBox region)
    {
        byte stamped = SvgReferenceResolver.MultiplyAlpha(Opacity, value);
        SvgClipBox clipped = Clip is null ? region : Clip.Value.Intersect(region);
        RenderBudget.BudgetReservation? reservation = _reservation;
        _reservation = null;
        return new SvgGradientPolygon(Points, ContourEnds, Signs, QuadSpans, CubicSpans, Gradient, Box, HasFill, IsEvenOdd, stamped, Mapping, clipped, reservation);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained radial-gradient polygon contour arrays with their paint box and scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgRadialPolygon : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    private SvgRadialPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation)
    {
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
        Gradient = gradient;
        Box = box;
        HasFill = hasFill;
        IsEvenOdd = isEvenOdd;
        Opacity = opacity;
        Mapping = mapping;
        Clip = clip;
        _reservation = reservation;
    }

    internal SvgDevicePoint[] Points { get; }

    internal int[] ContourEnds { get; }

    /// <summary>Per-contour combination weights resolved at retention: parity signs for even-odd fills, winding roles for nonzero fills; the rasterizer falls back to signed accumulation when absent.</summary>
    internal int[]? Signs { get; }

    /// <summary>Retained quadratic spans in retained-point index space; empty when the outline carries no analytic curves.</summary>
    internal SvgQuadSpan[] QuadSpans { get; }

    /// <summary>Retained cubic spans in retained-point index space; empty when the outline carries no analytic cubics.</summary>
    internal SvgCubicSpan[] CubicSpans { get; }

    internal SvgRadialGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal bool HasFill { get; }

    internal bool IsEvenOdd { get; }

    internal byte Opacity { get; }

    internal SvgPolygonMapping Mapping { get; }

    internal SvgClipBox? Clip { get; }

    /// <summary>Creates one radial-gradient polygon fill; adopts the caller scene reservation when supplied, which must cover exactly points, contourEnds and signs, and otherwise reserves exact scene storage. Ownership transfers to the fill in both cases.</summary>
    /// <summary>Creates one radial outline without recorded curves; spans stay empty on this route.</summary>
    internal static SvgRadialPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        return Create(points, contourEnds, signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation, budget);
    }

    /// <summary>Creates one radial outline carrying retained spans with exact scene storage.</summary>
    internal static SvgRadialPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        ArgumentNullException.ThrowIfNull(budget);
        if (isEvenOdd) ArgumentNullException.ThrowIfNull(signs);
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(contourEnds.Length) + (signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(signs.Length)) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        if (reservation is null)
        {
            reservation = bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
        }
        return new SvgRadialPolygon(points, contourEnds, signs, quadSpans, cubicSpans, gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation);
    }

    /// <summary>Repaints one solid outline as a radial-gradient outline, rendering single-stop and degenerate paints as solid end-stop fills. Callers admit the paint once for identical errors.</summary>
    internal static SvgGeometry RepaintOutline(SvgPolygonFill solid, SvgRadialGradient paint, SvgRectangle box, byte opacity, RenderBudget budget)
    {
        if (paint.Stops.Length == 1 || (paint.Radius == 0 && !paint.IsEmpty)) return SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, SvgPaintResolver.ApplyOpacity(paint.End, opacity), solid.HasFill, solid.IsEvenOdd, solid.Mapping, solid.Clip, null, budget));
        return SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, paint, box, solid.HasFill, solid.IsEvenOdd, opacity, solid.Mapping, solid.Clip, null, budget));
    }

    /// <summary>Stamps one radial polygon with a mask value inside its mask region, transferring scene ownership to the stamped outline.</summary>
    /// <remarks><para>Contour storage is shared with the source outline and the source reservation transfers to the result. The caller drops the source without disposing it; disposing the dropped source stays a safe no-op.</para></remarks>
    internal SvgRadialPolygon Stamped(byte value, SvgClipBox region)
    {
        byte stamped = SvgReferenceResolver.MultiplyAlpha(Opacity, value);
        SvgClipBox clipped = Clip is null ? region : Clip.Value.Intersect(region);
        RenderBudget.BudgetReservation? reservation = _reservation;
        _reservation = null;
        return new SvgRadialPolygon(Points, ContourEnds, Signs, QuadSpans, CubicSpans, Gradient, Box, HasFill, IsEvenOdd, stamped, Mapping, clipped, reservation);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained pattern-tile polygon contour arrays with their integer tile grid and scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgPatternPolygon : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    private SvgPatternPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRectangle content, int originX, int originY, int tileWidth, int tileHeight, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation)
    {
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
        Content = content;
        OriginX = originX;
        OriginY = originY;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        HasFill = hasFill;
        IsEvenOdd = isEvenOdd;
        Opacity = opacity;
        Mapping = mapping;
        Clip = clip;
        _reservation = reservation;
    }

    internal SvgDevicePoint[] Points { get; }

    internal int[] ContourEnds { get; }

    /// <summary>Per-contour combination weights resolved at retention: parity signs for even-odd fills, winding roles for nonzero fills; the rasterizer falls back to signed accumulation when absent.</summary>
    internal int[]? Signs { get; }

    /// <summary>Retained quadratic spans in retained-point index space; empty when the outline carries no analytic curves.</summary>
    internal SvgQuadSpan[] QuadSpans { get; }

    /// <summary>Retained cubic spans in retained-point index space; empty when the outline carries no analytic cubics.</summary>
    internal SvgCubicSpan[] CubicSpans { get; }

    internal SvgRectangle Content { get; }

    internal int OriginX { get; }

    internal int OriginY { get; }

    internal int TileWidth { get; }

    internal int TileHeight { get; }

    internal bool HasFill { get; }

    internal bool IsEvenOdd { get; }

    internal byte Opacity { get; }

    internal SvgPolygonMapping Mapping { get; }

    internal SvgClipBox? Clip { get; }

    /// <summary>Creates one pattern outline without recorded curves; spans stay empty on this route.</summary>
    internal static SvgPatternPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgRectangle content, int originX, int originY, int tileWidth, int tileHeight, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget budget)
    {
        return Create(points, contourEnds, signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), content, originX, originY, tileWidth, tileHeight, hasFill, isEvenOdd, opacity, mapping, clip, budget);
    }

    /// <summary>Creates one pattern outline carrying retained spans with exact scene storage.</summary>
    internal static SvgPatternPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRectangle content, int originX, int originY, int tileWidth, int tileHeight, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        ArgumentNullException.ThrowIfNull(budget);
        if (isEvenOdd) ArgumentNullException.ThrowIfNull(signs);
        long bytes;
        try { bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(contourEnds.Length) + (signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(signs.Length)) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length)); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        RenderBudget.BudgetReservation? reservation = bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
        return new SvgPatternPolygon(points, contourEnds, signs, quadSpans, cubicSpans, content, originX, originY, tileWidth, tileHeight, hasFill, isEvenOdd, opacity, mapping, clip, reservation);
    }

    /// <summary>Repaints one solid outline as a pattern-tile outline, rendering empty tiles as unpainted fills. Callers admit the paint once for identical errors.</summary>
    internal static SvgGeometry RepaintOutline(SvgPolygonFill solid, SvgPatternTile tile, byte opacity, RenderBudget budget)
    {
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || tile.Content is null) return SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(solid.Points, solid.ContourEnds, solid.Signs, default, false, solid.IsEvenOdd, solid.Mapping, solid.Clip, null, budget));
        return SvgGeometry.FromPatternPolygon(SvgPatternPolygon.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, tile.Content.Value.Rectangle, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, solid.HasFill, solid.IsEvenOdd, opacity, solid.Mapping, solid.Clip, budget));
    }

    /// <summary>Stamps one pattern polygon with a mask value inside its mask region, transferring scene ownership to the stamped outline.</summary>
    /// <remarks><para>Contour storage is shared with the source outline and the source reservation transfers to the result. The caller drops the source without disposing it; disposing the dropped source stays a safe no-op.</para></remarks>
    internal SvgPatternPolygon Stamped(byte value, SvgClipBox region)
    {
        byte stamped = SvgReferenceResolver.MultiplyAlpha(Opacity, value);
        SvgClipBox clipped = Clip is null ? region : Clip.Value.Intersect(region);
        RenderBudget.BudgetReservation? reservation = _reservation;
        _reservation = null;
        return new SvgPatternPolygon(Points, ContourEnds, Signs, QuadSpans, CubicSpans, Content, OriginX, OriginY, TileWidth, TileHeight, HasFill, IsEvenOdd, stamped, Mapping, clipped, reservation);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained glyph contour arrays and their scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgGlyphGeometry : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    internal SvgGlyphGeometry(SvgGlyphQuadraticContour[] contours, SvgColor color) : this(contours, color, null)
    {
        ArgumentNullException.ThrowIfNull(contours);
    }

    private SvgGlyphGeometry(SvgGlyphQuadraticContour[] contours, SvgColor color, RenderBudget.BudgetReservation? reservation)
    {
        Contours = contours;
        Color = color;
        _reservation = reservation;
    }

    internal SvgGlyphQuadraticContour[] Contours { get; }

    internal SvgColor Color { get; }

    internal static SvgGlyphGeometry Create(SvgGlyphContourStorage contours, SvgColor color)
    {
        ArgumentNullException.ThrowIfNull(contours);
        SvgGlyphContourTransfer transfer = contours.TransferOwnership();
        try { return new SvgGlyphGeometry(transfer.Contours, color, transfer.Reservation); }
        catch
        {
            transfer.Reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Reserves scene ownership for contours supplied by a non-rendering caller.</summary>
    internal static SvgGlyphGeometry Create(SvgGlyphQuadraticContour[] contours, SvgColor color, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(contours);
        ArgumentNullException.ThrowIfNull(budget);
        int segmentCount = 0;
        try
        {
            foreach (SvgGlyphQuadraticContour contour in contours)
            {
                ArgumentNullException.ThrowIfNull(contour.Segments);
                segmentCount = checked(segmentCount + contour.Segments.Length);
            }
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph contour storage exceeds the supported accounting range.");
        }
        RenderBudget.BudgetReservation reservation = budget.Reserve(GetStorageByteCount(contours.Length, segmentCount), SvgMemoryCategory.Scene);
        return new SvgGlyphGeometry(contours, color, reservation);
    }

    internal static long GetStorageByteCount(int contourCount, int segmentCount)
    {
        try
        {
            return checked(32L + (long)contourCount * Unsafe.SizeOf<SvgGlyphQuadraticContour>() + (long)contourCount * 32L + (long)segmentCount * Unsafe.SizeOf<SvgGlyphQuadraticSegment>());
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph contour storage exceeds the supported accounting range.");
        }
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained pattern tile content with its integer grid and fill region.</summary>
internal sealed class SvgPatternGeometry : IDisposable
{
    internal SvgPatternGeometry(SvgRectangle Content, int OriginX, int OriginY, int TileWidth, int TileHeight, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke)
    {
        this.Content = Content;
        this.OriginX = OriginX;
        this.OriginY = OriginY;
        this.TileWidth = TileWidth;
        this.TileHeight = TileHeight;
        this.Fill = Fill;
        this.Opacity = Opacity;
        this.Stroke = Stroke;
    }

    internal SvgRectangle Content { get; }

    internal int OriginX { get; }

    internal int OriginY { get; }

    internal int TileWidth { get; }

    internal int TileHeight { get; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    internal SvgPatternGeometry ClipTo(SvgClipBox box)
        => new(Content, OriginX, OriginY, TileWidth, TileHeight, box.ClipRectangle(Fill), Opacity, Stroke);

    internal SvgPatternGeometry Stamped(byte value)
        => new(Content, OriginX, OriginY, TileWidth, TileHeight, Fill, SvgReferenceResolver.MultiplyAlpha(Opacity, value), Stroke is null ? null : new SvgStroke(new SvgColor(Stroke.Value.Color.Red, Stroke.Value.Color.Green, Stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Stroke.Value.Color.Alpha, value)), Stroke.Value.Width));

    public void Dispose()
    {
    }
}

/// <summary>Owns retained linear-gradient stops with their bounding-box vector and fill region.</summary>
internal sealed class SvgGradientGeometry : IDisposable
{
    internal SvgGradientGeometry(SvgLinearGradient Gradient, SvgRectangle Box, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke)
    {
        this.Gradient = Gradient;
        this.Box = Box;
        this.Fill = Fill;
        this.Opacity = Opacity;
        this.Stroke = Stroke;
    }

    internal SvgLinearGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    internal SvgGradientGeometry ClipTo(SvgClipBox box)
        => new(Gradient, Box, box.ClipRectangle(Fill), Opacity, Stroke);

    internal SvgGradientGeometry Stamped(byte value)
        => new(Gradient, Box, Fill, SvgReferenceResolver.MultiplyAlpha(Opacity, value), Stroke is null ? null : new SvgStroke(new SvgColor(Stroke.Value.Color.Red, Stroke.Value.Color.Green, Stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Stroke.Value.Color.Alpha, value)), Stroke.Value.Width));

    public void Dispose()
    {
    }
}

/// <summary>Owns retained radial-gradient stops with their circle and fill region.</summary>
internal sealed class SvgRadialGeometry : IDisposable
{
    internal SvgRadialGeometry(SvgRadialGradient Gradient, SvgRectangle Box, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke)
    {
        this.Gradient = Gradient;
        this.Box = Box;
        this.Fill = Fill;
        this.Opacity = Opacity;
        this.Stroke = Stroke;
    }

    internal SvgRadialGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    internal SvgRadialGeometry ClipTo(SvgClipBox box)
        => new(Gradient, Box, box.ClipRectangle(Fill), Opacity, Stroke);

    internal SvgRadialGeometry Stamped(byte value)
        => new(Gradient, Box, Fill, SvgReferenceResolver.MultiplyAlpha(Opacity, value), Stroke is null ? null : new SvgStroke(new SvgColor(Stroke.Value.Color.Red, Stroke.Value.Color.Green, Stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Stroke.Value.Color.Alpha, value)), Stroke.Value.Width));

    public void Dispose()
    {
    }
}

/// <summary>Retains one solid fill with four gradient-evaluated stroke bands.</summary>
internal sealed class SvgGradientStrokeGeometry : IDisposable
{
    internal SvgGradientStrokeGeometry(SvgRectangle Fill, SvgRectangle Top, SvgRectangle Bottom, SvgRectangle Left, SvgRectangle Right, SvgLinearGradient Gradient, SvgRectangle Box, byte Opacity, bool HasStroke)
    {
        this.Fill = Fill;
        this.Top = Top;
        this.Bottom = Bottom;
        this.Left = Left;
        this.Right = Right;
        this.Gradient = Gradient;
        this.Box = Box;
        this.Opacity = Opacity;
        this.HasStroke = HasStroke;
    }

    internal SvgRectangle Fill { get; }

    internal SvgRectangle Top { get; }

    internal SvgRectangle Bottom { get; }

    internal SvgRectangle Left { get; }

    internal SvgRectangle Right { get; }

    internal SvgLinearGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal byte Opacity { get; }

    internal bool HasStroke { get; }

    internal SvgRectangle OuterBox()
    {
        double left = Math.Min(Fill.X, Math.Min(Top.X, Math.Min(Bottom.X, Math.Min(Left.X, Right.X))));
        double top = Math.Min(Fill.Y, Math.Min(Top.Y, Math.Min(Bottom.Y, Math.Min(Left.Y, Right.Y))));
        double right = Math.Max(Fill.X + Fill.Width, Math.Max(Top.X + Top.Width, Math.Max(Bottom.X + Bottom.Width, Math.Max(Left.X + Left.Width, Right.X + Right.Width))));
        double bottom = Math.Max(Fill.Y + Fill.Height, Math.Max(Top.Y + Top.Height, Math.Max(Bottom.Y + Bottom.Height, Math.Max(Left.Y + Left.Height, Right.Y + Right.Height))));
        return new SvgRectangle(left, top, right - left, bottom - top, Fill.Color, Fill.HasFill, null);
    }

    internal SvgGradientStrokeGeometry ClipTo(SvgClipBox box)
        => new(box.ClipRectangle(Fill), box.ClipRectangle(Top), box.ClipRectangle(Bottom), box.ClipRectangle(Left), box.ClipRectangle(Right), Gradient, Box, Opacity, HasStroke);

    internal SvgGradientStrokeGeometry Stamped(byte value)
        => new(Fill with { Color = new SvgColor(Fill.Color.Red, Fill.Color.Green, Fill.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Fill.Color.Alpha, value)) }, Top, Bottom, Left, Right, Gradient, Box, SvgReferenceResolver.MultiplyAlpha(Opacity, value), HasStroke);

    public void Dispose()
    {
    }
}

/// <summary>Retains one local definition's typed geometry and the narrow instance-style override capability.</summary>
internal readonly record struct SvgDefinition(SvgGeometry[] Geometries, bool AllowsInstanceFillOverride, double? InstanceStrokeMiterLimit, SvgViewBox? SymbolViewBox);

internal enum SvgDrawCommandKind { Geometry, BeginGroup, EndGroup }

/// <summary>References retained scene geometry or marks an opacity-scope boundary.</summary>
internal readonly record struct SvgDrawCommand(SvgDrawCommandKind Kind, int GeometryIndex, byte Opacity, SvgClipBox? MaskRegion, int MaskContentIndex);

internal readonly record struct SvgPoint(int X, int Y);

/// <summary>Retains one user-space fractional point-list vertex before device mapping.</summary>
internal readonly record struct SvgFractionalPoint(double X, double Y);

internal sealed class SvgSceneStorage : IDisposable
{
    private readonly RenderBudget.BudgetReservation? _geometryReservation;
    private readonly RenderBudget.BudgetReservation? _commandReservation;
    private readonly List<RenderBudget.BudgetReservation> _paintReservations;
    private readonly RenderBudget.BudgetReservation? _maskContentReservation;
    private readonly List<RenderBudget.BudgetReservation> _maskReservations;

    private SvgSceneStorage(SvgGeometry[] geometries, RenderBudget.BudgetReservation? geometryReservation, SvgDrawCommand[] commands, RenderBudget.BudgetReservation? commandReservation, List<RenderBudget.BudgetReservation> paintReservations, SvgMaskContent[] maskContents, RenderBudget.BudgetReservation? maskContentReservation, List<RenderBudget.BudgetReservation> maskReservations)
    {
        Geometries = geometries;
        Commands = commands;
        MaskContents = maskContents;
        _geometryReservation = geometryReservation;
        _commandReservation = commandReservation;
        _paintReservations = paintReservations;
        _maskContentReservation = maskContentReservation;
        _maskReservations = maskReservations;
    }

    internal SvgGeometry[] Geometries { get; }

    /// <summary>Materializes rectangles only for parser characterization tests.</summary>
    internal SvgRectangle[] Rectangles => SvgGeometry.ToRectangles(Geometries);

    internal SvgDrawCommand[] Commands { get; }

    internal SvgMaskContent[] MaskContents { get; }

    internal static SvgSceneStorage Create(BudgetedSceneList<SvgGeometry> geometries, BudgetedSceneList<SvgDrawCommand> commands, List<RenderBudget.BudgetReservation> paintReservations, BudgetedSceneList<SvgMaskContent> maskContents, List<RenderBudget.BudgetReservation> maskReservations)
    {
        ArgumentNullException.ThrowIfNull(paintReservations);
        ArgumentNullException.ThrowIfNull(maskContents);
        ArgumentNullException.ThrowIfNull(maskReservations);
        OwnedSceneArray<SvgGeometry> ownedGeometries = geometries.Detach();
        try
        {
            OwnedSceneArray<SvgDrawCommand> ownedCommands = commands.Detach();
            try
            {
                OwnedSceneArray<SvgMaskContent> ownedMasks = maskContents.Detach();
                return new SvgSceneStorage(ownedGeometries.Items, ownedGeometries.Reservation, ownedCommands.Items, ownedCommands.Reservation, paintReservations, ownedMasks.Items, ownedMasks.Reservation, maskReservations);
            }
            catch
            {
                ownedCommands.Dispose();
                foreach (RenderBudget.BudgetReservation reservation in maskReservations) reservation.Dispose();
                throw;
            }
        }
        catch
        {
            foreach (SvgGeometry geometry in ownedGeometries.Items) geometry.Dispose();
            ownedGeometries.Dispose();
            foreach (RenderBudget.BudgetReservation reservation in paintReservations) reservation.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (SvgGeometry geometry in Geometries) geometry.Dispose();
        foreach (SvgMaskContent content in MaskContents) foreach (SvgGeometry geometry in content.Geometries) geometry.Dispose();
        _geometryReservation?.Dispose();
        _commandReservation?.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in _paintReservations) reservation.Dispose();
        _maskContentReservation?.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in _maskReservations) reservation.Dispose();
    }
}

/// <summary>Owns the mutable, budgeted scene construction phase until arrays transfer to scene storage.</summary>
internal sealed class SvgSceneBuilder : IDisposable
{
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private readonly BudgetedSceneList<SvgGeometry> _geometries;
    private readonly BudgetedSceneList<SvgDrawCommand> _commands;
    private List<RenderBudget.BudgetReservation> _paintReservations = new();
    private readonly BudgetedSceneList<SvgMaskContent> _maskContents;
    private List<RenderBudget.BudgetReservation> _maskReservations = new();
    private readonly HashSet<SvgGradientStop[]> _reservedStopArrays = new();
    private bool _built;

    internal SvgSceneBuilder(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(workCounter);
        _budget = budget;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
        _geometries = new(budget, workCounter, cancellationToken);
        _commands = new(budget, workCounter, cancellationToken);
        _maskContents = new(budget, workCounter, cancellationToken);
    }

    internal void AddGeometry(SvgGeometry geometry)
    {
        ReserveGradientStops(geometry);
        int geometryIndex = _geometries.Count;
        _geometries.Add(geometry);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, geometryIndex, 255, null, -1));
    }

    internal int GeometryCount => _geometries.Count;

    internal SvgGeometry GeometryAt(int index) => _geometries[index];

    internal int CommandCount => _commands.Count;

    internal SvgDrawCommand CommandAt(int index) => _commands[index];

    internal void ReplaceCommand(int index, SvgDrawCommand command) => _commands[index] = command;

    internal void InsertCommands(int index, ReadOnlySpan<SvgDrawCommand> commands) => _commands.Insert(index, commands);

    /// <summary>Replaces one retained scene geometry with its clipped successor, keeping commands and releasing replaced storage.</summary>
    internal void ReplaceGeometry(int index, SvgGeometry geometry)
    {
        ReserveGradientStops(geometry);
        _geometries[index].Dispose();
        _geometries[index] = geometry;
    }

    /// <summary>Transfers one scene-owned reservation per distinct retained gradient stop array.</summary>
    internal void ReserveGradientStops(SvgGeometry geometry)
    {
        SvgGradientStop[]? stops = geometry.Kind switch
        {
            SvgGeometryKind.GradientFill => geometry.Gradient.Gradient.Stops,
            SvgGeometryKind.GradientStroke => geometry.GradientStroke.Gradient.Stops,
            SvgGeometryKind.RadialGradient => geometry.Radial.Gradient.Stops,
            _ => null,
        };
        if (stops is null || stops.Length == 0 || !_reservedStopArrays.Add(stops)) return;
        _paintReservations.Add(_budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(stops.Length), SvgMemoryCategory.Scene));
    }

    internal void AddGeometries(ReadOnlySpan<SvgGeometry> geometries)
    {
        foreach (SvgGeometry geometry in geometries) ReserveGradientStops(geometry);
        int firstGeometryIndex = _geometries.Count;
        _geometries.AddRange(geometries);
        for (int index = 0; index < geometries.Length; index++) _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, firstGeometryIndex + index, 255, null, -1));
    }

    internal SvgSceneSlot AddReferencePlaceholder()
    {
        SvgSceneSlot slot = new(_geometries.Count, _commands.Count);
        _geometries.Add(default);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, slot.GeometryIndex, 255, null, -1));
        return slot;
    }

    /// <summary>Expands one deferred reference placeholder into its resolved geometries, returning the inserted extra-command count that later slots must add.</summary>
    internal int ReplaceWithMany(SvgSceneSlot slot, ReadOnlySpan<SvgGeometry> applied)
    {
        if (applied.Length == 0) throw new InvalidOperationException("Deferred reference resolution requires at least one geometry.");
        foreach (SvgGeometry geometry in applied) ReserveGradientStops(geometry);
        _geometries[slot.GeometryIndex] = applied[0];
        _commands[slot.CommandIndex] = new SvgDrawCommand(SvgDrawCommandKind.Geometry, slot.GeometryIndex, 255, null, -1);
        if (applied.Length == 1) return 0;
        int firstGeometryIndex = _geometries.Count;
        for (int index = 1; index < applied.Length; index++) _geometries.Add(applied[index]);
        SvgDrawCommand[] extra = new SvgDrawCommand[applied.Length - 1];
        for (int index = 0; index < extra.Length; index++) extra[index] = new SvgDrawCommand(SvgDrawCommandKind.Geometry, firstGeometryIndex + index, 255, null, -1);
        _commands.Insert(slot.CommandIndex + 1, extra);
        return extra.Length;
    }

    internal void BeginOpacityScope(byte opacity) => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, opacity, null, -1));

    internal void EndOpacityScope() => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1));

    /// <summary>Copies one mask content evaluation under scene ownership, returning its command index.</summary>
    internal int AddMaskContent(SvgMaskContent content)
    {
        SvgGeometry[] copied = new SvgGeometry[content.Geometries.Length];
        try
        {
            for (int index = 0; index < content.Geometries.Length; index++)
            {
                _workCounter.Consume(_cancellationToken);
                if (content.Geometries[index].Kind != SvgGeometryKind.PolygonFill) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid mask content is implemented.");
                SvgPolygonFill polygon = content.Geometries[index].Polygon;
                long bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(polygon.Points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(polygon.ContourEnds.Length) + (polygon.Signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(polygon.Signs.Length)));
                RenderBudget.BudgetReservation reservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
                try
                {
                    SvgDevicePoint[] points = new SvgDevicePoint[polygon.Points.Length];
                    int[] ends = new int[polygon.ContourEnds.Length];
                    int[]? signs = null;
                    if (polygon.Signs is int[] sourceSigns)
                    {
                        signs = new int[sourceSigns.Length];
                        for (int sign = 0; sign < signs.Length; sign++)
                        {
                            _workCounter.Consume(_cancellationToken);
                            signs[sign] = sourceSigns[sign];
                        }
                    }
                    for (int point = 0; point < points.Length; point++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        points[point] = polygon.Points[point];
                    }
                    for (int end = 0; end < ends.Length; end++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        ends[end] = polygon.ContourEnds[end];
                    }
                    copied[index] = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, ends, signs, polygon.Color, polygon.HasFill, polygon.IsEvenOdd, polygon.Mapping, polygon.Clip, reservation, _budget));
                }
                catch
                {
                    reservation.Dispose();
                    throw;
                }
            }
        }
        catch
        {
            foreach (SvgGeometry geometry in copied) geometry.Dispose();
            throw;
        }
        _maskReservations.Add(_budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(copied.Length), SvgMemoryCategory.Scene));
        int contentIndex = _maskContents.Count;
        _maskContents.Add(new SvgMaskContent(copied, content.Luminance, content.IsAlpha));
        return contentIndex;
    }

    internal SvgSceneStorage Build()
    {
        if (_built) throw new InvalidOperationException("The SVG scene has already been built.");
        _built = true;
        SvgSceneStorage storage = SvgSceneStorage.Create(_geometries, _commands, _paintReservations, _maskContents, _maskReservations);
        _paintReservations = new();
        _maskReservations = new();
        return storage;
    }

    public void Dispose()
    {
        foreach (SvgGeometry geometry in _geometries.Items) geometry.Dispose();
        _geometries.Dispose();
        _commands.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in _paintReservations) reservation.Dispose();
        _paintReservations.Clear();
        _maskContents.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in _maskReservations) reservation.Dispose();
        _maskReservations.Clear();
    }
}

/// <summary>Identifies the parallel retained-scene entries reserved for a deferred reference.</summary>
internal readonly record struct SvgSceneSlot(int GeometryIndex, int CommandIndex);

internal sealed class BudgetedSceneList<T> : IDisposable where T : struct
{
    private const int CopyCheckpointItems = 4_096;
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private T[] _items = [];
    private RenderBudget.BudgetReservation? _reservation;
    private int _count;

    internal BudgetedSceneList(RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        _budget = budget;
        _workCounter = workCounter;
        _cancellationToken = cancellationToken;
    }

    internal int Count => _count;

    internal ReadOnlySpan<T> Items => _items.AsSpan(0, _count);

    internal T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    internal void Add(T item)
    {
        EnsureAdditionalCapacity(1);
        _items[_count++] = item;
    }

    internal void AddRange(ReadOnlySpan<T> items)
    {
        EnsureAdditionalCapacity(items.Length);
        CopyWithCheckpoints(items, _items.AsSpan(_count));
        _count += items.Length;
    }

    /// <summary>Inserts items at the given scene position, shifting later entries right under bounded work.</summary>
    internal void Insert(int index, ReadOnlySpan<T> items)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (items.Length == 0) return;
        EnsureAdditionalCapacity(items.Length);
        for (int remaining = _count - index; remaining > 0; remaining -= CopyCheckpointItems)
        {
            _workCounter.Consume(_cancellationToken);
            int count = Math.Min(CopyCheckpointItems, remaining);
            int sourceStart = index + remaining - count;
            _items.AsSpan(sourceStart, count).CopyTo(_items.AsSpan(sourceStart + items.Length, count));
        }
        CopyWithCheckpoints(items, _items.AsSpan(index, items.Length));
        _count += items.Length;
    }

    internal OwnedSceneArray<T> Detach()
    {
        if (_count == 0)
        {
            Dispose();
            return new([], null);
        }
        long bytes = ParserCollectionMemory.GetArrayByteCount<T>(_count);
        RenderBudget.BudgetReservation reservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
        T[] exact;
        try { exact = new T[_count]; }
        catch { reservation.Dispose(); throw; }
        try { CopyWithCheckpoints(_items.AsSpan(0, _count), exact); }
        catch { reservation.Dispose(); throw; }
        Dispose();
        return new OwnedSceneArray<T>(exact, reservation);
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
        _items = [];
        _count = 0;
    }

    private void EnsureAdditionalCapacity(int additionalCount)
    {
        if (additionalCount == 0) return;
        int requiredCount;
        try { requiredCount = checked(_count + additionalCount); }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "The SVG scene exceeds the supported array capacity."); }
        if (requiredCount <= _items.Length) return;
        int capacity = _items.Length == 0 ? Math.Max(4, requiredCount) : _items.Length > int.MaxValue / 2 ? requiredCount : Math.Max(_items.Length * 2, requiredCount);
        long bytes = ParserCollectionMemory.GetArrayByteCount<T>(capacity);
        RenderBudget.BudgetReservation replacementReservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
        T[] replacement;
        try { replacement = new T[capacity]; }
        catch { replacementReservation.Dispose(); throw; }
        try { CopyWithCheckpoints(_items.AsSpan(0, _count), replacement); }
        catch { replacementReservation.Dispose(); throw; }
        _reservation?.Dispose();
        _reservation = replacementReservation;
        _items = replacement;
    }

    private void CopyWithCheckpoints(ReadOnlySpan<T> source, Span<T> destination)
    {
        for (int offset = 0; offset < source.Length; offset += CopyCheckpointItems)
        {
            _workCounter.Consume(_cancellationToken);
            int count = Math.Min(CopyCheckpointItems, source.Length - offset);
            source.Slice(offset, count).CopyTo(destination.Slice(offset, count));
        }
    }
}

internal readonly record struct OwnedSceneArray<T>(T[] Items, RenderBudget.BudgetReservation? Reservation) where T : struct
{
    internal void Dispose() => Reservation?.Dispose();
}
