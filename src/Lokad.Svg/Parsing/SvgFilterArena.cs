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
    internal OwnedSceneArray<SvgFilterInput> MergeInputs { get; init; } = new([], null);
    public void Dispose() { MergeInputs.Dispose(); Primitives.Dispose(); }
}
internal readonly record struct SvgFilterBinding(string? ReferenceId, int ResourceIndex, SvgPaintBounds ObjectBounds,
    SvgPaintBounds VisualBounds, SvgAffineTransform Transform, SvgViewportLengthBases LengthBases)
{
    internal bool GroupSource { get; init; }
}

/// <summary>Owns filter resources and placed bindings until its scene is disposed.</summary>
/// <remarks>Construction is single-threaded and budgeted. Sealing resolves references and drops parser-only names.
/// Sealed resources are immutable and borrowed by playback; disposal is owned by the enclosing scene.</remarks>
internal sealed class SvgFilterArena : IDisposable
{
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly BudgetedSceneList<SvgFilterResource> _resources;
    private readonly BudgetedSceneList<SvgFilterBinding> _bindings;
    private BudgetedParserDictionary<string, int>? _names;
    private BudgetedParserStringStore? _strings;
    private bool _sealed;
    private bool _disposed;
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
        if (_sealed || _disposed) throw new InvalidOperationException("Filter construction is closed.");
        BudgetedParserDictionary<string, int> names = _names ?? throw new InvalidOperationException("Filter names are unavailable.");
        BudgetedParserStringStore strings = _strings ?? throw new InvalidOperationException("Filter strings are unavailable.");
        _work.Consume(_token);
        if (!names.TryAdd(strings.Copy(id), _resources.Count)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG filter ids must be unique.");
        _resources.Add(resource);
    }

    internal int AddBinding(string id, SvgPaintBounds objectBounds, SvgPaintBounds visualBounds,
        SvgAffineTransform transform, SvgViewportLengthBases lengthBases, bool groupSource)
    {
        if (_sealed || _disposed) throw new InvalidOperationException("Filter construction is closed.");
        BudgetedParserStringStore strings = _strings ?? throw new InvalidOperationException("Filter strings are unavailable.");
        int index = _bindings.Count;
        _bindings.Add(new(strings.Copy(id), -1, objectBounds, visualBounds, transform, lengthBases) { GroupSource = groupSource });
        HasGroupSources |= groupSource;
        return index;
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

    internal void Seal()
    {
        if (_sealed) return;
        BudgetedParserDictionary<string, int> names = _names ?? throw new InvalidOperationException("Filter names are unavailable.");
        for (int index = 0; index < _bindings.Count; index++)
        {
            _work.Consume(_token);
            SvgFilterBinding binding = _bindings[index];
            string id = binding.ReferenceId ?? throw new InvalidOperationException("Filter binding requires a name.");
            if (!names.TryGetValue(id, out int resource)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local filter references are implemented.");
            if (_resources[resource].Rejection is {} rejected) throw new SvgException(rejected.Code, rejected.Message);
            _bindings[index] = binding with { ReferenceId = null, ResourceIndex = resource };
        }
        _names.Dispose();
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
        if (!_sealed || _disposed || (uint)index >= (uint)_resources.Count) throw new InvalidOperationException("Filter resource is unavailable.");
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
        _names = null;
        _strings = null;
        _owner.Dispose();
    }
}
