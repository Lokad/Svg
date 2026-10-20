namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local glyph-expansion entry seam for deterministic cancellation tests.</summary>
internal static class GlyphExpansionProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();
    private static readonly AsyncLocal<Action?> Observer = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action>(Callback, previous);
    }

    /// <summary>Observes every expansion entry without changing the one-shot cancellation callback.</summary>
    internal static IDisposable Observe(Action observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        Action? previous = Observer.Value;
        Observer.Value = observer;
        return new SvgProbeScope<Action>(Observer, previous);
    }

    internal static void Started()
    {
        Observer.Value?.Invoke();
        Action? callback = Callback.Value;
        Callback.Value = null;
        callback?.Invoke();
    }

}
