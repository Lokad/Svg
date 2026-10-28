namespace Lokad.Svg.Parsing;

using Lokad.Svg.Fonts;
using Lokad.Svg.Parsing;
using Lokad.Svg.Rendering;
using System.Runtime.CompilerServices;

/// <summary>Represents the parsed root viewport dimensions before scene construction.</summary>
internal readonly record struct SvgRoot(SvgRootLength? Width, SvgRootLength? Height, SvgViewBox? ViewBox, SvgGeometry[] Geometries, SvgDrawCommand[] Commands, bool HasUnsupportedChild, long ParserWorkUnits, SvgSceneStorage? SceneStorage) : IDisposable
{
    internal SvgSourceDiagnostic? UnsupportedDiagnostic { get; init; }

    /// <summary>Borrows this scene's sealed bindings; child arenas separately borrow document-owned definitions.</summary>
    internal SvgFilterArena? FilterResources => SceneStorage?.Filters;

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

internal enum SvgGeometryKind { Invalid, Rectangle, AxisAlignedLine, OrthogonalBand, GlyphContours, PatternFill, GradientFill, GradientStroke, PolygonFill, RadialGradient, GradientPolygon, RadialPolygon, PatternPolygon, EmbeddedImage }

/// <summary>Owns the reference payload of one tagged scene geometry.</summary>
/// <remarks>Implementors retain their contour/paint/resource storage until scene disposal.
/// Borrowers must finish first; disposal must release owned reservations idempotently.
/// Construction, mutation and disposal belong to the conversion thread.</remarks>
internal interface ISvgGeometryContent : IDisposable { }

/// <summary>Represents retained scene geometry independently from its raster fast path.</summary>
internal readonly struct SvgGeometry
{
    /// <summary>Borrows a parser-owned element mask chain until final command emission clears it before scene transfer.</summary>
    internal SvgLeafMaskBinding? LeafMask { get; init; }
    internal SvgRecordedPaint RecordedPaint { get; init; }
    internal SvgRecordedPaint RecordedStrokePaint { get; init; }
    /// <summary>Gets source paint only for native solid rectangle draws without an opacity layer. Generated bands, images and paint-server fallbacks remain distinct.</summary>
    internal SvgSolidPaint? SolidPaint { get; init; }

    private readonly SvgGeometryKind _kind;
    private readonly SvgRectangle _rectangle;
    private readonly SvgAxisAlignedLine _axisAlignedLine;
    private readonly ISvgGeometryContent? _content;

    private SvgGeometry(SvgGeometryKind kind, SvgRectangle rectangle)
    {
        _kind = kind;
        _rectangle = rectangle;
    }

    private SvgGeometry(SvgAxisAlignedLine axisAlignedLine)
    {
        _kind = SvgGeometryKind.AxisAlignedLine;
        _axisAlignedLine = axisAlignedLine;
    }

    private SvgGeometry(SvgGeometryKind kind, ISvgGeometryContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _kind = kind;
        _content = content;
    }

    private T GetContent<T>(SvgGeometryKind kind, string message) where T : class, ISvgGeometryContent
        => _kind == kind && _content is T value ? value : throw new InvalidOperationException(message);

    internal SvgGeometryKind Kind => _kind;

    /// <summary>Owns authored fill or stroke-centerline commands for final mapping; scene contours retain bounds/paint semantics.</summary>
    internal SvgAuthoredPath? AuthoredPath { get; init; }

    /// <summary>Transfers authored ownership when stamping or repainting the same contours; disposal of the dropped geometry remains safe.</summary>
    internal SvgGeometry TransferAuthoredPathTo(SvgGeometry replacement)
    {
        if (replacement.AuthoredPath is not null) throw new InvalidOperationException("Replacement already owns authored geometry.");
        return AuthoredPath is null ? replacement : replacement with { AuthoredPath = AuthoredPath.Transfer() };
    }

    /// <summary>Copies authored ownership to a replacement geometry, composing its additional scene placement.</summary>
    internal SvgGeometry CopyAuthoredPathTo(SvgGeometry replacement, SvgAffineTransform placement, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (replacement.AuthoredPath is not null) throw new InvalidOperationException("Replacement already owns authored geometry.");
        if (AuthoredPath is null) return replacement;
        try { return replacement with { AuthoredPath = AuthoredPath.Copy(placement, budget, workCounter, cancellationToken) }; }
        catch { replacement.Dispose(); throw; }
    }


    internal SvgRectangle Rectangle
        => _kind is SvgGeometryKind.Rectangle or SvgGeometryKind.OrthogonalBand
            ? _rectangle
            : throw new InvalidOperationException("This SVG geometry does not contain a rectangle.");

    internal SvgAxisAlignedLine AxisAlignedLine
        => _kind == SvgGeometryKind.AxisAlignedLine
            ? _axisAlignedLine
            : throw new InvalidOperationException("This SVG geometry does not contain an axis-aligned line.");

    internal SvgGlyphGeometry Glyph
        => GetContent<SvgGlyphGeometry>(SvgGeometryKind.GlyphContours, "This SVG geometry does not contain glyph contours.");

    internal static SvgGeometry FromRectangle(SvgRectangle rectangle) => new(SvgGeometryKind.Rectangle, rectangle);

    internal static SvgGeometry FromImage(SvgImageGeometry image) => new(SvgGeometryKind.EmbeddedImage, image);

    internal SvgImageGeometry Image
        => GetContent<SvgImageGeometry>(SvgGeometryKind.EmbeddedImage, "This SVG geometry does not contain decoded image pixels.");

    internal static SvgGeometry FromAxisAlignedLine(SvgAxisAlignedLine line) => new(line);

    internal static SvgGeometry FromOrthogonalBand(SvgRectangle band) => new(SvgGeometryKind.OrthogonalBand, band);

    internal static SvgGeometry FromGlyphContours(SvgGlyphGeometry glyph) => new(SvgGeometryKind.GlyphContours, glyph);

    internal static SvgGeometry FromPatternFill(SvgPatternGeometry pattern) => new(SvgGeometryKind.PatternFill, pattern);

    internal static SvgGeometry FromGradientFill(SvgGradientGeometry gradient) => new(SvgGeometryKind.GradientFill, gradient);

    internal static SvgGeometry FromGradientStroke(SvgGradientStrokeGeometry gradientStroke) => new(SvgGeometryKind.GradientStroke, gradientStroke);

    internal static SvgGeometry FromRadialFill(SvgRadialGeometry radial) => new(SvgGeometryKind.RadialGradient, radial);

    internal static SvgGeometry FromPolygonFill(SvgPolygonFill polygon) => new(SvgGeometryKind.PolygonFill, polygon);

    internal static SvgGeometry FromGradientPolygon(SvgGradientPolygon polygon) => new(SvgGeometryKind.GradientPolygon, polygon);

    internal static SvgGeometry FromRadialPolygon(SvgRadialPolygon polygon) => new(SvgGeometryKind.RadialPolygon, polygon);

    internal static SvgGeometry FromPatternPolygon(SvgPatternPolygon polygon) => new(SvgGeometryKind.PatternPolygon, polygon);

    internal SvgGradientGeometry Gradient
        => GetContent<SvgGradientGeometry>(SvgGeometryKind.GradientFill, "This SVG geometry does not contain gradient stops.");

    internal SvgPatternGeometry Pattern
        => GetContent<SvgPatternGeometry>(SvgGeometryKind.PatternFill, "This SVG geometry does not contain pattern tile content.");

    internal SvgGradientStrokeGeometry GradientStroke
        => GetContent<SvgGradientStrokeGeometry>(SvgGeometryKind.GradientStroke, "This SVG geometry does not contain gradient stroke bands.");

    internal SvgRadialGeometry Radial
        => GetContent<SvgRadialGeometry>(SvgGeometryKind.RadialGradient, "This SVG geometry does not contain radial stops.");

    internal SvgPolygonFill Polygon
        => GetContent<SvgPolygonFill>(SvgGeometryKind.PolygonFill, "This SVG geometry does not contain polygon contours.");

    internal SvgGradientPolygon GradientPolygon
        => GetContent<SvgGradientPolygon>(SvgGeometryKind.GradientPolygon, "This SVG geometry does not contain gradient polygon contours.");

    internal SvgRadialPolygon RadialPolygon
        => GetContent<SvgRadialPolygon>(SvgGeometryKind.RadialPolygon, "This SVG geometry does not contain radial polygon contours.");

    internal SvgPatternPolygon PatternPolygon
        => GetContent<SvgPatternPolygon>(SvgGeometryKind.PatternPolygon, "This SVG geometry does not contain pattern polygon contours.");

    internal void Dispose()
    {
        AuthoredPath?.Dispose();
        _content?.Dispose();
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
    /// <summary>Authored fill contours replayed in output space with directed winding and retained curves under either fill rule.</summary>
    NativeFillEdges,
}

/// <summary>Retains one solid polygon's scene coordinates, contour storage and memory reservation until the scene is disposed.</summary>
/// <remarks>
/// <para>Points are device-space doubles with the caller transform and viewport shift applied; contour ends
/// partition them exactly and even-odd nesting signs ride alongside when the fill rule needs them. Flattened
/// curves ride the same arrays with a marker, since non-uniform output mapping distorts them while straight
/// edges map exactly. Straight edges map under the rectangle quarter-pixel discipline while flattened curve vertices,
/// never on a calibration grid, map finite. The optional
/// clip box stays in the same space and intersects raster bounds there. Scene storage covers all three arrays
/// exactly; callers charge raster work per evaluated pixel, matching rectangle bands.</para>
/// </remarks>
internal sealed class SvgPolygonFill : ISvgGeometryContent
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

    /// <summary>Per-contour combination weights for normalized fills; authored replay uses directed winding, with unit votes for parity and absent signs for nonzero accumulation.</summary>
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
internal sealed class SvgGradientPolygon : ISvgGeometryContent
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

    /// <summary>Repaints one solid outline as a linear-gradient outline, preserving shader alpha and native constant-paint replay. Callers admit the paint once for identical errors.</summary>
    internal static SvgGeometry RepaintOutline(SvgPolygonFill solid, SvgLinearGradient paint, SvgRectangle box, byte opacity, RenderBudget budget)
    {
        return Adopt(SvgGeometry.FromGradientPolygon(SvgGradientPolygon.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, paint, box, solid.HasFill, solid.IsEvenOdd, opacity, solid.Mapping, solid.Clip, null, budget)));

        SvgGeometry Adopt(SvgGeometry repaint)
        {
            solid.Dispose();
            return repaint;
        }
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
internal sealed class SvgRadialPolygon : ISvgGeometryContent
{
    private RenderBudget.BudgetReservation? _reservation;
    private RenderBudget.BudgetReservation? _paintReservation;

    private SvgRadialPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget.BudgetReservation? paintReservation)
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
        _paintReservation = paintReservation;
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

    /// <summary>Creates one radial outline without recorded curves; spans stay empty on this route. Adopts caller contour storage only on success and reserves retained paint storage separately.</summary>
    internal static SvgRadialPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgRadialGradient gradient, SvgRectangle box, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        return Create(points, contourEnds, signs, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation, budget);
    }

    /// <summary>Creates one radial outline carrying retained spans; the caller reservation covers contour arrays and transfers only on success.</summary>
    /// <remarks>Paint storage is reserved separately before allocation. Without a caller reservation, the factory owns its exact array reservation. Failure releases only factory-owned reservations.</remarks>
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
        RenderBudget.BudgetReservation paintReservation = budget.Reserve(SvgRadialGradient.RetainedStorageBytes, SvgMemoryCategory.Scene);
        bool ownsArrays = reservation is null;
        try
        {
            if (ownsArrays) reservation = bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
            return new SvgRadialPolygon(points, contourEnds, signs, quadSpans, cubicSpans, gradient, box, hasFill, isEvenOdd, opacity, mapping, clip, reservation, paintReservation);
        }
        catch
        {
            paintReservation.Dispose();
            if (ownsArrays) reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Repaints one solid outline as a radial-gradient outline, preserving shader alpha and native constant-paint replay. Callers admit the paint once for identical errors.</summary>
    internal static SvgGeometry RepaintOutline(SvgPolygonFill solid, SvgRadialGradient paint, SvgRectangle box, byte opacity, RenderBudget budget)
    {
        return Adopt(SvgGeometry.FromRadialPolygon(SvgRadialPolygon.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, paint, box, solid.HasFill, solid.IsEvenOdd, opacity, solid.Mapping, solid.Clip, null, budget)));

        SvgGeometry Adopt(SvgGeometry repaint)
        {
            solid.Dispose();
            return repaint;
        }
    }

    /// <summary>Stamps one radial polygon with a mask value inside its mask region, transferring scene ownership to the stamped outline.</summary>
    /// <remarks><para>Contour storage is shared with the source outline and the source reservation transfers to the result. The caller drops the source without disposing it; disposing the dropped source stays a safe no-op.</para></remarks>
    internal SvgRadialPolygon Stamped(byte value, SvgClipBox region)
    {
        byte stamped = SvgReferenceResolver.MultiplyAlpha(Opacity, value);
        SvgClipBox clipped = Clip is null ? region : Clip.Value.Intersect(region);
        RenderBudget.BudgetReservation? reservation = _reservation;
        SvgRadialPolygon result = new(Points, ContourEnds, Signs, QuadSpans, CubicSpans, Gradient, Box, HasFill, IsEvenOdd, stamped, Mapping, clipped, reservation, _paintReservation);
        _reservation = null;
        _paintReservation = null;
        return result;
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
        _paintReservation?.Dispose();
        _paintReservation = null;
    }
}

