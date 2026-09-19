namespace Lokad.Svg.Rendering;

/// <summary>Provides an immutable observation of a render budget.</summary>
internal readonly record struct RenderBudgetSnapshot(
    long CurrentBytes,
    long PeakBytes,
    long DeniedReservationCount,
    long[] BytesByCategory);
