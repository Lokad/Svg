namespace Lokad.Svg.Fonts;

/// <summary>Provides execution-context-local cmap lookup and preparation seams for cancellation and batching tests.</summary>
internal static class CmapLookupProbe
{
    private static readonly AsyncLocal<Action?> Callback = new();
    private static readonly AsyncLocal<Action?> ValidationCallback = new();

    internal static IDisposable Arm(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action>(Callback, previous);
    }

    internal static void Started() => Callback.Value?.Invoke();

    internal static IDisposable ArmValidation(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action? previous = ValidationCallback.Value;
        ValidationCallback.Value = callback;
        return new SvgProbeScope<Action>(ValidationCallback, previous);
    }

    internal static void Validating() => ValidationCallback.Value?.Invoke();

}