/// <summary>Owns retained pattern-tile polygon contour arrays with their integer tile grid and scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgPatternPolygon : ISvgGeometryContent
{
    private RenderBudget.BudgetReservation? _reservation;

    private SvgPatternPolygon(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRectangle content, SvgSolidPaint? contentPaint, double originX, double originY, double tileWidth, double tileHeight, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, RenderBudget.BudgetReservation? reservation)
    {
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
        Content = content;
        ContentPaint = contentPaint;
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

    /// <summary>Borrowed vector tile owner; parent scene storage controls its lifetime.</summary>
    internal SvgVectorContent? VectorContent { get; init; }

    internal SvgViewBox? ContentViewBox { get; init; }
    private SvgPatternCoordinates? _coordinates;
    internal SvgPatternCoordinates? Coordinates { get => _coordinates; init => _coordinates = value; }
    internal SvgPatternPaintMatrices? PaintMatrices { get; init; }

    /// <summary>Binds the complete unclipped local object box during parser-thread construction only.</summary>
    internal void BindObjectBox(SvgPaintBounds box)
    {
        if (_coordinates is { ObjectBox: null } coordinates) _coordinates = coordinates with { ObjectBox = box };
    }


    /// <summary>Maps vector tile coordinates into the paint instance; legacy rectangle tiles retain their existing grid.</summary>
    internal SvgAffineTransform ShaderPlacement { get; init; } = SvgAffineTransform.Identity;

    /// <summary>Preserves tile fill opacity before byte quantization; stroke paint retains its independent byte color.</summary>
    internal SvgSolidPaint? ContentPaint { get; }

    internal double OriginX { get; }

    internal double OriginY { get; }

    internal double TileWidth { get; }

    internal double TileHeight { get; }

    internal bool HasFill { get; }

    internal bool IsEvenOdd { get; }

    internal byte Opacity { get; }

    internal SvgPolygonMapping Mapping { get; }

    internal SvgClipBox? Clip { get; }

    /// <summary>Creates one pattern outline carrying retained spans with exact scene storage.</summary>
    /// <summary>Creates an outline borrowing its vector tile from the parent scene; disposal releases only contour storage.</summary>
    internal static SvgPatternPolygon Create(SvgDevicePoint[] points, int[] contourEnds, int[]? signs, SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, SvgRectangle content, SvgSolidPaint? contentPaint, double originX, double originY, double tileWidth, double tileHeight, bool hasFill, bool isEvenOdd, byte opacity, SvgPolygonMapping mapping, SvgClipBox? clip, SvgVectorContent? vectorContent, SvgAffineTransform shaderPlacement, SvgViewBox? contentViewBox, SvgPatternCoordinates? coordinates, SvgPatternPaintMatrices? paintMatrices, RenderBudget budget)
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
        return new SvgPatternPolygon(points, contourEnds, signs, quadSpans, cubicSpans, content, contentPaint, originX, originY, tileWidth, tileHeight, hasFill, isEvenOdd, opacity, mapping, clip, reservation) { VectorContent = vectorContent, ShaderPlacement = shaderPlacement, ContentViewBox = contentViewBox, Coordinates = coordinates, PaintMatrices = paintMatrices };
    }

    /// <summary>Repaints a retained contour while composing placement only for vector tiles; legacy tile pixels keep their established route.</summary>
    internal static SvgGeometry RepaintOutline(SvgPolygonFill solid, SvgPatternTile tile, byte opacity, SvgAffineTransform shaderPlacement, RenderBudget budget)
    {
        if (tile.TileWidth == 0 || tile.TileHeight == 0 || !tile.HasContent) return Adopt(SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(solid.Points, solid.ContourEnds, solid.Signs, default, false, solid.IsEvenOdd, solid.Mapping, solid.Clip, null, budget)));
        return Adopt(SvgGeometry.FromPatternPolygon(SvgPatternPolygon.Create(solid.Points, solid.ContourEnds, solid.Signs, solid.QuadSpans, solid.CubicSpans, tile.Content?.Rectangle ?? default, tile.Content?.SolidPaint, tile.OriginX, tile.OriginY, tile.TileWidth, tile.TileHeight, solid.HasFill, solid.IsEvenOdd, opacity, solid.Mapping, solid.Clip, tile.VectorContent, tile.VectorContent is null ? SvgAffineTransform.Identity : shaderPlacement.ComposeChecked(tile.VectorPlacement), tile.ContentViewBox, tile.Coordinates, tile.VectorContent is null ? null : new(shaderPlacement, tile.VectorPlacement), budget)));

        SvgGeometry Adopt(SvgGeometry repaint)
        {
            solid.Dispose();
            return repaint;
        }
    }

    /// <summary>Stamps one pattern polygon with a mask value inside its mask region, transferring scene ownership to the stamped outline.</summary>
    /// <remarks><para>Contour storage is shared with the source outline and the source reservation transfers to the result. The caller drops the source without disposing it; disposing the dropped source stays a safe no-op.</para></remarks>
    internal SvgPatternPolygon Stamped(byte value, SvgClipBox region)
    {
        byte stamped = SvgReferenceResolver.MultiplyAlpha(Opacity, value);
        SvgClipBox clipped = Clip is null ? region : Clip.Value.Intersect(region);
        RenderBudget.BudgetReservation? reservation = _reservation;
        _reservation = null;
        return new SvgPatternPolygon(Points, ContourEnds, Signs, QuadSpans, CubicSpans, Content, ContentPaint, OriginX, OriginY, TileWidth, TileHeight, HasFill, IsEvenOdd, stamped, Mapping, clipped, reservation) { VectorContent = VectorContent, ShaderPlacement = ShaderPlacement, ContentViewBox = ContentViewBox, Coordinates = Coordinates, PaintMatrices = PaintMatrices };
    }

    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained glyph contour arrays and their scene-memory reservation until the scene is disposed.</summary>
