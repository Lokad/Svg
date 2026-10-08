namespace Lokad.Svg.Rendering;

internal enum SvgAnalyticPaintPhase { BeforeBufferGrowth, BeforeSpanBufferGrowth, BeforeReplay, BeforeLinkBufferGrowth }

/// <summary>Provides an execution-context-local seam for operation storage and replay cancellation.</summary>
internal static class SvgAnalyticPaintProbe
{
    private static readonly AsyncLocal<Action<SvgAnalyticPaintPhase, int>?> Callback = new();
    private static int _armedScopes;

    internal static IDisposable Arm(Action<SvgAnalyticPaintPhase, int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action<SvgAnalyticPaintPhase, int>? previous = Callback.Value;
        Interlocked.Increment(ref _armedScopes);
        Callback.Value = callback;
        return new Scope(previous);
    }

    internal static void Observe(SvgAnalyticPaintPhase phase, int operationCount)
    {
        if (Volatile.Read(ref _armedScopes) != 0) Callback.Value?.Invoke(phase, operationCount);
    }

    private sealed class Scope(Action<SvgAnalyticPaintPhase, int>? previous) : IDisposable
    {
        private Action<SvgAnalyticPaintPhase, int>? _previous = previous;
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Callback.Value = Interlocked.Exchange(ref _previous, null);
            Interlocked.Decrement(ref _armedScopes);
        }
    }
}
