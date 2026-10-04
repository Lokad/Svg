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
        return new Scope(previous);
    }

    internal static void Row(int row) => Observer.Value?.Invoke(row);

    private sealed class Scope(Action<int>? previous) : IDisposable
    {
        private Action<int>? _previous = previous;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            Observer.Value = _previous;
            _previous = null;
            _disposed = true;
        }
    }
}
