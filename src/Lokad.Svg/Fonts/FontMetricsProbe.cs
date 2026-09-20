namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local SFNT directory-record seam for deterministic metric-parsing cancellation tests.</summary>
internal static class FontMetricsProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new Restoration(previous);
    }

    internal static void TableRecordVisited()
    {
        Action? callback = Callback.Value;
        Callback.Value = null;
        callback?.Invoke();
    }

    private sealed class Restoration(Action? previous) : IDisposable
    {
        public void Dispose() => Callback.Value = previous;
    }
}
