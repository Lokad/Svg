namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local segment-copy seam for deterministic glyph-outline cancellation tests.</summary>
internal static class GlyphOutlineProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new Scope(previous);
    }

    internal static void SegmentCopied()
    {
        Action? callback = Callback.Value;
        Callback.Value = null;
        callback?.Invoke();
    }

    private sealed class Scope(Action? previous) : IDisposable
    {
        private Action? _previous = previous;

        public void Dispose() => Callback.Value = Interlocked.Exchange(ref _previous, null);
    }
}
