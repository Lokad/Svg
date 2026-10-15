using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgLeafMaskOwner(IDisposable Owner);
internal readonly record struct SvgLeafMaskOperationReference(SvgLeafMaskOperation Operation);
internal readonly record struct SvgLeafMaskBindingReference(SvgLeafMaskBinding Binding);
internal readonly record struct SvgFilterClipScope(int Depth, SvgLeafMaskBinding Binding);
internal readonly record struct SvgNativeClipReference(string Id, SvgAffineTransform Map);

/// <summary>Stores one parser-owned mask value/region, or a lazily mapped instance of an earlier operation.</summary>
/// <remarks>Shared within one conversion; mutation is single-threaded. Bindings borrow operations.
/// The document registry owns reservations, and copied draw commands retain values after metadata clears.</remarks>
internal sealed class SvgLeafMaskOperation : IDisposable
{
    private readonly RenderBudget.BudgetReservation _reservation;
    internal SvgLeafMaskOperation(RenderBudget.BudgetReservation reservation, SvgLeafMaskOperation? source, SvgAffineTransform map)
    {
        _reservation = reservation;
        Source = source;
        Map = map;
    }
    internal SvgLeafMaskOperation? Source { get; }
    internal SvgAffineTransform Map { get; }
    internal bool Resolved { get; set; }
    internal byte Value { get; set; }
    internal SvgPaintOpacity SourceOpacity { get; set; } = SvgPaintOpacity.Opaque;
    internal SvgRecordingScope RecordingScope { get; set; } = new(-1);
    internal SvgClipBox? Region { get; set; }
    internal SvgLayerRegionKind RegionKind { get; set; }
    internal SvgAffineViewportClip? AffineViewport { get; set; }
    /// <summary>Borrows a parser-owned clip id until native content is emitted after instance placement.</summary>
    internal SvgNativeClipReference? NativeClip { get; set; }
    internal int ContentIndex { get; set; } = -1;
    /// <summary>Releases this metadata reservation without disposing borrowed operations or observing cancellation.</summary>
    /// <remarks>Only the conversion owner disposes this single-threaded value; budget release is idempotent.</remarks>
    public void Dispose() => _reservation.Dispose();
}

/// <summary>Borrows an ordered outer operation and optional inner mask chain for one lowered paint.</summary>
/// <remarks>The parser registry owns the node. Instance copying preserves sharing within one paint batch
/// and creates distinct nodes/operations between batches; geometry never disposes these borrowed links.</remarks>
internal sealed class SvgLeafMaskBinding : IDisposable
{
    private readonly RenderBudget.BudgetReservation _reservation;
    internal SvgLeafMaskBinding(RenderBudget.BudgetReservation reservation, SvgLeafMaskOperation operation, SvgLeafMaskBinding? inner)
    {
        _reservation = reservation;
        Operation = operation;
        Inner = inner;
    }
    internal SvgLeafMaskOperation Operation { get; }
    internal SvgLeafMaskBinding? Inner { get; }
    /// <summary>Releases this node reservation while keeping borrowed inner nodes/operations untouched.</summary>
    /// <remarks>Cleanup is single-threaded and unconditional; cancellation never interrupts budget release.</remarks>
    public void Dispose() => _reservation.Dispose();
}

