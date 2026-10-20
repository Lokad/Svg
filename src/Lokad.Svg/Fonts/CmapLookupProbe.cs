namespace Lokad.Svg.Fonts;

/// <summary>Provides an execution-context-local cmap lookup entry seam for deterministic cancellation tests.</summary>
internal static class CmapLookupProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action>(Callback, previous);
    }

    internal static void Started() => Callback.Value?.Invoke();

}
