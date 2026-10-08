using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Validates retained local reference graphs, including unused definitions, without recursion.</summary>
/// <remarks>Parser-thread-only owner. Visitation and stack storage reserve before growth; every scanned node
/// charges work and observes cancellation. Disposal releases metadata, never borrowed definition geometry.</remarks>
internal sealed class SvgSymbolReferenceValidator : IDisposable
{
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly SvgDocumentState _state;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly BudgetedParserDictionary<SvgSymbolContentRange, byte> _visits;
    private readonly BudgetedParserList<Frame> _stack;
    private readonly record struct Frame(SvgSymbolContentRange Range, int Next);

    private SvgSymbolReferenceValidator(SvgDocumentState state, ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation owner)
    {
        _owner = owner; _state = state; _work = work; _token = token;
        _visits = new(state.Budget, SvgMemoryCategory.Parser, 128);
        _stack = new(state.Budget, SvgMemoryCategory.Parser, work, token);
    }

    internal static SvgSymbolReferenceValidator Create(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(512, SvgMemoryCategory.Parser);
        try { return new(state, work, token, owner); }
        catch { owner.Dispose(); throw; }
    }

    internal void Validate(SvgDefinition definition)
    {
        if (definition.SymbolContent is not {} root) return;
        Push(root);
        while (_stack.Count > 0)
        {
            _work.Consume(_token);
            Frame frame = _stack[^1];
            if (frame.Next == frame.Range.Start + frame.Range.Count)
            {
                _visits.Replace(frame.Range, 2); _stack.RemoveLast(); continue;
            }
            SvgSymbolContentNode node = frame.Range.Content.Nodes[frame.Next];
            _stack[^1] = frame with { Next = frame.Next + 1 };
            if (node.Kind != SvgSymbolContentKind.Use) continue;
            if (!_state.Definitions.TryGetValue(node.Reference.Id, out SvgDefinition target))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local references in symbol content are implemented.");
            if (target.SymbolContent is {} child) Push(child);
        }
    }

    private void Push(SvgSymbolContentRange range)
    {
        _work.Consume(_token);
        if (_visits.TryGetValue(range, out byte value))
        {
            if (value == 1) throw new SvgException(SvgErrorCode.MalformedInput, "SVG use references cannot form a circular dependency.");
            return;
        }
        _visits.TryAdd(range, 1);
        _stack.Add(new(range, range.Start));
    }

    public void Dispose() { _stack.Dispose(); _visits.Dispose(); _owner.Dispose(); }
}
