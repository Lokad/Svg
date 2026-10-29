using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Owns a shortened drawable path string when SVG syntax recovery needs one.</summary>
/// <remarks>The original XML value remains borrowed. A copied prefix charges parser storage before allocation;
/// the lowering caller disposes this temporary owner after commands and geometry have transferred.</remarks>
internal readonly record struct SvgRenderablePathData(string Value, int CommandCount, RenderBudget.BudgetReservation? Reservation) : IDisposable
{
    internal static SvgRenderablePathData Read(string? value, int maximumCommands, RenderBudget budget, CancellationToken token)
    {
        value ??= string.Empty;
        (int length, int count) = SvgPathDataParser.ReadRenderablePrefix(value, maximumCommands, token);
        // Whitespace still passes through authored scanning and work accounting.
        if (count == 0) return new(string.Empty, count, null);
        if (length == value.Length) return new(value, count, null);
        if (length == 0) return new(string.Empty, count, null);
        token.ThrowIfCancellationRequested();
        RenderBudget.BudgetReservation reservation = budget.Reserve(32 + (long)length * sizeof(char), SvgMemoryCategory.Parser);
        try { return new(value.AsSpan(0, length).ToString(), count, reservation); }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>Releases the temporary prefix reservation without disposing borrowed input or observing cancellation.</summary>
    public void Dispose() => Reservation?.Dispose();
}