/// <summary>Copies mask identities through one instance placement using bounded, cancellable iterative caches.</summary>
/// <remarks>The mapper owns only its scratch/cache storage. New operations/nodes belong to the
/// document registry; mapped chains remain valid after mapper disposal. Instances use separate mappers.</remarks>
internal sealed class SvgLeafMaskMapper : IDisposable
{
    private const long OwnerBytes = 768;
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly SvgDocumentState _state;
    private readonly SvgAffineTransform _map;
    private SvgRecordingMapper? _recordingMapper;
    private readonly ParserWorkCounter _work;
    private readonly CancellationToken _token;
    private readonly BudgetedParserDictionary<SvgLeafMaskOperation, SvgLeafMaskOperationReference> _operations;
    private readonly BudgetedParserDictionary<SvgLeafMaskBinding, SvgLeafMaskBindingReference> _bindings;
    private readonly BudgetedParserList<SvgLeafMaskBindingReference> _stack;
    private SvgLeafMaskMapper(SvgDocumentState state, SvgAffineTransform map, ParserWorkCounter work, CancellationToken token, RenderBudget.BudgetReservation owner)
    {
        _owner = owner;
        _state = state;
        _map = map;
        _work = work;
        _token = token;
        _operations = new(state.Budget, SvgMemoryCategory.Parser, 96);
        _bindings = new(state.Budget, SvgMemoryCategory.Parser, 96);
        _stack = new(state.Budget, SvgMemoryCategory.Parser, work, token);
    }
    /// <summary>Reserves mapper/cache owners before allocation; dictionary entries and stack arrays charge growth separately.</summary>
    internal static SvgLeafMaskMapper Create(SvgDocumentState state, SvgAffineTransform map, ParserWorkCounter work, CancellationToken token)
    {
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(OwnerBytes, SvgMemoryCategory.Parser);
        try { return new(state, map, work, token, owner); }
        catch { owner.Dispose(); throw; }
    }

    internal SvgLeafMaskMapper WithRecordingMapper(SvgRecordingMapper mapper)
    {
        _recordingMapper = mapper;
        return this;
    }

    /// <summary>Returns a borrowed mapped chain without recursion, charging visited nodes and cache growth before use.</summary>
    internal SvgLeafMaskBinding? Map(SvgLeafMaskBinding? source)
    {
        if (source is null) return null;
        _stack.Clear();
        SvgLeafMaskBinding? inner = null;
        for (SvgLeafMaskBinding? current = source; current is not null; current = current.Inner)
        {
            _work.Consume(_token);
            if (_bindings.TryGetValue(current, out SvgLeafMaskBindingReference cached)) { inner = cached.Binding; break; }
            _stack.Add(new(current));
        }
        for (int index = _stack.Count - 1; index >= 0; index--)
        {
            _work.Consume(_token);
            SvgLeafMaskBinding original = _stack[index].Binding;
            if (!_operations.TryGetValue(original.Operation, out SvgLeafMaskOperationReference operation))
            {
                operation = new(SvgLeafMaskComposition.NewOperation(_state, original.Operation, _map));
                operation.Operation.RecordingScope = _recordingMapper is null ? original.Operation.RecordingScope : _recordingMapper.Map(original.Operation.RecordingScope);
                _operations.TryAdd(original.Operation, operation);
            }
            inner = SvgLeafMaskComposition.NewBinding(_state, operation.Operation, inner);
            _bindings.TryAdd(original, new(inner));
        }
        return inner;
    }
    /// <summary>Releases scratch/cache arrays and their owners, preserving registry-owned mapped chains.</summary>
    /// <remarks>Single-threaded cleanup does not observe cancellation and may run after partial mapping failures.</remarks>
    public void Dispose()
    {
        _operations.Dispose();
        _bindings.Dispose();
        _stack.Dispose();
        _owner.Dispose();
    }
}

