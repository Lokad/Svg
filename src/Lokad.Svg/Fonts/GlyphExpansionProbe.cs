namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local glyph-expansion entry seam for deterministic cancellation tests.</summary>
internal static class GlyphExpansionProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new Scope(previous);
    }

    internal static void Started()
    {
        Action? callback = Callback.Value;
        Callback.Value = null;
        callback?.Invoke();
    }

    private sealed class Scope(Action? previous) : IDisposable
    {
        public void Dispose() => Callback.Value = previous;
    }
}
