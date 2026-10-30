using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains immutable polygon arrays under one scene-storage charge, independently of paint and mapping.</summary>
/// <remarks>
/// Copies borrow the same arrays and reservation. A receiving paint binding must be constructed successfully
/// before the previous binding relinquishes ownership. Only the final owner disposes the reservation;
/// construction, transfer and disposal belong to the conversion thread. No cancellation work occurs here.
/// </remarks>
internal struct SvgPolygonContours : IDisposable
{
    private RenderBudget.BudgetReservation? _reservation;

    /// <summary>Adopts a caller reservation, or reserves the five arrays exactly; caller ownership transfers only on successful construction.</summary>
    internal SvgPolygonContours(SvgDevicePoint[] points, int[] contourEnds, int[]? signs,
        SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, bool isEvenOdd,
        RenderBudget.BudgetReservation? reservation, RenderBudget budget)
    {
        Validate(points, contourEnds, signs, quadSpans, cubicSpans, isEvenOdd, budget);
        _reservation = reservation ?? ReserveStorage(points, contourEnds, signs, quadSpans, cubicSpans, budget);
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
    }

    /// <summary>Reserves a retained payload before allocating mapped arrays; the caller releases on failure or hands the charge to a completed binding.</summary>
    /// <remarks>Lengths describe the receiving binding. Borrowed topology remains charged there because it can outlive the source definition; radial paint is separate.</remarks>
    internal static RenderBudget.BudgetReservation? ReserveStorage(ReadOnlySpan<SvgDevicePoint> points,
        ReadOnlySpan<int> contourEnds, ReadOnlySpan<int> signs, ReadOnlySpan<SvgQuadSpan> quadSpans,
        ReadOnlySpan<SvgCubicSpan> cubicSpans, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        long bytes;
        try
        {
            bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length)
                + ParserCollectionMemory.GetArrayByteCount<int>(contourEnds.Length)
                + ParserCollectionMemory.GetArrayByteCount<int>(signs.Length)
                + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length)
                + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length));
        }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        return bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene);
    }

    internal SvgDevicePoint[] Points { get; }
    internal int[] ContourEnds { get; }
    internal int[]? Signs { get; }
    internal SvgQuadSpan[] QuadSpans { get; }
    internal SvgCubicSpan[] CubicSpans { get; }

    /// <summary>Validates before any separate paint reservation, preserving caller ownership on rejected input.</summary>
    internal static void Validate(SvgDevicePoint[] points, int[] contourEnds, int[]? signs,
        SvgQuadSpan[] quadSpans, SvgCubicSpan[] cubicSpans, bool isEvenOdd, RenderBudget budget)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(contourEnds);
        ArgumentNullException.ThrowIfNull(quadSpans);
        ArgumentNullException.ThrowIfNull(cubicSpans);
        ArgumentNullException.ThrowIfNull(budget);
        if (isEvenOdd) ArgumentNullException.ThrowIfNull(signs);
    }

    /// <summary>Clears this value's ownership while arrays remain readable; the original owner or a receiving binding must keep their charge alive.</summary>
    internal void RelinquishOwnership() => _reservation = null;

    /// <summary>Releases this owner's charge exactly once, after borrowers finish; leaves immutable array metadata readable.</summary>
    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}
