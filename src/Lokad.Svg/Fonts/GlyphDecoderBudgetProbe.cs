namespace Lokad.Svg.Fonts;

using Lokad.Svg.Rendering;

/// <summary>Identifies reservation boundaries in the budgeted glyph decoder for deterministic ownership tests.</summary>
internal enum GlyphDecoderBudgetPhase
{
    None,
    ActiveGlyphs,
    SimpleEndpoints,
    SimpleFlags,
    SimplePoints,
    SimpleSegmentCapacity,
    SimpleSegmentExact,
    SimpleContourBoundaryCapacity,
    SimpleContourBoundaryExact,
    CompositeComponentsCapacity,
    CompositeComponentsExact,
    ExpandedSegmentCapacity,
    ExpandedSegmentExact,
    ExpandedContourBoundaryCapacity,
    ExpandedContourBoundaryExact
}

/// <summary>Provides an execution-context-local observation seam after a budgeted glyph-decoder array has been allocated.</summary>
internal static class GlyphDecoderBudgetProbe
{
    private static readonly AsyncLocal<Action<GlyphDecoderBudgetPhase, RenderBudgetSnapshot>?> Callback = new();

    internal static IDisposable Arm(Action<GlyphDecoderBudgetPhase, RenderBudgetSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Action<GlyphDecoderBudgetPhase, RenderBudgetSnapshot>? previous = Callback.Value;
        Callback.Value = callback;
        return new SvgProbeScope<Action<GlyphDecoderBudgetPhase, RenderBudgetSnapshot>>(Callback, previous);
    }

    internal static void Reserved(GlyphDecoderBudgetPhase phase, RenderBudget? budget)
    {
        if (phase == GlyphDecoderBudgetPhase.None || budget is null) return;
        Callback.Value?.Invoke(phase, budget.Snapshot());
    }

}
