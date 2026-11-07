namespace Lokad.Svg.Rendering;

/// <summary>Provides an execution-context-local observation of draw sampling-context preparation.</summary>
internal static class SvgShaderSamplingProbe
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

    /// <summary>Unarmed preparation allocates nothing; inherited child contexts retain their callback independently.</summary>
    internal static void Prepared() => CallbackForCurrentThread?.Invoke();
}
