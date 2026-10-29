using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

// Parser-owned scratch: mapped scalar scopes outlive this cache through scene values.
internal sealed class SvgRecordingMapper : IDisposable
{
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly SvgDocumentState _state;
    private readonly SvgRecordingScope _root;
    private readonly CancellationToken _token;
    private readonly BudgetedParserDictionary<SvgRecordingScope, SvgRecordingScope> _scopes;
    private readonly BudgetedParserDictionary<SvgPaintOperationIdentity, SvgPaintOperationIdentity> _paints;
    private SvgRecordingMapper(RenderBudget.BudgetReservation owner, SvgDocumentState state, SvgRecordingScope root, CancellationToken token)
    {
        _owner = owner; _state = state; _root = root; _token = token;
        _scopes = new(state.Budget, SvgMemoryCategory.Parser, 96);
        try { _paints = new(state.Budget, SvgMemoryCategory.Parser, 96); }
        catch { _scopes.Dispose(); throw; }
    }
    internal static SvgRecordingMapper Create(SvgDocumentState state, SvgRecordingScope root, CancellationToken token)
    {
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(384, SvgMemoryCategory.Parser);
        try { return new(owner, state, root, token); }
        catch { owner.Dispose(); throw; }
    }
    internal SvgRecordingScope Map(SvgRecordingScope source)
    {
        _token.ThrowIfCancellationRequested();
        if (source.Value < 0) return source;
        if (source.Value <= 1) return _root;
        if (_scopes.TryGetValue(source, out SvgRecordingScope cached)) return cached;
        SvgRecordingScope mapped = _state.NewRecordingScope();
        _scopes.TryAdd(source, mapped);
        return mapped;
    }
    internal SvgPaintOperationIdentity MapPaintIdentity(SvgPaintOperationIdentity source)
    {
        _token.ThrowIfCancellationRequested();
        if (source.Value <= 0) throw new SvgException(SvgErrorCode.MalformedInput, "Recording references an invalid paint identity.");
        if (_paints.TryGetValue(source, out SvgPaintOperationIdentity cached)) return cached;
        SvgPaintOperationIdentity mapped = _state.NewPaintOperationIdentity();
        _paints.TryAdd(source, mapped);
        return mapped;
    }
    public void Dispose() { _paints.Dispose(); _scopes.Dispose(); _owner.Dispose(); }
}