/// <summary>Retains per-element mask effects until clipping/instancing finishes, then emits bounded isolated scopes.</summary>
internal static class SvgLeafMaskComposition
{
    /// <summary>Reserves and registers an operation before returning a borrowed reference; registry teardown releases partial work.</summary>
    internal static SvgLeafMaskOperation NewOperation(SvgDocumentState state, SvgLeafMaskOperation? source, SvgAffineTransform map)
    {
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(256, SvgMemoryCategory.Parser);
        try
        {
            SvgLeafMaskOperation operation = new(reservation, source, map);
            state.LeafMaskOwners.Add(new(operation));
            return operation;
        }
        catch { reservation.Dispose(); throw; }
    }
    /// <summary>Reserves and registers a borrowed chain node without transferring ownership of its operation/inner link.</summary>
    internal static SvgLeafMaskBinding NewBinding(SvgDocumentState state, SvgLeafMaskOperation operation, SvgLeafMaskBinding? inner)
    {
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(128, SvgMemoryCategory.Parser);
        try
        {
            SvgLeafMaskBinding binding = new(reservation, operation, inner);
            state.LeafMaskOwners.Add(new(binding));
            return binding;
        }
        catch { reservation.Dispose(); throw; }
    }
    /// <summary>Tags a leaf's entire geometry batch with one operation; resolution keeps its original range for coverage/bounds.</summary>
    internal static void Retain(SvgDocumentState state, int first, int count, string id, SvgViewportFrame? parent, SvgAffineTransform transform, ParserWorkCounter work, CancellationToken token)
    {
        SvgLeafMaskOperation operation = NewOperation(state, null, SvgAffineTransform.Identity);
        state.PendingLeafMasks.Add(new(first, count, id, parent, transform, operation));
        SvgLeafMaskBinding binding = NewBinding(state, operation, null);
        for (int index = first; index < first + count; index++)
        {
            work.Consume(token);
            state.Scene.AssignLeafMask(index, binding);
        }
    }
    /// <summary>Preserves the existing fully-contained stroke policy for rectangle-based geometries.</summary>
    internal static void ValidateRegion(SvgGeometry geometry, SvgClipBox region)
    {
        SvgRectangle? fill = geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => geometry.Rectangle,
            SvgGeometryKind.PatternFill => geometry.Pattern.Fill,
            SvgGeometryKind.GradientFill => geometry.Gradient.Fill,
            SvgGeometryKind.RadialGradient => geometry.Radial.Fill,
            _ => null,
        };
        SvgStroke? stroke = geometry.Kind switch
        {
            SvgGeometryKind.Rectangle => geometry.Rectangle.Stroke,
            SvgGeometryKind.PatternFill => geometry.Pattern.Stroke,
            SvgGeometryKind.GradientFill => geometry.Gradient.Stroke,
            SvgGeometryKind.RadialGradient => geometry.Radial.Stroke,
            _ => null,
        };
        if (fill is SvgRectangle box && stroke is SvgStroke outline)
        {
            double half = outline.Width / 2d;
            if (!region.Contains(box.X - half, box.Y - half, box.X + box.Width + half, box.Y + box.Height + half)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a mask region boundary are not implemented.");
        }
    }
    /// <summary>Builds replacement commands under reserved storage and bounded work, preserving instance and sibling boundaries.</summary>
    /// <remarks>Operations resolve iteratively. On success the scene adopts commands and every geometry
    /// drops its parser-owned link before registry disposal; failure releases staging and the scene owner
    /// releases any adopted partial result. Group validation runs before these leaf scopes appear.</remarks>
    internal static void Emit(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        => Emit(state, state.Scene, work, token);

    /// <summary>Compiles leaf scopes into the specified scene, retaining the document's bounded shared operation registry.</summary>
    internal static void Emit(SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        scene.ResolveFilterOutputClips(state);
        if (state.LeafMaskOwners.Count == 0) return;
        BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, work, token);
        bool transferred = false;
        int depth = 0;
        BudgetedParserList<SvgFilterClipScope>? filterClips = null;
        using BudgetedParserList<SvgLeafMaskOperationReference> active = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgLeafMaskOperationReference> desired = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<SvgLeafMaskOperationReference> unresolved = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        try
        {
            int count = scene.CommandCount;
            for (int index = 0; index <= count; index++)
            {
                work.Consume(token);
                desired.Clear();
                SvgDrawCommand command = index < count ? scene.CommandAt(index) : default;
                if (index < count && command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    depth++;
                    if (command.RegionKind == SvgLayerRegionKind.Filter && scene.FilterOutputClipAt(command.MaskContentIndex) is {} clip)
                    {
                        filterClips ??= new(state.Budget, SvgMemoryCategory.Parser, work, token);
                        filterClips.Add(new(depth, clip));
                    }
                }
                if (filterClips is not null)
                    foreach (SvgFilterClipScope scope in filterClips.AsSpan()) AppendBindings(scope.Binding);
                if (index < count && command.Kind == SvgDrawCommandKind.Geometry)
                    AppendBindings(scene.GeometryAt(command.GeometryIndex).LeafMask);
                int common = 0;
                while (common < active.Count && common < desired.Count && ReferenceEquals(active[common].Operation, desired[common].Operation))
                {
                    work.Consume(token);
                    common++;
                }
                for (int end = active.Count; end > common; end--) commands.Add(new(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255));
                for (int begin = common; begin < desired.Count; begin++)
                {
                    SvgLeafMaskOperation operation = desired[begin].Operation;
                    commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, operation.RecordingScope.Value, operation.SourceOpacity, operation.Region, operation.ContentIndex, operation.Value) { RegionKind = operation.RegionKind });
                }
                active.Clear();
                active.AddRange(desired.AsSpan());
                if (index < count) commands.Add(command);
                if (index < count && command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    if (filterClips is { Count: > 0 } && filterClips[filterClips.Count - 1].Depth == depth) filterClips.RemoveLast();
                    depth--;
                }
            }
            scene.AdoptCommands(commands);
            transferred = true;
            for (int index = 0; index < scene.GeometryCount; index++)
            {
                work.Consume(token);
                scene.AssignLeafMask(index, null);
            }
        }
        finally { filterClips?.Dispose(); if (!transferred) commands.Dispose(); }

        void AppendBindings(SvgLeafMaskBinding? first)
        {
            for (SvgLeafMaskBinding? binding = first; binding is not null; binding = binding.Inner)
            {
                work.Consume(token);
                SvgLeafMaskOperation operation = binding.Operation;
                unresolved.Clear();
                for (SvgLeafMaskOperation? current = operation; current is not null && !current.Resolved; current = current.Source)
                {
                    work.Consume(token);
                    unresolved.Add(new(current));
                }
                for (int pending = unresolved.Count - 1; pending >= 0; pending--)
                {
                    work.Consume(token);
                    SvgLeafMaskOperation mapped = unresolved[pending].Operation;
                    SvgLeafMaskOperation source = mapped.Source ?? throw new InvalidOperationException("Leaf mask was not resolved.");
                    mapped.Value = source.Value;
                    mapped.SourceOpacity = source.SourceOpacity;
                    mapped.RegionKind = source.RegionKind;
                    if (source.NativeClip is {} native)
                        mapped.NativeClip = native with { Map = mapped.Map.ComposeChecked(native.Map) };
                    if (source.AffineViewport is {} affine)
                        mapped.AffineViewport = affine with { Map = mapped.Map.ComposeChecked(affine.Map) };
                    else if (source.RegionKind == SvgLayerRegionKind.Viewport && !mapped.Map.IsAxisAligned && source.Region is SvgClipBox viewport)
                    {
                        mapped.AffineViewport = new(viewport, mapped.Map);
                        mapped.RegionKind = SvgLayerRegionKind.AffineViewport;
                    }
                    else mapped.Region = source.Region is SvgClipBox region ? mapped.Map.Apply(region) : null;
                    mapped.Resolved = true;
                }
                if (operation.AffineViewport is {} clip && operation.ContentIndex < 0)
                    (operation.Region, operation.ContentIndex) = clip.Register(state, scene, work, token);
                if (operation.NativeClip is {} nativeClip && operation.ContentIndex < 0)
                {
                    if (!state.ClipPolygons.TryGetValue(nativeClip.Id, out SvgClipPolygon content))
                        throw new InvalidOperationException("Retained native clip content is missing.");
                    (operation.Region, operation.ContentIndex) = SvgAffineViewportClip.RegisterContour(content.Points, content.ContourEnds,
                        nativeClip.Map, content.IsOval ? SvgClipContentKind.OvalClipPath : SvgClipContentKind.ClipPath,
                        content.UsesNativeReplay ? content.SourcePath : null, content.ResourceBounds, content.IsEvenOdd, state, scene, work, token);
                }
                if (operation.Value != 255 || operation.Region is not null || operation.ContentIndex >= 0 || operation.SourceOpacity.Value != 1) desired.Add(new(operation));
            }
        }
    }
}
