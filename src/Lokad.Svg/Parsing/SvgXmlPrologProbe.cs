namespace Lokad.Svg.Parsing;

/// <summary>Observes decoded prolog checkpoints in one execution context for cancellation and ownership validation.</summary>
internal static class SvgXmlPrologProbe
{
    private static readonly AsyncLocal<Action<int>?> Observer = new();

    /// <summary>Arms one observer; callers dispose its scope before reusing this execution context.</summary>
    internal static IDisposable Arm(Action<int> onCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(onCheckpoint);
        Action<int>? previous = Observer.Value;
        Observer.Value = onCheckpoint;
        return new Scope(previous);
    }

    internal static void Checkpoint(int position) => Observer.Value?.Invoke(position);

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
