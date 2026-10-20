namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local segment-transformation seam for deterministic glyph-outline cancellation tests.</summary>
internal static class GlyphOutlineProbe
{
    [ThreadStatic]
    private static Action? CallbackForCurrentThread;
    private static readonly AsyncLocal<Action?> Callback = new(static change => CallbackForCurrentThread = change.CurrentValue);

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action>(Callback, previous);
    }

    internal static void SegmentTransformed()
    {
        Action? callback = CallbackForCurrentThread;
        if (callback is null) return;
        Callback.Value = null;
        callback();
    }

}