internal sealed class SvgGlyphGeometry : ISvgGeometryContent
{
    private RenderBudget.BudgetReservation? _reservation;

    internal SvgGlyphGeometry(SvgGlyphQuadraticContour[] contours, SvgColor color) : this(contours, color, null, null)
    {
        ArgumentNullException.ThrowIfNull(contours);
    }

    private SvgGlyphGeometry(SvgGlyphQuadraticContour[] contours, SvgColor color, RenderBudget.BudgetReservation? reservation, SvgGlyphRasterSource? source)
    {
        Contours = contours;
        Color = color;
        _reservation = reservation;
        Source = source;
    }

    internal SvgGlyphQuadraticContour[] Contours { get; }

    internal SvgColor Color { get; }

    internal SvgGlyphRasterSource? Source { get; }
    internal SvgTextStrokePaint? StrokePaint { get; private set; }
    internal SvgTextShader? Shader { get; set; }
    internal double StrokeBoundsInflation { get; private set; }

    internal static SvgGlyphGeometry CreateStroke(SvgGlyphContourStorage contours, SvgTextStrokePaint paint,
        SvgPreparedFont font, SvgPositionedGlyph position, double fontSize, SvgAffineTransform glyphMap, RenderBudget budget)
    {
        SvgColor color = paint.Resolve()?.Color ?? throw new InvalidOperationException("A painted text stroke requires a color.");
        SvgGlyphGeometry geometry = Create(contours, color, font, position, fontSize, glyphMap, null, budget);
        geometry.StrokePaint = paint;
        double scale = Math.Max(Math.Sqrt(glyphMap.A * glyphMap.A + glyphMap.B * glyphMap.B), Math.Sqrt(glyphMap.C * glyphMap.C + glyphMap.D * glyphMap.D));
        double strokeScale = paint.NonScaling ? 1 / paint.FontScale : 1;
        geometry.StrokeBoundsInflation = 2 + (paint.Resolve()?.Width ?? 0) * .5 * scale * strokeScale * Math.Max(1, SvgStrokeStyleSyntax.ParseMiterLimit(paint.Style.Miterlimit));
        return geometry;
    }

    internal static SvgGlyphGeometry Create(SvgGlyphContourStorage contours, SvgColor color)
    {
        ArgumentNullException.ThrowIfNull(contours);
        SvgGlyphContourTransfer transfer = contours.TransferOwnership();
        try { return new SvgGlyphGeometry(transfer.Contours, color, transfer.Reservation, null); }
        catch
        {
            transfer.Reservation?.Dispose();
            throw;
        }
    }

