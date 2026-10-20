namespace Lokad.Svg.Rendering;

/// <summary>Observes solid-canvas raster checkpoints inside one execution context for cancellation and ownership validation.</summary>
internal static class SolidCanvasRasterizationProbe
{
    private static readonly AsyncLocal<Action<int>?> Observer = new();

    /// <summary>Arms one observer; callers dispose its scope before reusing this execution context.</summary>
    internal static IDisposable Arm(Action<int> onRow)
    {
        ArgumentNullException.ThrowIfNull(onRow);
        Action<int>? previous = Observer.Value;
        Observer.Value = onRow;
        return new SvgProbeScope<Action<int>>(Observer, previous);
    }

    internal static void Row(int row) => Observer.Value?.Invoke(row);

}
