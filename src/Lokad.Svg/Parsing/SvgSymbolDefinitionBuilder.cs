using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgSymbolPresentation(SvgPaint Paint, SvgInheritedStrokeStyle Stroke, string? FillRule, bool HasAuthoredFill, SvgPaintOpacity? FillOpacity, SvgColor? CurrentColor, SvgPaintOpacity SourceOpacity)
{
    internal string? PaintServerFill { get; init; }
    internal SvgPaintOrder? PaintOrder { get; init; }
    internal SvgMarkerStyle Markers { get; init; }
    internal string? PaintServerId { get; init; }
}

/// <summary>Owns one open symbol's ordered children until its complete definition transfers to the document registry.</summary>
/// <remarks>Storage and copies charge the parser budget and work counter before allocation. Failure and cancellation
/// leave children owned until retry or disposal; successful registration transfers geometry ownership and leaves child-ID aliases borrowed.</remarks>
internal sealed class SvgSymbolDefinitionBuilder : IDisposable
{
    private readonly BudgetedParserList<SvgDefinition> _children;
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly SvgSymbolPresentation _presentation;
    private SvgSymbolContent? _content;

    private SvgSymbolDefinitionBuilder(RenderBudget budget, ParserWorkCounter work, CancellationToken token, SvgSymbolPresentation presentation, RenderBudget.BudgetReservation owner)
    {
        _children = new(budget, SvgMemoryCategory.Parser, work, token);
        _presentation = presentation; _budget = budget; _work = work; _token = token;
        _owner = owner;
    }

    internal SvgSymbolPresentation Presentation => _content?.Presentation ?? _presentation;
    internal int ChildDepth(int symbolDepth) => _content?.ChildDepth(symbolDepth) ?? symbolDepth + 1;
    internal bool EndGroup(int depth, SvgDocumentState state) => _content?.EndGroup(depth, state) ?? false;

    internal SvgSymbolContent EnableContent()
    {
        if (_content is not null) return _content;
        SvgSymbolContent content = SvgSymbolContent.Create(_budget, _work, _token, _presentation);
        try
        {
            foreach (SvgDefinition child in _children.AsSpan()) content.AddLeaf(child, child.SourceDisplayed, child.SourceVisible);
            content.ClaimLeafOwnership();
            _children.Clear();
            _content = content;
            return content;
        }
        catch { content.Dispose(); throw; }
    }

    /// <summary>Optional assembly observer for deterministic cancellation tests. Runs on the owning parser thread
    /// after each copied geometry or retained source node; a throwing observer leaves children owned by this builder and releases assembly scratch.</summary>
    internal Action<int>? AssemblyCheckpoint { private get; set; }

    internal static SvgSymbolDefinitionBuilder Create(RenderBudget budget, ParserWorkCounter work, CancellationToken token, SvgSymbolPresentation presentation)
    {
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Parser);
        try { return new(budget, work, token, presentation, owner); }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Transfers a complete child's geometries on success; the caller retains ownership if growth fails.</summary>
    internal void Add(SvgDefinition definition)
    {
        if (_content is null) _children.Add(definition);
        else _content.AddLeaf(definition, definition.SourceDisplayed, definition.SourceVisible);
    }

    /// <summary>Transfers all ordered geometry owners into one symbol, retaining separate inherited-fill bindings per child channel.</summary>
    internal SvgDefinition Build(SvgViewBox? viewBox, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (_content is SvgSymbolContent content)
        {
            SvgSymbolContentRange range = content.Seal(work, token, AssemblyCheckpoint);
            _content = null;
            return new([], false, null, viewBox) { SymbolContent = range };
        }
        if (_children.Count == 0) return new([], false, null, viewBox) { SymbolFillBindings = [] };
        if (_children.Count == 1 && Presentation.SourceOpacity.Value == 1)
        {
            SvgDefinition child = _children[0];
            SvgSymbolContentRange strokeRange = RetainStrokeRange();
            _children.Clear();
            return child with { SymbolViewBox = viewBox, BorrowedFromSymbol = false, StrokeContent = strokeRange };
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
            SvgSymbolContentRange strokeRange = RetainStrokeRange();
            _children.Clear();
            return new([], false, null, viewBox) { SymbolFillBindings = [], StrokeContent = strokeRange };
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
            SvgSymbolContentRange strokeRange = RetainStrokeRange();
            state.DefinitionReservations.Add(reservation);
            reservation = null;
            _children.Clear();
            return new(geometries, false, null, viewBox) { SymbolFillBindings = bindings, StrokeContent = strokeRange };
        }
        finally { reservation?.Dispose(); }

        SvgSymbolContentRange RetainStrokeRange()
        {
            SvgSymbolContent content = SvgSymbolContent.Create(_budget, work, token, _presentation);
            try
            {
                foreach (SvgDefinition child in _children.AsSpan()) content.AddLeaf(child, child.SourceDisplayed, child.SourceVisible);
                SvgSymbolContentRange range = content.Seal(work, token, null);
                state.RetainStrokeContent(content);
                return range;
            }
            catch { content.Dispose(); throw; }
        }
    }

    public void Dispose()
    {
        foreach (SvgDefinition child in _children.AsSpan())
            foreach (SvgGeometry geometry in child.Geometries) geometry.Dispose();
        _content?.Dispose();
        _children.Dispose();
        _owner.Dispose();
    }
}
