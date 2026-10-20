namespace Lokad.Svg.Rendering;

/// <summary>Provides an internal, execution-context-local observation seam for the fresh-process memory probe.</summary>
internal static class BackgroundInitializationProbe
{
    private static readonly AsyncLocal<Callbacks?> CallbacksForCurrentExecution = new();

    /// <summary>Arms callbacks for one synchronous conversion; callbacks run on that conversion's execution context.</summary>
    internal static IDisposable Arm(Action onStarted, Action onCancellationObserved)
    {
        ArgumentNullException.ThrowIfNull(onStarted);
        ArgumentNullException.ThrowIfNull(onCancellationObserved);
        Callbacks? previous = CallbacksForCurrentExecution.Value;
        CallbacksForCurrentExecution.Value = new Callbacks(onStarted, onCancellationObserved);
        return new SvgProbeScope<Callbacks>(CallbacksForCurrentExecution, previous);
    }

    /// <summary>Records that opaque-background initialization has started and consumes the start callback.</summary>
    internal static void Started()
    {
        Callbacks? callbacks = CallbacksForCurrentExecution.Value;
        if (callbacks is null || callbacks.Started) return;
        callbacks.Started = true;
        callbacks.OnStarted();
    }

    /// <summary>Records that a background-loop checkpoint observed cancellation and consumes that callback.</summary>
    internal static void CancellationObserved()
    {
        Callbacks? callbacks = CallbacksForCurrentExecution.Value;
        if (callbacks is null || callbacks.CancellationObserved) return;
        callbacks.CancellationObserved = true;
        callbacks.OnCancellationObserved();
    }

    private sealed class Callbacks(Action onStarted, Action onCancellationObserved)
    {
        internal Action OnStarted { get; } = onStarted;
        internal Action OnCancellationObserved { get; } = onCancellationObserved;
        internal bool Started { get; set; }
        internal bool CancellationObserved { get; set; }
    }

}
