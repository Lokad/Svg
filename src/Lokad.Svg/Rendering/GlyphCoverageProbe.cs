namespace Lokad.Svg.Rendering;

/// <summary>Provides an execution-context-local coverage-entry seam for deterministic cancellation tests.</summary>
internal static class GlyphCoverageProbe
{
    [ThreadStatic]
    private static Action? CallbackForCurrentThread;
    // AsyncLocal updates the fast thread cache on assignments and context switches,
    // including inherited child contexts that outlive the parent's arm scope.
    private static readonly AsyncLocal<Action?> CallbackForCurrentExecution = new(static change => CallbackForCurrentThread = change.CurrentValue);

    /// <summary>Arms one callback that runs when glyph coverage next starts on this execution context.</summary>
    internal static IDisposable Arm(Action onStarted)
    {
        ArgumentNullException.ThrowIfNull(onStarted);
        Action? previous = CallbackForCurrentExecution.Value;
        CallbackForCurrentExecution.Value = onStarted;
        return new SvgProbeScope<Action>(CallbackForCurrentExecution, previous);
    }

    /// <summary>Consumes the armed callback before coverage samples begin.</summary>
    internal static void Started()
    {
        Action? callback = CallbackForCurrentThread;
        if (callback is null) return;
        CallbackForCurrentExecution.Value = null;
        callback();
    }

}
