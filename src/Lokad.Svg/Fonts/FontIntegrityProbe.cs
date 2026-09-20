namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local completed-hash-chunk seam for deterministic cancellation tests.</summary>
internal static class FontIntegrityProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new Restoration(previous);
    }

    internal static void HashChunkCompleted() => Callback.Value?.Invoke();

    private sealed class Restoration(Action? previous) : IDisposable
    {
        public void Dispose() => Callback.Value = previous;
    }
}
