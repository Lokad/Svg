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
        return new SvgProbeScope<Action>(Callback, previous);
    }

    internal static void HashChunkCompleted() => Callback.Value?.Invoke();

}
