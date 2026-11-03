using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains the native stroke record used after source-space dash filtering.</summary>
internal readonly record struct SvgNativeStrokeParameters(float Width, float MiterLimit, SvgStrokeCap Cap,
    SvgStrokeJoin Join, float ResolutionScale);

/// <summary>Owns undashed cullable verbs and their pattern until the retained stroke is released.</summary>
/// <remarks>Creation adopts commands only on success and copies the borrowed pattern. Copying reserves
/// separate arrays; the owning authored path moves or disposes this owner. Replay borrows immutable
/// inputs and returns private filtered commands charged to its operation budget. Source pathLength
/// scaling precedes culling. All copies and filtering observe cancellation; no shared mutable state.</remarks>
internal sealed class SvgAuthoredDashSource : IDisposable
{
    private readonly OwnedParserArray<SvgAuthoredPathCommand> _commands;
    private readonly OwnedParserArray<double> _intervals;
    private readonly SvgAffineTransform _pointMap;
    private readonly double _offset;
    private readonly double? _pathLength;
    private readonly SvgDashCull _sourceBounds;
    private readonly RenderBudget.BudgetReservation _owner;

    private SvgAuthoredDashSource(OwnedParserArray<SvgAuthoredPathCommand> commands, OwnedParserArray<double> intervals,
        SvgAffineTransform pointMap, double offset, double? pathLength, SvgDashCull sourceBounds,
        SvgNativeStrokeParameters parameters, RenderBudget.BudgetReservation owner)
    {
        _commands = commands; _intervals = intervals; _pointMap = pointMap; _offset = offset;
        _pathLength = pathLength; _sourceBounds = sourceBounds; Parameters = parameters; _owner = owner;
    }

    internal SvgNativeStrokeParameters Parameters { get; }

    internal static SvgAuthoredDashSource Create(OwnedParserArray<SvgAuthoredPathCommand> commands,
        ReadOnlySpan<double> intervals, SvgAffineTransform pointMap, double offset, double? pathLength,
        SvgNativeStrokeParameters parameters, RenderBudget budget, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Covers the retained owner and temporary pattern-list/reservation metadata.
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Scene);
        OwnedParserArray<double> copied = new([], null);
        try
        {
            using BudgetedParserList<double> pattern = new(budget, SvgMemoryCategory.Scene, token);
            foreach (double interval in intervals) pattern.Add(interval);
            copied = pattern.Detach();
            float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
            foreach (SvgAuthoredPathCommand command in commands.Items)
            {
                token.ThrowIfCancellationRequested();
                if (command.Verb is not (SvgAuthoredPathVerb.Move or SvgAuthoredPathVerb.Line)) continue;
                SvgDevicePoint point = SvgDashCulling.Map(pointMap, command.First);
                left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
                right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
            }
            return new(commands, copied, pointMap, offset, pathLength, new(left, top, right, bottom), parameters, owner);
        }
        catch { copied.Dispose(); owner.Dispose(); throw; }
    }

    internal SvgAuthoredDashSource Copy(RenderBudget budget, CancellationToken token)
    {
        using RenderBudget.BudgetReservation scratch = budget.Reserve(128, SvgMemoryCategory.Scene);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, token);
        foreach (SvgAuthoredPathCommand command in _commands.Items) commands.Add(command);
        OwnedParserArray<SvgAuthoredPathCommand> copied = commands.Detach();
        try { return Create(copied, _intervals.Items, _pointMap, _offset, _pathLength, Parameters, budget, token); }
        catch { copied.Dispose(); throw; }
    }

    /// <summary>Returns a private filtered centerline when its source escapes this construction clip.</summary>
    /// <remarks>The output map places already-filtered source coordinates. A noninvertible or nonfinite
    /// inverse cull conservatively retains the original prepared path. The caller releases a successful
    /// output after its thin-stroke traversal or outline construction has finished.</remarks>
    internal bool TryFilter(SvgAffineTransform map, SvgAnalyticClipContext construction, RenderBudget budget,
        CancellationToken token, out OwnedParserArray<SvgAuthoredPathCommand> commands)
    {
        token.ThrowIfCancellationRequested();
        commands = new([], null);
        SvgAnalyticClipBounds bitmap = construction.Bounds;
        SvgCpuBitmapOrigin origin = construction.BitmapOrigin;
        // Match point construction: narrow the scene matrix and subtract the
        // bitmap origin before inverting. A global inverse loses this rounding.
        SvgAffineTransform local = new((float)map.A, (float)map.B, (float)map.C, (float)map.D,
            (float)map.E - origin.X, (float)map.F - origin.Y);
        if (!local.TryInvert(out SvgAffineTransform inverse)) return false;
        Span<SvgDevicePoint> corners = stackalloc SvgDevicePoint[4]
        {
            new(bitmap.Left - origin.X, bitmap.Top - origin.Y), new(bitmap.Right - origin.X, bitmap.Top - origin.Y),
            new(bitmap.Left - origin.X, bitmap.Bottom - origin.Y), new(bitmap.Right - origin.X, bitmap.Bottom - origin.Y)
        };
        float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
        foreach (SvgDevicePoint corner in corners)
        {
            SvgDevicePoint point = SvgDashCulling.Map(inverse, corner);
            left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
            right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
        }
        float radius = Parameters.Width == 0 ? 1 : Parameters.Width * .5f;
        if (Parameters.Join == SvgStrokeJoin.Miter) radius *= Parameters.MiterLimit;
        SvgDashCull bounds = new(left - radius, top - radius, right + radius, bottom + radius);
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) || !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom)) return false;
        if (_sourceBounds.Left >= bounds.Left && _sourceBounds.Top >= bounds.Top
            && _sourceBounds.Right <= bounds.Right && _sourceBounds.Bottom <= bounds.Bottom) return false;
        commands = SvgNativeDashPath.FilterClipped(_commands.Items, _intervals.Items, _offset, _pathLength,
            _pointMap, Parameters.ResolutionScale, bounds, budget, token);
        return true;
    }

    public void Dispose()
    {
        _commands.Dispose(); _intervals.Dispose(); _owner.Dispose();
    }
}
