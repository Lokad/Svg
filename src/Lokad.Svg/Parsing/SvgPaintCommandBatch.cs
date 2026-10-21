using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Marks the commands and scope ranges appended by one deferred paint emission.</summary>
internal readonly record struct SvgPaintCommandEmission(int Position, int TailStart, int FirstClip, int FirstMask);

/// <summary>Identifies the first retained text, clip, mask and paint entries belonging to a source replay.</summary>
internal readonly record struct SvgPendingCommandStart(int Text, int Clip, int Mask, int Paint);

/// <summary>Replaces descending paint placeholders with appended commands in one ordered rebuild.</summary>
/// <remarks>Single-threaded, conversion-local scratch borrows the scene and document. Geometry and paint
/// owners never move. Emissions must follow descending original positions and append commands only.
/// Scope ranges created during an emission follow that tail; older ranges follow original boundaries.
/// Storage reserves before growth, every traversal charges work and cancellation, and failure leaves
/// retained owners with document teardown. Disposal releases scratch without observing cancellation.</remarks>
internal sealed class SvgPaintCommandBatch : IDisposable
{
    private readonly record struct Replacement(SvgPaintCommandEmission Emission, int Count, int EndClip, int EndMask);

    private readonly SvgDocumentState _state;
    private readonly SvgSceneBuilder _scene;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly SvgPendingCommandStart _first;
    private readonly int _originalCount;
    private readonly SvgPendingCommandStart _end;
    private readonly BudgetedParserList<Replacement> _replacements;

    internal SvgPaintCommandBatch(SvgDocumentState state, SvgSceneBuilder scene, SvgPendingCommandStart first, SvgPendingCommandStart end, ParserWorkCounter work, CancellationToken token)
    {
        _state = state;
        _scene = scene;
        _work = work;
        _token = token;
        _first = first;
        _originalCount = scene.CommandCount;
        _end = end;
        _replacements = new(state.Budget, SvgMemoryCategory.Parser, work, token);
    }

    internal SvgPaintCommandEmission Begin(int position)
        => new(position, _scene.CommandCount, _state.PendingGroupClips.Count, _state.PendingGroupMasks.Count);

    internal void Retain(SvgPaintCommandEmission emission)
    {
        if ((uint)emission.Position >= (uint)_originalCount || _replacements.Count != 0 && emission.Position >= _replacements[^1].Emission.Position)
            throw new InvalidOperationException("Deferred paint slots must follow descending original drawing order.");
        if (_scene.CommandCount == emission.TailStart)
            _scene.AddGeometry(SvgGeometry.FromRectangle(new(0, 0, 0, 0, SvgColor.Transparent, false, null)));
        _replacements.Add(new(emission, _scene.CommandCount - emission.TailStart, _state.PendingGroupClips.Count, _state.PendingGroupMasks.Count));
    }

    internal void Complete() => Complete(Span<SvgPatternContentScope>.Empty);

    /// <summary>Also remaps retained tile-group starts while the batch owns its boundary map.</summary>
    internal void Complete(Span<SvgPatternContentScope> scopes)
    {
        if (_replacements.Count == 0) return;
        using RenderBudget.BudgetReservation mapOwner = _state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(_originalCount + 1), SvgMemoryCategory.Parser);
        int[] positions = new int[_originalCount + 1];
        BudgetedSceneList<SvgDrawCommand> commands = new(_state.Budget, _work, _token);
        bool transferred = false;
        try
        {
            int next = _replacements.Count - 1;
            for (int index = 0; index < _originalCount; index++)
            {
                _work.Consume(_token);
                positions[index] = commands.Count;
                if (next < 0 || _replacements[next].Emission.Position != index)
                {
                    commands.Add(_scene.CommandAt(index));
                    continue;
                }
                Replacement replacement = _replacements[next--];
                for (int offset = 0; offset < replacement.Count; offset++)
                {
                    _work.Consume(_token);
                    commands.Add(_scene.CommandAt(replacement.Emission.TailStart + offset));
                }
            }
            positions[_originalCount] = commands.Count;
            SvgReferenceResolver.RemapPendingCommandRanges(_state, _scene, positions, _first, _end, _work, _token);
            for (int index = 0; index < scopes.Length; index++)
            {
                _work.Consume(_token);
                scopes[index] = scopes[index] with { FirstCommandIndex = positions[scopes[index].FirstCommandIndex] };
            }
            foreach (Replacement replacement in _replacements.AsSpan())
            {
                _work.Consume(_token);
                SvgPaintCommandEmission emission = replacement.Emission;
                int shift = positions[emission.Position] - emission.TailStart;
                for (int index = emission.FirstClip; index < replacement.EndClip; index++)
                {
                    _work.Consume(_token);
                    PendingGroupClip clip = _state.PendingGroupClips[index];
                    if (ReferenceEquals(clip.Scene, _scene)) _state.PendingGroupClips[index] = clip with
                    {
                        FirstCommandIndex = clip.FirstCommandIndex + shift,
                        EndCommandIndex = clip.EndCommandIndex < 0 ? clip.EndCommandIndex : clip.EndCommandIndex + shift
                    };
                }
                for (int index = emission.FirstMask; index < replacement.EndMask; index++)
                {
                    _work.Consume(_token);
                    PendingGroupMask mask = _state.PendingGroupMasks[index];
                    if (ReferenceEquals(mask.Scene, _scene)) _state.PendingGroupMasks[index] = mask with
                    {
                        FirstCommandIndex = mask.FirstCommandIndex + shift,
                        EndCommandIndex = mask.EndCommandIndex < 0 ? mask.EndCommandIndex : mask.EndCommandIndex + shift
                    };
                }
            }
            _scene.AdoptCommands(commands);
            transferred = true;
        }
        finally { if (!transferred) commands.Dispose(); }
    }

    public void Dispose() => _replacements.Dispose();
}
