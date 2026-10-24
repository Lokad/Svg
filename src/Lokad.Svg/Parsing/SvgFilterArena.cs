using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgFilterPrimitiveKind : byte { GaussianBlur, Offset, DropShadow, Merge, Flood, Composite, Blend, Unsupported }
internal enum SvgFilterComposition : byte { Over, In, Multiply, Screen }
internal enum SvgFilterUnits : byte { ObjectBoundingBox, UserSpace }
internal enum SvgFilterColorSpace : byte { LinearRgb, Srgb }
internal readonly record struct SvgFilterInput(int Index)
{
    internal static SvgFilterInput SourceGraphic => new(-1);
    internal static SvgFilterInput SourceAlpha => new(-2);
}
internal readonly record struct SvgFilterLength(double Value, bool Percentage);
internal readonly record struct SvgFilterRegion(SvgFilterLength X, SvgFilterLength Y, SvgFilterLength Width, SvgFilterLength Height, SvgFilterUnits Units);
/// <summary>Retains authored subregion lengths; omitted components borrow the input's default subregion.</summary>
internal readonly record struct SvgFilterPrimitiveRegion(SvgFilterLength? X, SvgFilterLength? Y, SvgFilterLength? Width, SvgFilterLength? Height);
internal readonly record struct SvgFilterPrimitive(SvgFilterPrimitiveKind Kind, SvgFilterInput Input, double SigmaX, double SigmaY, double OffsetX, double OffsetY, SvgColor Flood, float FloodOpacity)
{
    internal SvgFilterColorSpace? ColorSpace { get; init; }
    internal SvgFilterPrimitiveRegion? Region { get; init; }
    internal SvgFilterInput SecondInput { get; init; }
    internal SvgFilterComposition Composition { get; init; }
    internal int MergeFirst { get; init; }
    internal int MergeCount { get; init; }
}
internal readonly record struct SvgFilterAdmission(SvgErrorCode Code, string Message);
internal readonly record struct SvgFilterResource(SvgFilterRegion Region, SvgFilterUnits PrimitiveUnits, SvgFilterColorSpace ColorSpace,
    OwnedSceneArray<SvgFilterPrimitive> Primitives, SvgFilterAdmission? Rejection) : IDisposable
{
    /// <summary>Disables playback when the filter or its defs container has display:none.</summary>
    internal bool IsAvailable { get; init; } = true;
    internal OwnedSceneArray<SvgFilterInput> MergeInputs { get; init; } = new([], null);
    public void Dispose() { MergeInputs.Dispose(); Primitives.Dispose(); }
}
internal readonly record struct SvgFilterBinding(string? ReferenceId, int ResourceIndex, SvgPaintBounds ObjectBounds,
    SvgPaintBounds VisualBounds, SvgAffineTransform Transform, SvgViewportLengthBases LengthBases)
{
    internal bool GroupSource { get; init; }
    internal string? ClipReferenceId { get; init; }
    /// <summary>Borrows a parser-owned output clip until command emission; sealing clears the reference.</summary>
    internal SvgLeafMaskBinding? OutputClip { get; init; }
}

