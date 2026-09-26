namespace Lokad.Svg.Rendering;

/// <summary>Provides an execution-context-local fan-evaluation seam for raster work characterization.</summary>
internal static class SvgPolygonCoverageProbe
{
    private static readonly AsyncLocal<Action?> CallbackForCurrentExecution = new();

    /// <summary>Arms one callback that runs on every device-space fan evaluation on this execution context.</summary>
    internal static IDisposable Arm(Action onFanEvaluated)
    {
        ArgumentNullException.ThrowIfNull(onFanEvaluated);
        Action? previous = CallbackForCurrentExecution.Value;
        CallbackForCurrentExecution.Value = onFanEvaluated;
        return new Scope(previous);
    }

    /// <summary>Runs the armed fan callback when present; unarmed evaluations cost one null check.</summary>
    internal static void FanEvaluated()
    {
        CallbackForCurrentExecution.Value?.Invoke();
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