    /// <summary>Adopts contours and positioned source metadata, releasing both on failed ownership transfer.</summary>
    internal static SvgGlyphGeometry Create(SvgGlyphContourStorage contours, SvgColor color, SvgPreparedFont font, SvgPositionedGlyph position, double fontSize, SvgAffineTransform glyphMap, double? pictureFontScale, RenderBudget budget)
    {
        if (contours.Contours.Length == 0) return Create(contours, color);
        SvgGlyphRasterSource source = SvgGlyphRasterSource.Create(font, position, fontSize, glyphMap, pictureFontScale, budget);
        try
        {
            SvgGlyphContourTransfer transfer = contours.TransferOwnership();
            try { return new(transfer.Contours, color, transfer.Reservation, source); }
            catch { transfer.Reservation?.Dispose(); throw; }
        }
        catch { source.Dispose(); throw; }
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
        return new SvgGlyphGeometry(contours, color, reservation, null);
    }

    internal static long GetStorageByteCount(int contourCount, int segmentCount)
    {
        try
        {
            return checked(40L + (long)contourCount * Unsafe.SizeOf<SvgGlyphQuadraticContour>() + (long)contourCount * 32L + (long)segmentCount * Unsafe.SizeOf<SvgGlyphQuadraticSegment>());
        }
        catch (OverflowException)
        {
            throw new SvgException(SvgErrorCode.LimitExceeded, "Glyph contour storage exceeds the supported accounting range.");
        }
    }

    public void Dispose()
    {
        Source?.Dispose();
        _reservation?.Dispose();
        _reservation = null;
    }
}

/// <summary>Owns retained pattern tile content with its integer grid and fill region.</summary>
internal sealed class SvgPatternGeometry : ISvgGeometryContent
{
    internal SvgPatternGeometry(SvgRectangle Content, double OriginX, double OriginY, double TileWidth, double TileHeight, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke)
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

    /// <summary>Borrowed vector tile owner; parent scene storage controls its lifetime.</summary>
    internal SvgVectorContent? VectorContent { get; init; }

    internal SvgViewBox? ContentViewBox { get; init; }
    private SvgPatternCoordinates? _coordinates;
    internal SvgPatternCoordinates? Coordinates { get => _coordinates; init => _coordinates = value; }
    internal SvgPatternPaintMatrices? PaintMatrices { get; init; }

    /// <summary>Binds the complete unclipped local object box during parser-thread construction only.</summary>
    internal void BindObjectBox(SvgPaintBounds box)
    {
        if (_coordinates is { ObjectBox: null } coordinates) _coordinates = coordinates with { ObjectBox = box };
    }

    internal double OriginX { get; }

    internal double OriginY { get; }

    internal double TileWidth { get; }

    internal double TileHeight { get; }

    internal int TextureWidth => checked((int)TileWidth);
    internal int TextureHeight => checked((int)TileHeight);
    /// <summary>Only device-mapped vector tiles retain source bounds and native image-shader coordinates.</summary>
    internal SvgViewBox? RasterSourceBox { get; init; }
    internal SvgShaderMatrix? NativeImageMap { get; init; }
    internal SvgShaderMatrix? NativeImageLocal { get; init; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    /// <summary>Maps original tile coordinates into the instance coordinate space; rasterization composes the output map.</summary>
    internal SvgAffineTransform ShaderPlacement { get; init; } = SvgAffineTransform.Identity;

    /// <summary>Preserves tile fill opacity before byte quantization; null uses the retained byte paint.</summary>
    internal SvgSolidPaint? ContentPaint { get; init; }

    internal SvgPatternGeometry ClipTo(SvgClipBox box)
        => new(Content, OriginX, OriginY, TileWidth, TileHeight, box.ClipRectangle(Fill), Opacity, Stroke) { ShaderPlacement = ShaderPlacement, ContentPaint = ContentPaint, VectorContent = VectorContent, ContentViewBox = ContentViewBox, Coordinates = Coordinates, PaintMatrices = PaintMatrices };

    public void Dispose()
    {
    }
}

/// <summary>Owns retained linear-gradient stops with their bounding-box vector and fill region.</summary>
internal sealed class SvgGradientGeometry : ISvgGeometryContent
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

    public void Dispose()
    {
    }
}

/// <summary>Owns retained radial paint-value storage with its circle and fill region; the scene separately reserves shared stops.</summary>
internal sealed class SvgRadialGeometry : ISvgGeometryContent
{
    private RenderBudget.BudgetReservation? _paintReservation;

    private SvgRadialGeometry(SvgRadialGradient Gradient, SvgRectangle Box, SvgRectangle Fill, byte Opacity, SvgStroke? Stroke, RenderBudget.BudgetReservation? paintReservation)
    {
        this.Gradient = Gradient;
        this.Box = Box;
        this.Fill = Fill;
        this.Opacity = Opacity;
        this.Stroke = Stroke;
        _paintReservation = paintReservation;
    }

    /// <summary>Reserves retained paint storage before allocation. The result owns it until disposal or clipping/mask transfer.</summary>
    internal static SvgRadialGeometry Create(SvgRadialGradient gradient, SvgRectangle box, SvgRectangle fill, byte opacity, SvgStroke? stroke, RenderBudget budget)
    {
        RenderBudget.BudgetReservation reservation = budget.Reserve(SvgRadialGradient.RetainedStorageBytes, SvgMemoryCategory.Scene);
        try { return new(gradient, box, fill, opacity, stroke, reservation); }
        catch { reservation.Dispose(); throw; }
    }

    internal SvgRadialGradient Gradient { get; }

    internal SvgRectangle Box { get; }

    internal SvgRectangle Fill { get; }

    internal byte Opacity { get; }

    internal SvgStroke? Stroke { get; }

    /// <summary>Clips the fill and transfers retained paint ownership; callers drop the source.</summary>
    internal SvgRadialGeometry ClipTo(SvgClipBox box)
    {
        SvgRadialGeometry result = new(Gradient, Box, box.ClipRectangle(Fill), Opacity, Stroke, _paintReservation);
        _paintReservation = null;
        return result;
    }

    /// <summary>Stamps alpha and transfers retained paint ownership; callers drop the source.</summary>
    internal SvgRadialGeometry Stamped(byte value)
    {
        SvgStroke? stroke = Stroke is null ? null : new SvgStroke(new SvgColor(Stroke.Value.Color.Red, Stroke.Value.Color.Green, Stroke.Value.Color.Blue, SvgReferenceResolver.MultiplyAlpha(Stroke.Value.Color.Alpha, value)), Stroke.Value.Width);
        SvgRadialGeometry result = new(Gradient, Box, Fill, SvgReferenceResolver.MultiplyAlpha(Opacity, value), stroke, _paintReservation);
        _paintReservation = null;
        return result;
    }

    public void Dispose()
    {
        _paintReservation?.Dispose();
        _paintReservation = null;
    }
}

/// <summary>Retains one solid fill with four gradient-evaluated stroke bands.</summary>
internal sealed class SvgGradientStrokeGeometry : ISvgGeometryContent
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

    public void Dispose()
    {
    }
}

