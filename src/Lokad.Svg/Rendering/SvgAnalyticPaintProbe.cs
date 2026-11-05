namespace Lokad.Svg.Rendering;

internal enum SvgAnalyticPaintPhase { BeforeBufferGrowth, BeforeSpanBufferGrowth, BeforeReplay, BeforeLinkBufferGrowth }

/// <summary>Provides an execution-context-local seam for operation storage and replay cancellation.</summary>
/// <remarks>Callbacks flow to child contexts independently of the parent's scope lifetime.
/// Each scope restores its owning context; unarmed observations allocate nothing.</remarks>
internal static class SvgAnalyticPaintProbe
{
    [ThreadStatic]
    private static Action<SvgAnalyticPaintPhase, int>? CallbackForCurrentThread;
    private static readonly AsyncLocal<Action<SvgAnalyticPaintPhase, int>?> Callback = new(static change => CallbackForCurrentThread = change.CurrentValue);

    internal static IDisposable Arm(Action<SvgAnalyticPaintPhase, int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action<SvgAnalyticPaintPhase, int>? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action<SvgAnalyticPaintPhase, int>>(Callback, previous);
    }

    /// <summary>Reports every phase/count to the current context's callback, including inherited child contexts.</summary>
    internal static void Observe(SvgAnalyticPaintPhase phase, int operationCount)
        => CallbackForCurrentThread?.Invoke(phase, operationCount);
}
