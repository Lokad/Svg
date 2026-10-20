namespace Lokad.Svg.Rendering;

/// <summary>Provides an execution-context-local fan-evaluation seam for raster work characterization.</summary>
internal static class SvgPolygonCoverageProbe
{
    [ThreadStatic]
    private static Action? CallbackForCurrentThread;
    private static readonly AsyncLocal<Action?> CallbackForCurrentExecution = new(static change => CallbackForCurrentThread = change.CurrentValue);

    /// <summary>Arms one callback that runs on every device-space fan evaluation on this execution context.</summary>
    internal static IDisposable Arm(Action onFanEvaluated)
    {
        ArgumentNullException.ThrowIfNull(onFanEvaluated);
        Action? previous = CallbackForCurrentExecution.Value;
        CallbackForCurrentExecution.Value = onFanEvaluated;
        return new SvgProbeScope<Action>(CallbackForCurrentExecution, previous);
    }

    /// <summary>Runs the execution-context callback cached on the current thread; unarmed evaluations avoid AsyncLocal lookup.</summary>
    internal static void FanEvaluated()
    {
        CallbackForCurrentThread?.Invoke();
    }

}
