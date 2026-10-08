using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgSymbolContentKind { Leaf, Group, EndGroup, Use }

/// <summary>Retains independently authored container paint; absent properties inherit from the instance context.</summary>
internal readonly record struct SvgSymbolContentStyle(SvgPaint Paint, bool HasAuthoredFill, SvgPaintOpacity? FillOpacity, SvgColor? Color, SvgPaintOpacity Opacity, bool IsDisplayed, bool? IsVisible)
{
    internal SvgPaint Resolve(SvgPaint inherited)
    {
        SvgPaint paint = HasAuthoredFill ? Paint : inherited;
        return paint with { FillOpacity = FillOpacity ?? inherited.FillOpacity, CurrentColor = Color ?? inherited.CurrentColor };
    }
}

internal readonly record struct SvgSymbolContentNode(SvgSymbolContentKind Kind, SvgDefinition Definition, SvgUseReference Reference, SvgSymbolContentStyle Style, SvgAffineTransform Transform, int End);
internal readonly record struct SvgSymbolContentRange(SvgSymbolContent Content, int Start, int Count);
internal readonly record struct SvgSymbolContentGroup(int Depth, int Start, string? Id, SvgSymbolPresentation Presentation);

/// <summary>Owns ordered, non-rendering symbol source geometry and typed group/use nodes.</summary>
/// <remarks>One root definition owns this object; ID aliases borrow ranges or leaf geometry. Mutable construction
/// and sealing reserve storage and charge work before allocation/copy. Native sources stay alive through expansion,
/// and every failure or cancellation releases the owning builder. The object is confined to the parser thread.</remarks>
internal sealed class SvgSymbolContent : IDisposable
{
    private readonly RenderBudget _budget;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly BudgetedParserList<SvgSymbolContentNode> _building;
    private readonly BudgetedParserList<SvgSymbolContentGroup> _groups;
    private readonly SvgSymbolPresentation _rootPresentation;
    private RenderBudget.BudgetReservation? _sealedOwner;
    private SvgSymbolContentNode[]? _nodes;
    private bool _ownsLeaves;
    private bool _disposed;
    private bool _closingAdded;

    private SvgSymbolContent(RenderBudget budget, ParserWorkCounter work, CancellationToken token, SvgSymbolPresentation presentation, RenderBudget.BudgetReservation owner)
    {
        _budget = budget; _work = work; _token = token; _owner = owner; _rootPresentation = presentation;
        _building = new(budget, SvgMemoryCategory.Parser, work, token);
        _groups = new(budget, SvgMemoryCategory.Parser, work, token);
    }

    internal static SvgSymbolContent Create(RenderBudget budget, ParserWorkCounter work, CancellationToken token, SvgSymbolPresentation presentation)
    {
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Parser);
        SvgSymbolContent? content = null;
        try
        {
            content = new(budget, work, token, presentation, owner);
            content._building.Add(new(SvgSymbolContentKind.Group, default, default,
                new(presentation.Paint, presentation.HasAuthoredFill, presentation.FillOpacity, presentation.CurrentColor, presentation.SourceOpacity, true, null), SvgAffineTransform.Identity, -1));
            return content;
        }
        catch { if (content is null) owner.Dispose(); else content.Dispose(); throw; }
    }

    internal void ClaimLeafOwnership() => _ownsLeaves = true;
    internal SvgSymbolPresentation Presentation => _groups.Count == 0 ? _rootPresentation : _groups[^1].Presentation;
    internal int ChildDepth(int symbolDepth) => (_groups.Count == 0 ? symbolDepth : _groups[^1].Depth) + 1;
    internal ReadOnlySpan<SvgSymbolContentNode> Nodes => _nodes ?? throw new InvalidOperationException("Symbol content must be sealed before expansion.");

    /// <summary>Transfers one leaf on successful append; failure leaves ownership with the caller.</summary>
    internal void AddLeaf(SvgDefinition definition, bool displayed, bool? visible)
        => _building.Add(new(SvgSymbolContentKind.Leaf, definition with { SymbolFillBindings = definition.SourceFillBindings ?? definition.SymbolFillBindings }, default,
            new(default, false, null, null, SvgPaintOpacity.Opaque, displayed, visible), SvgAffineTransform.Identity, -1));

    internal void BeginGroup(int depth, string? id, SvgSymbolPresentation presentation, SvgSymbolContentStyle style, SvgAffineTransform transform)
    {
        int start = _building.Count;
        _groups.Add(new(depth, start, id, presentation));
        try { _building.Add(new(SvgSymbolContentKind.Group, default, default, style, transform, -1)); }
        catch { _groups.RemoveLast(); throw; }
    }

    internal int AddUse(SvgUseReference reference, bool displayed, bool? visible)
    {
        int start = _building.Count;
        _building.Add(new(SvgSymbolContentKind.Use, default, reference,
            new(default, false, null, null, reference.SourceOpacity, displayed, visible), SvgAffineTransform.Identity, -1));
        return start;
    }

    internal bool EndGroup(int depth, SvgDocumentState state)
    {
        if (_groups.Count == 0 || _groups[^1].Depth != depth) return false;
        SvgSymbolContentGroup group = _groups[^1];
        _building.Add(new(SvgSymbolContentKind.EndGroup, default, default, default, default, -1));
        _building[group.Start] = _building[group.Start] with { End = _building.Count - 1 };
        if (group.Id is string id)
            state.Definitions.Replace(id, new([], false, null, null) { SymbolContent = new(this, group.Start, _building.Count - group.Start), BorrowedFromSymbol = true });
        _groups.RemoveLast();
        return true;
    }

    internal SvgSymbolContentRange Seal(ParserWorkCounter work, CancellationToken token, Action<int>? checkpoint)
    {
        if (_groups.Count != 0) throw new InvalidOperationException("Symbol groups must close before sealing.");
        if (_nodes is not null) return new(this, 0, _nodes.Length);
        if (!_closingAdded)
        {
            _building.Add(new(SvgSymbolContentKind.EndGroup, default, default, default, default, -1));
            _building[0] = _building[0] with { End = _building.Count - 1 };
            _closingAdded = true;
        }
        RenderBudget.BudgetReservation owner = _budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgSymbolContentNode>(_building.Count), SvgMemoryCategory.Scene);
        try
        {
            SvgSymbolContentNode[] nodes = new SvgSymbolContentNode[_building.Count];
            for (int index = 0; index < nodes.Length; index++)
            {
                work.Consume(token);
                nodes[index] = _building[index];
                checkpoint?.Invoke(index + 1);
            }
            token.ThrowIfCancellationRequested();
            _nodes = nodes; _sealedOwner = owner;
            _building.Dispose(); _groups.Dispose();
            return new(this, 0, nodes.Length);
        }
        catch { owner.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsLeaves)
        {
            ReadOnlySpan<SvgSymbolContentNode> nodes = _nodes is null ? _building.AsSpan() : _nodes;
            foreach (SvgSymbolContentNode node in nodes)
                if (node.Kind == SvgSymbolContentKind.Leaf)
                    foreach (SvgGeometry geometry in node.Definition.Geometries) geometry.Dispose();
        }
        _building.Dispose(); _groups.Dispose(); _sealedOwner?.Dispose(); _owner.Dispose();
    }
}