/// <summary>Retains one local definition's typed geometry and the narrow instance-style override capability.</summary>
internal readonly record struct SvgDefinition(SvgGeometry[] Geometries, bool AllowsInstanceFillOverride, double? InstanceStrokeMiterLimit, SvgViewBox? SymbolViewBox)
{
    /// <summary>Retains per-geometry fill inheritance for symbol children, independent of authored stroke channels.</summary>
    internal SvgSymbolFillBinding[]? SymbolFillBindings { get; init; }
    /// <summary>Marks a child-ID alias whose geometry owner is the enclosing symbol or its open builder.</summary>
    internal bool BorrowedFromSymbol { get; init; }
    internal SvgSymbolContentRange? SymbolContent { get; init; }
    internal SvgSymbolFillBinding[]? SourceFillBindings { get; init; }
    internal bool SourceDisplayed { get; init; } = true;
    internal SvgDisplay SourceDisplay { get; init; }
    internal bool? SourceVisible { get; init; }
    internal SvgDefinitionLeafSource? LeafSource { get; init; }
    internal SvgRetainedTextSource? TextSource { get; init; }
    internal string? RenderedGroupId { get; init; }
    internal SvgSymbolContentRange? StrokeContent { get; init; }
    internal SvgMarkerDefinition? Marker { get; init; }
    /// <summary>Distinguishes symbol/marker viewports from reusable groups without a viewBox.</summary>
    internal bool EstablishesViewport { get; init; }

    /// <summary>Reports an empty symbol/marker viewport; ordinary use target dimensions do not suppress their source.</summary>
    internal bool HasEmptyViewport(SvgUseReference reference)
        => (EstablishesViewport || SymbolViewBox is not null) && (reference.Width == 0 || reference.Height == 0);

    internal SvgViewportLengthBases InstanceLengthBases(SvgUseReference reference)
        => SymbolViewBox is {} box ? new(box.Width, box.Height)
            : EstablishesViewport ? new(reference.Width, reference.Height) : reference.LengthBases;
}

/// <summary>Retains a solid fill's independently inherited properties until a symbol or child alias is instanced.
/// Solid strokes retain independently authored or inherited opacity; paint servers keep their separately lowered paint.</summary>
internal readonly record struct SvgSymbolFillBinding(bool IsInherited, SvgPaintOpacity? FillOpacity, SvgSolidPaint? CurrentColor)
{
    internal SvgPaint? AuthoredFill { get; init; }
    internal SvgPaint? AuthoredStroke { get; init; }
    internal SvgPaintOpacity? StrokeOpacity { get; init; }
    internal bool IsStroke { get; init; }
    internal bool IsPaintServer { get; init; }
    internal bool IsStrokePaintServer { get; init; }
    internal bool IsPainted { get; init; } = true;
    internal bool ResolvesFill => IsInherited || AuthoredFill is not null || !IsPaintServer && AuthoredStroke is not null;

    internal SvgPaint Resolve(SvgPaint instancePaint, SvgPaintOpacity inheritedStrokeOpacity)
    {
        if (IsStroke) return ResolveStroke(instancePaint, inheritedStrokeOpacity);
        SvgPaint fill = AuthoredFill ?? instancePaint;
        return fill with { HasFill = fill.HasFill && IsPainted, FillOpacity = FillOpacity ?? instancePaint.FillOpacity, CurrentPaint = CurrentColor ?? instancePaint.CurrentPaint };
    }

    internal SvgPaint ResolveStroke(SvgPaint instancePaint, SvgPaintOpacity inheritedStrokeOpacity)
    {
        SvgPaint stroke = AuthoredStroke ?? throw new InvalidOperationException("Solid stroke binding requires source paint.");
        return stroke with { HasFill = stroke.HasFill && IsPainted, FillOpacity = StrokeOpacity ?? inheritedStrokeOpacity, CurrentPaint = CurrentColor ?? instancePaint.CurrentPaint };
    }
}

internal enum SvgDrawCommandKind { Geometry, BeginGroup, EndGroup }

/// <summary>Separates filter-output clipping, mask allocation regions and geometric viewport clips at layer restore.</summary>
internal enum SvgLayerRegionKind : byte { Mask, Viewport, AffineViewport, Bounds, Filter, FilterOutputClip }

/// <summary>References retained geometry or marks an isolated layer; mask alpha applies before the separate outer opacity.</summary>
internal readonly record struct SvgDrawCommand(SvgDrawCommandKind Kind, int GeometryIndex, SvgPaintOpacity SourceOpacity, SvgClipBox? MaskRegion, int MaskContentIndex, byte MaskAlpha)
{
    internal SvgLayerRegionKind RegionKind { get; init; }
    internal SvgDrawCommand(SvgDrawCommandKind kind, int geometryIndex, byte opacity, SvgClipBox? maskRegion, int maskContentIndex, byte maskAlpha)
        : this(kind, geometryIndex, new SvgPaintOpacity(opacity, opacity / 255f), maskRegion, maskContentIndex, maskAlpha) { }
    internal byte Opacity => SourceOpacity.Alpha;
    internal SvgRecordingScope RecordingScope => new(Kind == SvgDrawCommandKind.BeginGroup ? GeometryIndex : -1);
}

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
    private readonly OwnedSceneArray<SvgRecordedPaintValue> _recordedPaintValues;
    private readonly OwnedSceneArray<SvgVectorContentOwner> _vectorContents;
    private readonly RenderBudget.BudgetReservation _recordedPaintOwner;
    internal SvgRecordedPaintValue[] RecordedPaintValues => _recordedPaintValues.Items;
    internal SvgFilterArena? Filters { get; }

    private SvgSceneStorage(SvgGeometry[] geometries, RenderBudget.BudgetReservation? geometryReservation, SvgDrawCommand[] commands, RenderBudget.BudgetReservation? commandReservation, List<RenderBudget.BudgetReservation> paintReservations, SvgMaskContent[] maskContents, RenderBudget.BudgetReservation? maskContentReservation, List<RenderBudget.BudgetReservation> maskReservations, OwnedSceneArray<SvgRecordedPaintValue> recordedPaintValues, RenderBudget.BudgetReservation recordedPaintOwner, OwnedSceneArray<SvgVectorContentOwner> vectorContents, SvgFilterArena? filters)
    {
        Filters = filters;
        _recordedPaintValues = recordedPaintValues;
        _vectorContents = vectorContents;
        _recordedPaintOwner = recordedPaintOwner;
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

    internal static SvgSceneStorage Create(BudgetedSceneList<SvgGeometry> geometries, BudgetedSceneList<SvgDrawCommand> commands, List<RenderBudget.BudgetReservation> paintReservations, BudgetedSceneList<SvgMaskContent> maskContents, List<RenderBudget.BudgetReservation> maskReservations, BudgetedSceneList<SvgRecordedPaintValue> recordedPaintValues, RenderBudget.BudgetReservation recordedPaintOwner, BudgetedSceneList<SvgVectorContentOwner>? vectorContents, SvgFilterArena? filters)
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
                try
                {
                    OwnedSceneArray<SvgRecordedPaintValue> ownedPaints = recordedPaintValues.Detach();
                    try
                    {
                        OwnedSceneArray<SvgVectorContentOwner> ownedVectors = vectorContents is null ? new([], null) : vectorContents.Detach();
                        try
                        {
                            return new SvgSceneStorage(ownedGeometries.Items, ownedGeometries.Reservation, ownedCommands.Items, ownedCommands.Reservation, paintReservations, ownedMasks.Items, ownedMasks.Reservation, maskReservations, ownedPaints, recordedPaintOwner, ownedVectors, filters);
                        }
                        catch
                        {
                            foreach (SvgVectorContentOwner content in ownedVectors.Items) content.Dispose();
                            ownedVectors.Dispose();
                            throw;
                        }
                    }
                    catch { ownedPaints.Dispose(); throw; }
                }
                catch
                {
                    foreach (SvgMaskContent content in ownedMasks.Items) foreach (SvgGeometry geometry in content.Geometries) geometry.Dispose();
                    ownedMasks.Dispose();
                    throw;
                }
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
        _recordedPaintValues.Dispose();
        _recordedPaintOwner.Dispose();
        foreach (SvgVectorContentOwner content in _vectorContents.Items) content.Dispose();
        _vectorContents.Dispose();
        Filters?.Dispose();
    }
}

