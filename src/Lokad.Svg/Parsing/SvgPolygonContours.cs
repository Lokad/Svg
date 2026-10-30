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
        long bytes;
        try
        {
            bytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length)
                + ParserCollectionMemory.GetArrayByteCount<int>(contourEnds.Length)
                + (signs is null ? 0 : ParserCollectionMemory.GetArrayByteCount<int>(signs.Length))
                + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(quadSpans.Length)
                + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(cubicSpans.Length));
        }
        catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Polygon fill storage exceeds the supported accounting range."); }
        _reservation = reservation ?? (bytes == 0 ? null : budget.Reserve(bytes, SvgMemoryCategory.Scene));
        Points = points;
        ContourEnds = contourEnds;
        Signs = signs;
        QuadSpans = quadSpans;
        CubicSpans = cubicSpans;
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

    /// <summary>Relinquishes a charge after another binding adopts this payload; the borrowed arrays remain readable.</summary>
    internal void RelinquishOwnership() => _reservation = null;

    /// <summary>Releases this owner's charge exactly once, after borrowers finish; leaves immutable array metadata readable.</summary>
    public void Dispose()
    {
        _reservation?.Dispose();
        _reservation = null;
    }
}
