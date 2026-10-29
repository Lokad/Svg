namespace Lokad.Svg.Parsing;

/// <summary>Identifies document-level preparation boundaries for focused invocation-count tests.</summary>
internal enum SvgPreparationPhase { Workspace, StyleSheets, FontIndex, PaintIndex, Scene }

/// <summary>Provides execution-local preparation observations without introducing runtime fuel or quotas.</summary>
internal static class SvgPreparationProbe
{
    private static readonly AsyncLocal<Action<SvgPreparationPhase>?> Callback = new();

    internal static IDisposable Arm(Action<SvgPreparationPhase> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action<SvgPreparationPhase>? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action<SvgPreparationPhase>>(Callback, previous);
    }

    internal static void Observe(SvgPreparationPhase phase) => Callback.Value?.Invoke(phase);
}