/// <summary>Owns the mutable, budgeted scene construction phase until arrays transfer to scene storage.</summary>
internal sealed class SvgSceneBuilder : IDisposable
{
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _workCounter;
    private readonly CancellationToken _cancellationToken;
    private readonly BudgetedSceneList<SvgGeometry> _geometries;
    private BudgetedSceneList<SvgDrawCommand> _commands;
    private List<RenderBudget.BudgetReservation> _paintReservations = new();
    private readonly BudgetedSceneList<SvgMaskContent> _maskContents;
    private List<RenderBudget.BudgetReservation> _maskReservations = new();
    private readonly HashSet<SvgGradientStop[]> _reservedStopArrays = new();
    private bool _built;
    private bool _hasAuthoredGroupScopes;
    private readonly BudgetedSceneList<SvgRecordedPaintValue> _recordedPaintValues;
    internal const long RecordedPaintOwnerBytes = 192;
    private RenderBudget.BudgetReservation? _recordedPaintOwner;
    private BudgetedSceneList<SvgVectorContentOwner>? _vectorContents;
    private SvgFilterArena? _filters;
    internal SvgFilterArena Filters => _filters ??= SvgFilterArena.Create(_budget, _workCounter, _cancellationToken);

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
        _recordedPaintOwner = budget.Reserve(RecordedPaintOwnerBytes, SvgMemoryCategory.Scene);
        try { _recordedPaintValues = new(budget, workCounter, cancellationToken); }
        catch { _recordedPaintOwner.Dispose(); throw; }
    }

    /// <summary>Adopts an immutable vector-scene owner once; a failed insertion leaves disposal to the caller.</summary>
    internal void AdoptVectorContent(SvgVectorContent content)
    {
        _vectorContents ??= new(_budget, _workCounter, _cancellationToken);
        _vectorContents.Add(new(content));
    }

    internal void AddGeometry(SvgGeometry geometry)
    {
        try
        {
            ReserveGradientStops(geometry);
            int geometryIndex = _geometries.Count;
            _geometries.Add(geometry);
            _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, geometryIndex, 255, null, -1, 255));
        }
        catch { geometry.Dispose(); throw; }
    }

    internal SvgRecordedPaint RegisterRecordedPaint(SvgRecordedPaintValue value)
    {
        _workCounter.Consume(_cancellationToken);
        _recordedPaintValues.Add(value);
        return new(_recordedPaintValues.Count);
    }

    internal SvgRecordedPaintValue GetRecordedPaintValue(SvgRecordedPaint reference)
    {
        if (reference.Identity == 0) return default;
        if ((uint)(reference.Identity - 1) >= (uint)_recordedPaintValues.Count) throw new InvalidOperationException("Paint reference is outside the scene arena.");
        return _recordedPaintValues[reference.Identity - 1];
    }

    /// <summary>Reserves one immutable recording boundary before publishing it to source contexts.</summary>
    /// <remarks>The built scene owns the reservation, including failure/disposal paths. Parent nodes
    /// remain borrowed from the same document's scene owners. Construction is parser-thread confined;
    /// native mapping charges shared work and cancellation before allocation.</remarks>
    internal SvgRecordedTransform? RetainRecordedTransform(SvgRecordedTransform? parent, SvgAffineTransform map, SvgViewportFrame? viewport)
    {
        _workCounter.Consume(_cancellationToken);
        // Recording metadata retains finite computed intermediates; geometry
        // admission owns coordinate-range checks after child maps compose.
        if (viewport is {} frame)
            map = SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY).Compose(map);
        SvgAffineTransform previous = parent?.Map ?? SvgAffineTransform.Identity;
        // The first identity boundary still separates an authored viewBox map
        // from transforms accumulated inside its clip/opacity/mask scope.
        if (parent is not null && map == previous) return parent;
        if (!previous.TryInvert(out SvgAffineTransform inverse)) return parent;
        SvgAffineTransform local = inverse.Compose(map);
        RenderBudget.BudgetReservation reservation = _budget.Reserve(SvgRecordedTransform.RetainedStorageBytes, SvgMemoryCategory.Scene);
        try
        {
            SvgRecordedTransform result = new(parent, map, local);
            _paintReservations.Add(reservation);
            return result;
        }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Reserves and retains one immutable linear source or referencing map under this scene.</summary>
    internal SvgLinearPaintMap RetainLinearPaintMap(SvgLinearPaintMapData data)
    {
        RenderBudget.BudgetReservation reservation = _budget.Reserve(SvgLinearPaintMap.RetainedStorageBytes, SvgMemoryCategory.Scene);
        try
        {
            SvgLinearPaintMap map = new(data);
            _paintReservations.Add(reservation);
            return map;
        }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Reserves immutable radial definition coordinates separately from mapped paint storage.</summary>
    internal SvgRadialGradientSource RetainRadialSource(SvgRadialTemplate template)
    {
        RenderBudget.BudgetReservation reservation = _budget.Reserve(SvgRadialGradientSource.RetainedStorageBytes, SvgMemoryCategory.Scene);
        try
        {
            SvgRadialGradientSource source = new(template.CenterX, template.CenterY, template.Radius,
                template.HasFocalX ? template.FocalX : template.CenterX, template.HasFocalY ? template.FocalY : template.CenterY,
                template.FocalRadius, template.HasTransform ? template.Transform : SvgGradientTransform.Identity,
                template.HasFocalX, template.HasFocalY, template.HasFocalRadius, template.PercentageFocalFields);
            _paintReservations.Add(reservation);
            return source;
        }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Shares one immutable text shader across a fragment's glyphs under scene ownership.</summary>
    /// <remarks>Reserves aggregate metadata and stop storage before publication. The scene releases
    /// storage on failure/disposal; the value borrows no parser state and is safe for concurrent draws.</remarks>
    internal SvgTextShader RetainTextShader(SvgLinearGradient gradient)
    {
        RenderBudget.BudgetReservation reservation = _budget.Reserve(64L + Unsafe.SizeOf<SvgLinearGradient>(), SvgMemoryCategory.Scene);
        try
        {
            SvgTextLinearShader paint = new(gradient);
            ReserveGradientStopArray(gradient.Stops);
            _paintReservations.Add(reservation);
            return paint;
        }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Retains a bound radial text paint with its immutable stop array under scene ownership.</summary>
    internal SvgTextShader RetainTextShader(SvgRadialGradient gradient)
    {
        RenderBudget.BudgetReservation reservation = _budget.Reserve(SvgRadialGradient.RetainedStorageBytes, SvgMemoryCategory.Scene);
        try
        {
            SvgTextRadialShader paint = new(gradient);
            ReserveGradientStopArray(gradient.Stops);
            _paintReservations.Add(reservation);
            return paint;
        }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Retains aggregate pattern metadata; vector content remains borrowed from the parent-owned tile.</summary>
    internal SvgTextShader RetainTextShader(SvgPatternGeometry pattern)
    {
        RenderBudget.BudgetReservation reservation = _budget.Reserve(512, SvgMemoryCategory.Scene);
        try
        {
            SvgTextPatternShader paint = new(pattern);
            _paintReservations.Add(reservation);
            return paint;
        }
        catch { reservation.Dispose(); throw; }
    }

    internal void ChangeRecordedPaint(SvgRecordedPaint reference, SvgRecordedPaintValue value)
    {
        _workCounter.Consume(_cancellationToken);
        if ((uint)(reference.Identity - 1) >= (uint)_recordedPaintValues.Count) throw new InvalidOperationException("Paint mutation is outside its owned arena.");
        _recordedPaintValues[reference.Identity - 1] = value;
    }

    internal int GeometryCount => _geometries.Count;

    internal SvgGeometry GeometryAt(int index) => _geometries[index];

    internal int CommandCount => _commands.Count;

    internal SvgDrawCommand CommandAt(int index) => _commands[index];

    internal void ReplaceCommand(int index, SvgDrawCommand command) => _commands[index] = command;

    /// <summary>Updates borrowed metadata without disposing or transferring the geometry's native owners.</summary>
    internal void AssignRecordedGeometry(int index, SvgGeometry geometry) => _geometries[index] = geometry;
    internal void BeginSourceOpacityScope(SvgPaintOpacity opacity, SvgRecordingScope scope)
    {
        _hasAuthoredGroupScopes = true;
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, scope.Value, opacity, null, -1, 255));
    }
    internal void AssignLeafMask(int index, SvgLeafMaskBinding? binding) => _geometries[index] = _geometries[index] with { LeafMask = binding };

    /// <summary>Resolves filter-owned output clips before the shared command emitter consumes their borrowed operations.</summary>
    internal void ResolveFilterOutputClips(SvgDocumentState state) => _filters?.ResolveOutputClips(state, this);

    /// <summary>Adopts reserved replacement commands, releasing the former array; the caller must stop disposing the replacement.</summary>
    internal void AdoptCommands(BudgetedSceneList<SvgDrawCommand> commands)
    {
        _commands.Dispose();
        _commands = commands;
    }

    internal void InsertCommands(int index, ReadOnlySpan<SvgDrawCommand> commands) => _commands.Insert(index, commands);

    /// <summary>Replaces one retained scene geometry with its clipped successor, keeping commands and releasing replaced storage.</summary>
    internal void ReplaceGeometry(int index, SvgGeometry geometry)
    {
        geometry = geometry with { LeafMask = _geometries[index].LeafMask, RecordedPaint = _geometries[index].RecordedPaint, RecordedStrokePaint = _geometries[index].RecordedStrokePaint };
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
            SvgGeometryKind.GradientPolygon => geometry.GradientPolygon.Gradient.Stops,
            SvgGeometryKind.RadialPolygon => geometry.RadialPolygon.Gradient.Stops,
            _ => null,
        };
        if (stops is not null) ReserveGradientStopArray(stops);
    }

    private void ReserveGradientStopArray(SvgGradientStop[] stops)
    {
        if (stops.Length == 0 || !_reservedStopArrays.Add(stops)) return;
        _paintReservations.Add(_budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGradientStop>(stops.Length), SvgMemoryCategory.Scene));
    }

    internal void AddGeometries(ReadOnlySpan<SvgGeometry> geometries)
    {
        try
        {
            foreach (SvgGeometry geometry in geometries) ReserveGradientStops(geometry);
            int firstGeometryIndex = _geometries.Count;
            _geometries.AddRange(geometries);
            for (int index = 0; index < geometries.Length; index++) _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, firstGeometryIndex + index, 255, null, -1, 255));
        }
        catch { foreach (SvgGeometry geometry in geometries) geometry.Dispose(); throw; }
    }

    /// <summary>Reserves drawing order for a leaf whose local paint is defined later.</summary>
    internal int AddPaintPlaceholder()
    {
        int position = _commands.Count;
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, -1, 255, null, -1, 255));
        return position;
    }

    internal SvgSceneSlot AddReferencePlaceholder()
    {
        SvgSceneSlot slot = new(_geometries.Count, _commands.Count);
        _geometries.Add(default);
        _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.Geometry, slot.GeometryIndex, 255, null, -1, 255));
        return slot;
    }

    /// <summary>Adopts a nonempty resolved reference batch into stable slots, returning the first extra geometry index.</summary>
    /// <remarks>Adopts the input owners on entry. On failure the scene keeps adopted geometries and this method
    /// releases the remainder. Geometry growth charges work/storage and observes cancellation. The caller
    /// binds empty references separately and batches extra commands; one geometry needs no command expansion. Single-threaded.</remarks>
    internal int AdoptReferenceGeometries(SvgSceneSlot slot, ReadOnlySpan<SvgGeometry> applied)
    {
        int adopted = 0;
        try
        {
            foreach (SvgGeometry geometry in applied) ReserveGradientStops(geometry);
            _geometries[slot.GeometryIndex] = applied[0];
            adopted = 1;
            _commands[slot.CommandIndex] = new SvgDrawCommand(SvgDrawCommandKind.Geometry, slot.GeometryIndex, 255, null, -1, 255);
            if (applied.Length == 1) return -1;
            int firstGeometryIndex = _geometries.Count;
            for (; adopted < applied.Length; adopted++)
            {
                _workCounter.Consume(_cancellationToken);
                _geometries.Add(applied[adopted]);
            }
            return firstGeometryIndex;
        }
        catch { for (int index = adopted; index < applied.Length; index++) applied[index].Dispose(); throw; }
    }

    internal void EndOpacityScope() => _commands.Add(new SvgDrawCommand(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255));

    /// <summary>Retains placed paint-clip metadata without materializing a scalar mask's pixels.</summary>
    /// <remarks>The scene owns the new slot and any copied contours. Existing registration reserves
    /// storage and charges work/cancellation; borrowed vector content keeps its original owner.</remarks>
    internal int BindMaskPaintRegion(int index, SvgClipBox clip)
    {
        SvgMaskContent content = index < 0 ? new([], 255, true) { ConstantAlpha = 255 } : _maskContents[index];
        return AddMaskContent(content with { SourcePaintRegion = clip });
    }

    /// <summary>Copies one mask content evaluation under scene ownership, returning its command index.</summary>
    internal int AddMaskContent(SvgMaskContent content)
    {
        if (content.VectorContent is not null || content.Geometries.Length == 0)
        {
            int borrowedIndex = _maskContents.Count;
            _maskContents.Add(content);
            return borrowedIndex;
        }
        RenderBudget.BudgetReservation arrayReservation = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(content.Geometries.Length), SvgMemoryCategory.Scene);
        SvgGeometry[] copied = [];
        try
        {
            copied = new SvgGeometry[content.Geometries.Length];
            for (int index = 0; index < content.Geometries.Length; index++)
            {
                _workCounter.Consume(_cancellationToken);
                if (content.Geometries[index].Kind != SvgGeometryKind.PolygonFill) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only solid mask content is implemented.");
                SvgPolygonFill polygon = content.Geometries[index].Polygon;
                long bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(polygon.Points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(polygon.ContourEnds.Length) + (polygon.Signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(polygon.Signs.Length)) + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(polygon.QuadSpans.Length) + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(polygon.CubicSpans.Length));
                RenderBudget.BudgetReservation reservation = _budget.Reserve(bytes, SvgMemoryCategory.Scene);
                try
                {
                    SvgDevicePoint[] points = new SvgDevicePoint[polygon.Points.Length];
                    int[] ends = new int[polygon.ContourEnds.Length];
                    SvgQuadSpan[] quadSpans = new SvgQuadSpan[polygon.QuadSpans.Length];
                    SvgCubicSpan[] cubicSpans = new SvgCubicSpan[polygon.CubicSpans.Length];
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
                    for (int span = 0; span < quadSpans.Length; span++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        quadSpans[span] = polygon.QuadSpans[span];
                    }
                    for (int span = 0; span < cubicSpans.Length; span++)
                    {
                        _workCounter.Consume(_cancellationToken);
                        cubicSpans[span] = polygon.CubicSpans[span];
                    }
                    copied[index] = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, ends, signs, quadSpans, cubicSpans, polygon.Color, polygon.HasFill, polygon.IsEvenOdd, polygon.Mapping, polygon.Clip, reservation, _budget));
                }
                catch
                {
                    reservation.Dispose();
                    throw;
                }
                copied[index] = content.Geometries[index].CopyAuthoredPathTo(copied[index], SvgAffineTransform.Identity, _budget, _workCounter, _cancellationToken);
            }
            int contentIndex = _maskContents.Count;
            _maskContents.Add(new SvgMaskContent(copied, content.Luminance, content.IsAlpha) { ClipKind = content.ClipKind, BoundsClipContentIndex = content.BoundsClipContentIndex, DrawMap = content.DrawMap });
            _maskReservations.Add(arrayReservation);
            return contentIndex;
        }
        catch
        {
            foreach (SvgGeometry geometry in copied) geometry.Dispose();
            arrayReservation.Dispose();
            throw;
        }
    }

    internal SvgSceneStorage Build()
    {
        if (_built) throw new InvalidOperationException("The SVG scene has already been built.");
        if (_hasAuthoredGroupScopes) RemoveEmptyGroups();
        if (_filters is { HasGroupSources: true } filters) ResolveGroupSources(filters);
        _filters?.Seal();
        _built = true;
        SvgSceneStorage storage = SvgSceneStorage.Create(_geometries, _commands, _paintReservations, _maskContents, _maskReservations, _recordedPaintValues, _recordedPaintOwner ?? throw new InvalidOperationException("Paint arena has already transferred."), _vectorContents, _filters);
        _filters = null;
        _recordedPaintOwner = null;
        _paintReservations = new();
        _maskReservations = new();
        return storage;

        void ResolveGroupSources(SvgFilterArena filters)
        {
            int depth = 0;
            using BudgetedParserList<SvgFilterSourceScope> sources = new(_budget, SvgMemoryCategory.Parser, _workCounter, _cancellationToken);
            foreach (SvgDrawCommand command in _commands.Items)
            {
                _workCounter.Consume(_cancellationToken);
                if (command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    depth++;
                    if (command.RegionKind == SvgLayerRegionKind.Filter)
                        sources.Add(new(command.MaskContentIndex, depth, null, null));
                }
                else if (command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (sources.Count > 0 && sources[^1].Depth == depth)
                    {
                        SvgFilterSourceScope source = sources[^1];
                        sources.RemoveLast();
                        if (filters.IsGroupSource(source.Binding))
                            filters.SetGroupBounds(source.Binding, source.ObjectBounds ?? default, source.VisualBounds ?? source.ObjectBounds ?? default);
                        if (source.ObjectBounds is {} box) Include(box, source.VisualBounds ?? box);
                    }
                    depth--;
                }
                else if (sources.Count > 0 && command.Kind == SvgDrawCommandKind.Geometry)
                {
                    SvgGeometry geometry = _geometries[command.GeometryIndex];
                    SvgRecordedPaintValue paint = GetRecordedPaintValue(geometry.RecordedPaint);
                    if (paint.IsEffectAnchor && paint.ObjectBounds is null) continue;
                    if (paint.ObjectBounds is not {} box) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group filter descendants require retained source bounds.");
                    Include(paint.IsMarkerContent ? null : box, paint.IsEffectAnchor ? null : paint.VisualBounds ?? box);
                }
            }
            if (sources.Count > 0 || depth != 0) throw new SvgException(SvgErrorCode.MalformedInput, "Filter group source is unbalanced.");

            void Include(SvgPaintBounds? box, SvgPaintBounds? visual)
            {
                if (sources.Count == 0) return;
                SvgFilterSourceScope parent = sources[^1];
                sources[^1] = parent with
                {
                    ObjectBounds = box is {} objects ? Union(parent.ObjectBounds, objects) : parent.ObjectBounds,
                    VisualBounds = visual is {} painted ? Union(parent.VisualBounds, painted) : parent.VisualBounds
                };
            }

            static SvgPaintBounds Union(SvgPaintBounds? previous, SvgPaintBounds next) => previous is {} box
                ? new(Math.Min(box.Left, next.Left), Math.Min(box.Top, next.Top), Math.Max(box.Right, next.Right), Math.Max(box.Bottom, next.Bottom)) : next;
        }

        void RemoveEmptyGroups()
        {
            // Deferred resource bindings need authored boundaries until resolution
            // finishes. Empty filter scopes can generate paint from transparent
            // input; retain them and their enclosing composition scopes.
            int retained = 0;
            for (int index = 0; index < _commands.Count; index++)
            {
                _workCounter.Consume(_cancellationToken);
                SvgDrawCommand command = _commands[index];
                if (command.Kind == SvgDrawCommandKind.EndGroup && retained > 0
                    && _commands[retained - 1].Kind == SvgDrawCommandKind.BeginGroup
                    && _commands[retained - 1].RegionKind != SvgLayerRegionKind.Filter)
                    retained--;
                else _commands[retained++] = command;
            }
            _commands.Truncate(retained);
        }
    }

    public void Dispose()
    {
        foreach (SvgGeometry geometry in _geometries.Items) geometry.Dispose();
        _geometries.Dispose();
        _commands.Dispose();
        _recordedPaintValues.Dispose();
        _recordedPaintOwner?.Dispose();
        if (_vectorContents is {} patterns)
        {
            foreach (SvgVectorContentOwner content in patterns.Items) content.Dispose();
            patterns.Dispose();
        }
        foreach (RenderBudget.BudgetReservation reservation in _paintReservations) reservation.Dispose();
        _paintReservations.Clear();
        foreach (SvgMaskContent content in _maskContents.Items)
            foreach (SvgGeometry geometry in content.Geometries) geometry.Dispose();
        _maskContents.Dispose();
        foreach (RenderBudget.BudgetReservation reservation in _maskReservations) reservation.Dispose();
        _maskReservations.Clear();
        _filters?.Dispose();
    }
}

/// <summary>Retains one bounded filter source while scene commands aggregate descendant geometry.</summary>
internal readonly record struct SvgFilterSourceScope(int Binding, int Depth, SvgPaintBounds? ObjectBounds, SvgPaintBounds? VisualBounds);

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

    /// <summary>Drops a compacted tail without releasing borrowed resource owners; array storage remains reserved until detach or disposal.</summary>
    internal void Truncate(int count)
    {
        if ((uint)count > (uint)_count) throw new ArgumentOutOfRangeException(nameof(count));
        for (int offset = count; offset < _count; offset += CopyCheckpointItems)
        {
            _workCounter.Consume(_cancellationToken);
            _items.AsSpan(offset, Math.Min(CopyCheckpointItems, _count - offset)).Clear();
        }
        _count = count;
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
