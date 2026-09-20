namespace Lokad.Svg.Rendering;

/// <summary>Provides an execution-context-local coverage-entry seam for deterministic cancellation tests.</summary>
internal static class GlyphCoverageProbe
{
    private static readonly AsyncLocal<Action?> CallbackForCurrentExecution = new();

    /// <summary>Arms one callback that runs when glyph coverage next starts on this execution context.</summary>
    internal static IDisposable Arm(Action onStarted)
    {
        ArgumentNullException.ThrowIfNull(onStarted);
        Action? previous = CallbackForCurrentExecution.Value;
        CallbackForCurrentExecution.Value = onStarted;
        return new Scope(previous);
    }

    /// <summary>Consumes the armed callback before coverage samples begin.</summary>
    internal static void Started()
    {
        Action? callback = CallbackForCurrentExecution.Value;
        if (callback is null) return;
        CallbackForCurrentExecution.Value = null;
        callback();
    }

    private sealed class Scope(Action? previous) : IDisposable
    {
        private Action? _previous = previous;

        public void Dispose()
        {
            CallbackForCurrentExecution.Value = Interlocked.Exchange(ref _previous, null);
        }
    }
}
