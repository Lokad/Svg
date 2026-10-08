using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgSymbolPresentation(SvgPaint Paint, SvgInheritedStrokeStyle Stroke, string? FillRule, bool HasAuthoredFill, SvgPaintOpacity? FillOpacity, SvgColor? CurrentColor, SvgPaintOpacity SourceOpacity);

/// <summary>Owns one open symbol's ordered children until its complete definition transfers to the document registry.</summary>
/// <remarks>Storage and copies charge the parser budget and work counter before allocation. Failure and cancellation
/// leave children owned until retry or disposal; successful registration transfers geometry ownership and leaves child-ID aliases borrowed.</remarks>
internal sealed class SvgSymbolDefinitionBuilder : IDisposable
{
    private readonly BudgetedParserList<SvgDefinition> _children;
    private readonly RenderBudget.BudgetReservation _owner;

    private SvgSymbolDefinitionBuilder(RenderBudget budget, ParserWorkCounter work, CancellationToken token, SvgSymbolPresentation presentation, RenderBudget.BudgetReservation owner)
    {
        _children = new(budget, SvgMemoryCategory.Parser, work, token);
        Presentation = presentation;
        _owner = owner;
    }

    internal SvgSymbolPresentation Presentation { get; }

    /// <summary>Optional assembly observer for deterministic cancellation tests. Runs on the owning parser thread
    /// after each copied geometry; a throwing observer leaves children owned by this builder and releases assembly scratch.</summary>
    internal Action<int>? AssemblyCheckpoint { private get; set; }

    internal static SvgSymbolDefinitionBuilder Create(RenderBudget budget, ParserWorkCounter work, CancellationToken token, SvgSymbolPresentation presentation)
    {
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Parser);
        try { return new(budget, work, token, presentation, owner); }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Transfers a complete child's geometries on success; the caller retains ownership if growth fails.</summary>
    internal void Add(SvgDefinition definition) => _children.Add(definition);

    /// <summary>Transfers all ordered geometry owners into one symbol, retaining separate inherited-fill bindings per child channel.</summary>
    internal SvgDefinition Build(SvgViewBox? viewBox, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (_children.Count == 0) return new([], false, null, viewBox) { SymbolFillBindings = [] };
        if (_children.Count == 1 && Presentation.SourceOpacity.Value == 1)
        {
            SvgDefinition child = _children[0];
            _children.Clear();
            return child with { SymbolViewBox = viewBox, BorrowedFromSymbol = false };
        }
        long total = 0;
        foreach (SvgDefinition child in _children.AsSpan())
        {
            work.Consume(token);
            total += child.Geometries.Length;
        }
        if (total > int.MaxValue) throw new SvgException(SvgErrorCode.LimitExceeded, "Symbol geometry storage exceeds the supported range.");
        int count = (int)total;
        if (count == 0)
        {
            _children.Clear();
            return new([], false, null, viewBox) { SymbolFillBindings = [] };
        }
        RenderBudget.BudgetReservation? reservation = state.Budget.Reserve(
            128 + ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(count) + ParserCollectionMemory.GetArrayByteCount<SvgSymbolFillBinding>(count), SvgMemoryCategory.Scene);
        try
        {
            SvgGeometry[] geometries = new SvgGeometry[count];
            SvgSymbolFillBinding[] bindings = new SvgSymbolFillBinding[count];
            int offset = 0;
            foreach (SvgDefinition child in _children.AsSpan())
            {
                SvgSymbolFillBinding[] childBindings = child.SymbolFillBindings ?? throw new InvalidOperationException("Symbol children require inherited-fill bindings.");
                for (int index = 0; index < child.Geometries.Length; index++)
                {
                    work.Consume(token);
                    geometries[offset] = child.Geometries[index];
                    bindings[offset++] = childBindings[index];
                    AssemblyCheckpoint?.Invoke(offset);
                }
            }
            state.DefinitionReservations.Add(reservation);
            reservation = null;
            _children.Clear();
            return new(geometries, false, null, viewBox) { SymbolFillBindings = bindings };
        }
        finally { reservation?.Dispose(); }
    }

    public void Dispose()
    {
        foreach (SvgDefinition child in _children.AsSpan())
            foreach (SvgGeometry geometry in child.Geometries) geometry.Dispose();
        _children.Dispose();
        _owner.Dispose();
    }
}