/// <summary>Borrows one filter placement while retained source scopes are mapped and emitted.</summary>
/// <remarks>Parser metadata owns no resources. Each destination scene owns its emitted binding;
/// resolved resource indices refer to the shared document definitions, never another scene's bindings.</remarks>
internal sealed record SvgRetainedFilter(SvgFilterBinding Binding)
{
    internal static SvgRetainedFilter? Create(string? filter, SvgAffineTransform transform, SvgViewportLengthBases lengthBases, SvgDocumentState state)
    {
        if (filter is null || filter.Trim() == "none") return null;
        if (!SvgLocalUrl.TryParseId(filter, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local filter url references are implemented.");
        return new(new SvgFilterBinding(state.Strings.Copy(id), -1, default, default, transform, lengthBases));
    }
}

/// <summary>Owns filter resources and placed bindings until its scene is disposed.</summary>
/// <remarks>Construction is single-threaded and budgeted. Sealing resolves references and drops parser-only names.
/// Sealed resources are immutable and borrowed by playback; disposal is owned by the enclosing scene.</remarks>
internal sealed class SvgFilterArena : IDisposable
{
    private readonly record struct SourceRange(int First, int End);

    private readonly RenderBudget.BudgetReservation _owner;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly BudgetedSceneList<SvgFilterResource> _resources;
    private readonly BudgetedSceneList<SvgFilterBinding> _bindings;
    private BudgetedParserDictionary<string, int>? _names;
    private BudgetedParserStringStore? _strings;
    private bool _sealed;
    private bool _disposed;
    private bool _hasPendingClips;
    private SvgFilterArena? _resourceSource;
    internal bool HasGroupSources { get; private set; }

    private SvgFilterArena(RenderBudget budget, ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation owner)
    {
        _owner = owner;
        _work = work;
        _token = token;
        _resources = new(budget, work, token);
        _bindings = new(budget, work, token);
        _names = new(budget, SvgMemoryCategory.Parser, 72);
        _strings = new(budget);
    }

    internal static SvgFilterArena Create(RenderBudget budget, ParserWorkCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RenderBudget.BudgetReservation owner = budget.Reserve(512, SvgMemoryCategory.Scene);
        try { return new(budget, work, token, owner); }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Adopts a resource on success; a failed insertion leaves its array owner with the caller.</summary>
    internal void AddResource(string id, SvgFilterResource resource)
    {
        if (_sealed || _disposed || _resourceSource is not null) throw new InvalidOperationException("Filter resource construction is closed.");
        BudgetedParserDictionary<string, int> names = _names ?? throw new InvalidOperationException("Filter names are unavailable.");
        BudgetedParserStringStore strings = _strings ?? throw new InvalidOperationException("Filter strings are unavailable.");
        _work.Consume(_token);
        if (!names.TryAdd(strings.Copy(id), _resources.Count)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG filter ids must be unique.");
        _resources.Add(resource);
    }

    internal int AddBinding(string id, SvgPaintBounds objectBounds, SvgPaintBounds visualBounds,
        SvgAffineTransform transform, SvgViewportLengthBases lengthBases, bool groupSource, string? clipId)
    {
        if (_sealed || _disposed) throw new InvalidOperationException("Filter construction is closed.");
        BudgetedParserStringStore strings = _strings ?? throw new InvalidOperationException("Filter strings are unavailable.");
        int index = _bindings.Count;
        _bindings.Add(new(strings.Copy(id), -1, objectBounds, visualBounds, transform, lengthBases)
        { GroupSource = groupSource, ClipReferenceId = clipId is null ? null : strings.Copy(clipId) });
        HasGroupSources |= groupSource;
        _hasPendingClips |= clipId is not null;
        return index;
    }

    /// <summary>Adopts a retained placement into this scene, preserving its document resource identity.</summary>
    internal int AddBinding(SvgFilterBinding binding)
    {
        if (_sealed || _disposed) throw new InvalidOperationException("Filter construction is closed.");
        if (binding.ReferenceId is {} id)
            return AddBinding(id, binding.ObjectBounds, binding.VisualBounds, binding.Transform, binding.LengthBases, true, null);
        int index = _bindings.Count;
        _bindings.Add(binding with { GroupSource = true, ClipReferenceId = null, OutputClip = null });
        HasGroupSources = true;
        return index;
    }

    /// <summary>Borrows document-owned definitions while this scene owns its placed bindings.</summary>
    /// <remarks>The document outlives all child scenes. Bindings seal after definition collection and before
    /// the document arena seals. Playback borrows immutable resources; child disposal never releases them.</remarks>
    internal void BorrowResourcesFrom(SvgFilterArena source)
    {
        if (_sealed || _disposed || ReferenceEquals(source, this) || _resources.Count != 0
            || _resourceSource is not null && !ReferenceEquals(_resourceSource, source))
            throw new InvalidOperationException("A child filter arena requires one document resource owner.");
        _resourceSource = source;
    }

    /// <summary>Resolves forward output clips through the document's existing bounded clip registry before emission.</summary>
    /// <remarks>Bindings borrow parser-owned operations. Command emission adopts native clip content into the scene;
    /// sealing drops these borrowed references before parser disposal. Work, cancellation and owners are shared.</remarks>
    internal void ResolveOutputClips(SvgDocumentState state, SvgSceneBuilder scene)
    {
        if (!_hasPendingClips) return;
        if (_sealed || _disposed) throw new InvalidOperationException("Filter construction is closed.");
        using BudgetedParserList<SourceRange> ranges = new(state.Budget, SvgMemoryCategory.Parser, _work, _token);
        bool indexed = false;
        for (int index = 0; index < _bindings.Count; index++)
        {
            _work.Consume(_token);
            SvgFilterBinding binding = _bindings[index];
            if (binding.ClipReferenceId is not {} id) continue;
            if (SvgClipPathSyntax.IsDisabled(id, state))
            {
                _bindings[index] = binding with { ClipReferenceId = null, OutputClip = null };
                continue;
            }
            SvgLeafMaskBinding? clip;
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = scene;
            try
            {
                if (state.ObjectClips.TryGetValue(id, out SvgClipBox fractions))
                    clip = SvgLeafMaskComposition.NewBinding(state, TargetBounds().Register(fractions, state, _work, _token), null);
                else if (state.ClipPolygons.TryGetValue(id, out SvgClipPolygon polygon) && polygon.IsObjectBoundingBox)
                    clip = SvgLeafMaskComposition.NewBinding(state, TargetBounds().Register(polygon, state, _work, _token), null);
                else clip = SvgClipPathSyntax.BindResourceClip(id, binding.Transform, null, state, _work, _token);
            }
            finally { state.DefinitionReplayScene = previous; }
            _bindings[index] = binding with { ClipReferenceId = null, OutputClip = clip };

            SvgObjectBoundingClip TargetBounds()
            {
                SvgObjectBoundingClip target = new(binding.Transform, null);
                if (!indexed) IndexSources();
                SourceRange range = ranges[index];
                if (range.First < 0 || range.End < 0)
                    throw new SvgException(SvgErrorCode.MalformedInput, "Filter output clip requires a balanced source scope.");
                for (int commandIndex = range.First + 1; commandIndex < range.End; commandIndex++)
                {
                    _work.Consume(_token);
                    SvgDrawCommand command = scene.CommandAt(commandIndex);
                    if (command.Kind == SvgDrawCommandKind.Geometry)
                    {
                        SvgGeometry geometry = scene.GeometryAt(command.GeometryIndex);
                        if (!scene.GetRecordedPaintValue(geometry.RecordedPaint).IsMarkerContent)
                            target.Add(geometry, state, _work, _token);
                    }
                }
                return target;
            }
        }
        _hasPendingClips = false;

        // Object-box clips need native geometry bounds within their own source.
        // Index balanced boundaries once, only when such a clip is encountered.
        void IndexSources()
        {
            for (int index = 0; index < _bindings.Count; index++) ranges.Add(new(-1, -1));
            using BudgetedParserList<int> stack = new(state.Budget, SvgMemoryCategory.Parser, _work, _token);
            for (int commandIndex = 0; commandIndex < scene.CommandCount; commandIndex++)
            {
                _work.Consume(_token);
                SvgDrawCommand command = scene.CommandAt(commandIndex);
                if (command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    int selected = -1;
                    if (command.RegionKind == SvgLayerRegionKind.Filter)
                    {
                        int binding = command.MaskContentIndex;
                        if ((uint)binding >= (uint)_bindings.Count)
                            throw new SvgException(SvgErrorCode.MalformedInput, "Filter output clip requires a valid source binding.");
                        // Match the original first-source rule if a binding is reused.
                        if (ranges[binding].First < 0)
                        {
                            ranges[binding] = new(commandIndex, -1);
                            selected = binding;
                        }
                    }
                    stack.Add(selected);
                }
                else if (command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (stack.Count == 0) throw new SvgException(SvgErrorCode.MalformedInput, "Filter output clip requires a balanced source scope.");
                    int binding = stack[^1];
                    stack.RemoveLast();
                    if (binding >= 0) ranges[binding] = ranges[binding] with { End = commandIndex };
                }
            }
            if (stack.Count != 0) throw new SvgException(SvgErrorCode.MalformedInput, "Filter output clip requires a balanced source scope.");
            indexed = true;
        }
    }

    /// <summary>Borrows a resolved output clip while command emission is still constructing this scene.</summary>
    internal SvgLeafMaskBinding? OutputClipAt(int index)
    {
        if (_sealed || _disposed || (uint)index >= (uint)_bindings.Count) throw new InvalidOperationException("Filter clip binding is unavailable.");
        return _bindings[index].OutputClip;
    }

    /// <summary>Identifies a source boundary while the owning scene finalizes its drawing ranges.</summary>
    internal bool IsGroupSource(int index) => _bindings[index].GroupSource;

    /// <summary>Completes a group source's aggregate bounds before immutable resources are sealed.</summary>
    internal void SetGroupBounds(int index, SvgPaintBounds objectBounds, SvgPaintBounds visualBounds)
    {
        if (_sealed || _disposed) throw new InvalidOperationException("Filter construction is closed.");
        _work.Consume(_token);
        _bindings[index] = _bindings[index] with { ObjectBounds = objectBounds, VisualBounds = visualBounds };
    }

    /// <summary>Validates a borrowed local resource ID before sealing, charging work/cancellation without creating a binding.</summary>
    internal int ResolveReference(string id)
    {
        _work.Consume(_token);
        SvgFilterArena definitions = _resourceSource ?? this;
        BudgetedParserDictionary<string, int> names = definitions._names ?? throw new InvalidOperationException("Filter names are unavailable.");
        if (!names.TryGetValue(id, out int resource))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local filter references are implemented.");
        if (definitions._resources[resource].Rejection is {} rejected) throw new SvgException(rejected.Code, rejected.Message);
        return resource;
    }

    internal void Seal()
    {
        if (_sealed) return;
        if (_hasPendingClips) throw new InvalidOperationException("Filter output clips require command emission before sealing.");
        SvgFilterArena definitions = _resourceSource ?? this;
        for (int index = 0; index < _bindings.Count; index++)
        {
            _work.Consume(_token);
            SvgFilterBinding binding = _bindings[index];
            int resource = binding.ResourceIndex;
            if (resource < 0) resource = binding.ReferenceId is {} id ? ResolveReference(id)
                : throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local filter references are implemented.");
            if ((uint)resource >= (uint)definitions._resources.Count) throw new InvalidOperationException("Retained filter resource is unavailable.");
            if (definitions._resources[resource].Rejection is {} rejected) throw new SvgException(rejected.Code, rejected.Message);
            _bindings[index] = binding with { ReferenceId = null, ResourceIndex = resource, OutputClip = null };
        }
        _names?.Dispose();
        _names = null;
        _strings?.Dispose();
        _strings = null;
        _sealed = true;
    }

    internal SvgFilterBinding BindingAt(int index)
    {
        if (!_sealed || _disposed || (uint)index >= (uint)_bindings.Count) throw new InvalidOperationException("Filter binding is unavailable.");
        return _bindings[index];
    }

    internal SvgFilterResource ResourceAt(int index)
    {
        if (!_sealed || _disposed) throw new InvalidOperationException("Filter resource is unavailable.");
        if (_resourceSource is {} source) return source.ResourceAt(index);
        if ((uint)index >= (uint)_resources.Count) throw new InvalidOperationException("Filter resource is unavailable.");
        return _resources[index];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (SvgFilterResource resource in _resources.Items) resource.Dispose();
        _resources.Dispose();
        _bindings.Dispose();
        _names?.Dispose();
        _strings?.Dispose();
        _resourceSource = null;
        _names = null;
        _strings = null;
        _owner.Dispose();
    }
}
