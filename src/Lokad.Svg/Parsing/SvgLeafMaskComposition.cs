using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgLeafMaskOwner(IDisposable Owner);
internal readonly record struct SvgLeafMaskOperationReference(SvgLeafMaskOperation Operation);
internal readonly record struct SvgLeafMaskBindingReference(SvgLeafMaskBinding Binding);
internal readonly record struct SvgFilterClipScope(int Depth, SvgLeafMaskBinding Binding);
/// <summary>Borrows a clip resource id and retains resource placement separately from its referencing canvas.</summary>
/// <remarks>Object-box normalization changes Map only. Instance transfer composes both maps;
/// immutable values own no buffers, and their containing parser metadata accounts for storage.</remarks>
internal readonly record struct SvgNativeClipReference(string Id, SvgAffineTransform Map, SvgAffineTransform CanvasMap);
/// <summary>Borrows a mask id and pre-placement target bounds until an instance registers content in its final scene.</summary>
internal readonly record struct SvgRetainedMask(string Id, SvgAffineTransform Map, SvgClipBox ObjectBounds, SvgPaintBounds LocalVisualBounds)
{
    // Local visual bounds remain independent of normalized object bounds while instances compose Map.
    /// <summary>Retains scalar coverage while later instances recompute their own paint clip.</summary>
    internal byte? ConstantAlpha { get; init; }
}

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
    /// <summary>Preserves an authored isolation layer when its numerical opacity/mask values are identity.</summary>
    internal bool PreservesIsolation { get; set; }
    internal bool Resolved { get; set; }
    internal byte Value { get; set; }
    internal SvgPaintOpacity SourceOpacity { get; set; } = SvgPaintOpacity.Opaque;
    internal SvgRecordingScope RecordingScope { get; set; } = new(-1);
    internal SvgClipBox? Region { get; set; }
    internal SvgLayerRegionKind RegionKind { get; set; }
    internal SvgAffineViewportClip? AffineViewport { get; set; }
    /// <summary>Borrows a parser-owned clip id until native content is emitted after instance placement.</summary>
    internal SvgNativeClipReference? NativeClip { get; set; }
    /// <summary>Reconstructs spatial mask content after instance mapping instead of copying another scene's content index.</summary>
    internal SvgRetainedMask? NativeMask { get; set; }
    internal SvgRetainedFilter? Filter { get; set; }
    internal int ContentIndex { get; set; } = -1;
    /// <summary>Borrows an enclosing native clip operation owned by this document registry.</summary>
    internal SvgLeafMaskOperation? EnclosingClip { get; set; }
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
    private readonly record struct SceneTransfer(SvgSceneBuilder Source, SvgSceneBuilder Destination);
    private readonly SceneTransfer? _transfer;
    private readonly BudgetedParserDictionary<int, int>? _contents;
    private readonly BudgetedParserList<int>? _contentStack;
    private readonly RenderBudget.BudgetReservation _owner;
    private readonly SvgDocumentState _state;
    private readonly SvgAffineTransform _map;
    private SvgRecordingMapper? _recordingMapper;
    private readonly CancellationToken _token;
    private readonly BudgetedParserDictionary<SvgLeafMaskOperation, SvgLeafMaskOperationReference> _operations;
    private readonly BudgetedParserDictionary<SvgLeafMaskBinding, SvgLeafMaskBindingReference> _bindings;
    private readonly BudgetedParserList<SvgLeafMaskBindingReference> _stack;
    private SvgLeafMaskMapper(SvgDocumentState state, SvgAffineTransform map, CancellationToken token, RenderBudget.BudgetReservation owner, SceneTransfer? transfer)
    {
        _owner = owner;
        _transfer = transfer;
        if (transfer is not null)
        {
            _contents = new(state.Budget, SvgMemoryCategory.Parser, 96);
            _contentStack = new(state.Budget, SvgMemoryCategory.Parser, token);
        }
        _state = state;
        _map = map;
        _token = token;
        _operations = new(state.Budget, SvgMemoryCategory.Parser, 96);
        _bindings = new(state.Budget, SvgMemoryCategory.Parser, 96);
        _stack = new(state.Budget, SvgMemoryCategory.Parser, token);
    }
    /// <summary>Reserves mapper/cache owners before allocation; dictionary entries and stack arrays charge growth separately.</summary>
    internal static SvgLeafMaskMapper Create(SvgDocumentState state, SvgAffineTransform map, CancellationToken token)
    {
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(OwnerBytes, SvgMemoryCategory.Parser);
        try { return new(state, map, token, owner, null); }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Rebinds borrowed scene-local effects without changing their placement or source paint lookup.</summary>
    /// <remarks>The destination owns copied content. The document owns mapped operations/bindings;
    /// this single-threaded mapper owns only bounded caches. Cancellation cover every transfer,
    /// and disposal releases scratch after success or failure without disposing either scene.</remarks>
    internal static SvgLeafMaskMapper CreateSceneTransfer(SvgDocumentState state, SvgSceneBuilder source,
        SvgSceneBuilder destination, CancellationToken token)
    {
        RenderBudget.BudgetReservation owner = state.Budget.Reserve(OwnerBytes + 256, SvgMemoryCategory.Parser);
        try { return new(state, SvgAffineTransform.Identity, token, owner, new(source, destination)); }
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
            _token.ThrowIfCancellationRequested();
            if (_bindings.TryGetValue(current, out SvgLeafMaskBindingReference cached)) { inner = cached.Binding; break; }
            _stack.Add(new(current));
        }
        for (int index = _stack.Count - 1; index >= 0; index--)
        {
            _token.ThrowIfCancellationRequested();
            SvgLeafMaskBinding original = _stack[index].Binding;
            if (!_operations.TryGetValue(original.Operation, out SvgLeafMaskOperationReference operation))
            {
                operation = new(SvgLeafMaskComposition.NewOperation(_state, original.Operation, _map));
                operation.Operation.RecordingScope = _recordingMapper is null ? original.Operation.RecordingScope : _recordingMapper.Map(original.Operation.RecordingScope);
                _operations.TryAdd(original.Operation, operation);
                if (_transfer is {} transfer)
                {
                    for (SvgLeafMaskOperation? current = original.Operation; current is not null; current = current.EnclosingClip)
                    {
                        _token.ThrowIfCancellationRequested();
                        if (!_operations.TryGetValue(current, out SvgLeafMaskOperationReference mapped))
                        {
                            mapped = new(SvgLeafMaskComposition.NewOperation(_state, current, SvgAffineTransform.Identity));
                            mapped.Operation.RecordingScope = current.RecordingScope;
                            _operations.TryAdd(current, mapped);
                        }
                        mapped.Operation.Region = current.Region;
                        if (current.Resolved && current.ContentIndex >= 0 && current.Filter is null)
                            mapped.Operation.ContentIndex = TransferContent(current.ContentIndex, transfer);
                        if (current.EnclosingClip is {} enclosing)
                        {
                            if (!_operations.TryGetValue(enclosing, out SvgLeafMaskOperationReference copied))
                            {
                                copied = new(SvgLeafMaskComposition.NewOperation(_state, enclosing, SvgAffineTransform.Identity));
                                copied.Operation.RecordingScope = enclosing.RecordingScope;
                                _operations.TryAdd(enclosing, copied);
                            }
                            mapped.Operation.EnclosingClip = copied.Operation;
                        }
                    }
                }
            }
            inner = SvgLeafMaskComposition.NewBinding(_state, operation.Operation, inner);
            _bindings.TryAdd(original, new(inner));
        }
        return inner;

        int TransferContent(int contentIndex, SceneTransfer transfer)
        {
            BudgetedParserDictionary<int, int> contents = _contents ?? throw new InvalidOperationException("Scene transfer requires its content cache.");
            BudgetedParserList<int> pending = _contentStack ?? throw new InvalidOperationException("Scene transfer requires its traversal stack.");
            if (contents.TryGetValue(contentIndex, out int existing)) return existing;
            pending.Clear();
            for (int index = contentIndex; index >= 0 && !contents.TryGetValue(index, out _);)
            {
                _token.ThrowIfCancellationRequested();
                pending.Add(index);
                int bounds = transfer.Source.MaskContentAt(index).BoundsClipContentIndex;
                if (bounds >= index) throw new InvalidOperationException("A transferred bounds clip must precede its content.");
                index = bounds;
            }
            for (int index = pending.Count - 1; index >= 0; index--)
            {
                _token.ThrowIfCancellationRequested();
                SvgMaskContent content = transfer.Source.MaskContentAt(pending[index]);
                int bounds = content.BoundsClipContentIndex;
                int mappedBounds = -1;
                if (bounds >= 0 && !contents.TryGetValue(bounds, out mappedBounds))
                    throw new InvalidOperationException("A transferred bounds clip must be registered first.");
                int mapped = transfer.Destination.AddMaskContent(content with { BoundsClipContentIndex = mappedBounds });
                contents.TryAdd(pending[index], mapped);
            }
            if (!contents.TryGetValue(contentIndex, out int result))
                throw new InvalidOperationException("Scene transfer did not register its mask content.");
            return result;
        }
    }
    /// <summary>Releases scratch/cache arrays and their owners, preserving registry-owned mapped chains.</summary>
    /// <remarks>Single-threaded cleanup does not observe cancellation and may run after partial mapping failures.</remarks>
    public void Dispose()
    {
        _contents?.Dispose();
        _contentStack?.Dispose();
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
        // Aggregate allowance includes retained resource/target metadata and its scalar coverage,
        // including 48 bytes for the clip's separate referencing canvas transform.
        RenderBudget.BudgetReservation reservation = state.Budget.Reserve(464, SvgMemoryCategory.Parser);
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
    internal static void Retain(SvgDocumentState state, int first, int count, string id, SvgViewportFrame? parent, SvgAffineTransform transform, CancellationToken token)
    {
        SvgLeafMaskOperation operation = NewOperation(state, null, SvgAffineTransform.Identity);
        state.PendingLeafMasks.Add(new(first, count, id, parent, transform, operation, state.Scene));
        SvgLeafMaskBinding binding = NewBinding(state, operation, null);
        for (int index = first; index < first + count; index++)
        {
            token.ThrowIfCancellationRequested();
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
    /// <summary>Builds replacement commands under reserved storage with cancellation checks, preserving instance and sibling boundaries.</summary>
    /// <remarks>Operations resolve iteratively. On success the scene adopts commands and every geometry
    /// drops its parser-owned link before registry disposal; failure releases staging and the scene owner
    /// releases any adopted partial result. Group validation runs before these leaf scopes appear.</remarks>
    internal static void Emit(SvgDocumentState state, CancellationToken token)
        => Emit(state, state.Scene, token);

    /// <summary>Compiles leaf scopes into the specified scene, retaining the document's bounded shared operation registry.</summary>
    internal static void Emit(SvgDocumentState state, SvgSceneBuilder scene, CancellationToken token)
    {
        scene.ResolveFilterOutputClips(state);
        if (state.LeafMaskOwners.Count == 0) return;
        BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, token);
        bool transferred = false;
        int depth = 0;
        BudgetedParserList<SvgFilterClipScope>? filterClips = null;
        using BudgetedParserList<SvgLeafMaskOperationReference> active = new(state.Budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgLeafMaskOperationReference> desired = new(state.Budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgLeafMaskOperationReference> unresolved = new(state.Budget, SvgMemoryCategory.Parser, token);
        try
        {
            int count = scene.CommandCount;
            for (int index = 0; index <= count; index++)
            {
                token.ThrowIfCancellationRequested();
                desired.Clear();
                SvgDrawCommand command = index < count ? scene.CommandAt(index) : default;
                if (index < count && command.Kind == SvgDrawCommandKind.BeginGroup)
                {
                    depth++;
                    SvgLeafMaskBinding? outputClip = null;
                    int anchorIndex = -1;
                    if (command.RegionKind == SvgLayerRegionKind.Filter)
                    {
                        anchorIndex = index + 1;
                        bool ownedMask = command.RecordingScope.Value > 0 && index > 0 && scene.CommandAt(index - 1) is { Kind: SvgDrawCommandKind.BeginGroup } owner
                            && owner.RegionKind != SvgLayerRegionKind.Filter && owner.RecordingScope == command.RecordingScope;
                        if (!ownedMask) outputClip = scene.Filters.OutputClipAt(command.MaskContentIndex);
                    }
                    else if (command.RecordingScope.Value > 0 && index + 1 < count && scene.CommandAt(index + 1) is { Kind: SvgDrawCommandKind.BeginGroup, RegionKind: SvgLayerRegionKind.Filter } filter
                        && filter.RecordingScope == command.RecordingScope)
                    {
                        // A mask and filter sharing one recording owner belong to
                        // the same element. Native clipping surrounds both layers.
                        outputClip = scene.Filters.OutputClipAt(filter.MaskContentIndex);
                        anchorIndex = index + 2;
                    }
                    // Empty filtered sources carry ancestor bindings on a non-painting
                    // anchor. Those bindings enclose the generated output, including
                    // the filter's own mask/clip composition.
                    if (anchorIndex >= 0 && anchorIndex < count && scene.CommandAt(anchorIndex) is { Kind: SvgDrawCommandKind.Geometry } anchorCommand)
                    {
                        SvgGeometry anchor = scene.GeometryAt(anchorCommand.GeometryIndex);
                        if (scene.GetRecordedPaintValue(anchor.RecordedPaint).IsEffectAnchor && anchor.LeafMask is {} ancestors)
                        {
                            bool retainedFilter = false;
                            for (SvgLeafMaskBinding? binding = ancestors; binding is not null; binding = binding.Inner)
                                for (SvgLeafMaskOperation? operation = binding.Operation; operation is not null; operation = operation.Source)
                                {
                                    token.ThrowIfCancellationRequested();
                                    retainedFilter |= operation.Filter is not null;
                                }
                            // A retained child's own filter stays inside this scope.
                            if (!retainedFilter)
                            {
                                filterClips ??= new(state.Budget, SvgMemoryCategory.Parser, token);
                                filterClips.Add(new(depth, ancestors));
                            }
                        }
                    }
                    if (outputClip is {} clip)
                    {
                        filterClips ??= new(state.Budget, SvgMemoryCategory.Parser, token);
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
                    token.ThrowIfCancellationRequested();
                    common++;
                }
                for (int end = active.Count; end > common; end--) commands.Add(new(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255));
                for (int begin = common; begin < desired.Count; begin++)
                {
                    SvgLeafMaskOperation operation = desired[begin].Operation;
                    int contentIndex = operation.ContentIndex;
                    if (operation.Filter is {} filter)
                    {
                        SvgFilterArena arena = scene.Filters;
                        if (!ReferenceEquals(arena, state.DocumentFilters)) arena.BorrowResourcesFrom(state.DocumentFilters);
                        contentIndex = arena.AddBinding(filter.Binding);
                    }
                    commands.Add(new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, operation.RecordingScope.Value, operation.SourceOpacity, operation.Region, contentIndex, operation.Value) { RegionKind = operation.RegionKind });
                }
                active.Clear();
                active.AddRange(desired.AsSpan());
                if (index < count) commands.Add(command);
                if (index < count && command.Kind == SvgDrawCommandKind.EndGroup)
                {
                    while (filterClips is { Count: > 0 } && filterClips[filterClips.Count - 1].Depth == depth) filterClips.RemoveLast();
                    depth--;
                }
            }
            scene.AdoptCommands(commands);
            transferred = true;
            for (int index = 0; index < scene.GeometryCount; index++)
            {
                token.ThrowIfCancellationRequested();
                scene.AssignLeafMask(index, null);
            }
        }
        finally { filterClips?.Dispose(); if (!transferred) commands.Dispose(); }

        void AppendBindings(SvgLeafMaskBinding? first)
        {
            for (SvgLeafMaskBinding? binding = first; binding is not null; binding = binding.Inner)
            {
                token.ThrowIfCancellationRequested();
                SvgLeafMaskOperation operation = binding.Operation;
                unresolved.Clear();
                for (SvgLeafMaskOperation? current = operation; current is not null && !current.Resolved; current = current.Source)
                {
                    token.ThrowIfCancellationRequested();
                    unresolved.Add(new(current));
                }
                for (int pending = unresolved.Count - 1; pending >= 0; pending--)
                {
                    token.ThrowIfCancellationRequested();
                    SvgLeafMaskOperation mapped = unresolved[pending].Operation;
                    SvgLeafMaskOperation source = mapped.Source ?? throw new InvalidOperationException("Leaf mask was not resolved.");
                    mapped.Value = source.Value;
                    mapped.PreservesIsolation = source.PreservesIsolation;
                    mapped.SourceOpacity = source.SourceOpacity;
                    mapped.RegionKind = source.RegionKind;
                    if (source.Filter is {} filter)
                        mapped.Filter = new(filter.Binding with { Transform = mapped.Map.ComposeChecked(filter.Binding.Transform) });
                    if (source.NativeClip is {} native)
                        mapped.NativeClip = native with { Map = mapped.Map.ComposeChecked(native.Map), CanvasMap = mapped.Map.ComposeChecked(native.CanvasMap) };
                    if (source.NativeMask is {} mask)
                        mapped.NativeMask = mask with { Map = mapped.Map.ComposeChecked(mask.Map) };
                    if (source.AffineViewport is {} affine)
                        mapped.AffineViewport = affine with
                        {
                            Map = mapped.Map.ComposeChecked(affine.Map),
                            DrawMap = affine.DrawMap is {} drawMap
                                ? new(mapped.Map.Compose(drawMap.Parent), drawMap.Boundary, mapped.Map.ComposeChecked(drawMap.Source)) : null
                        };
                    else if (source.RegionKind is (SvgLayerRegionKind.Viewport or SvgLayerRegionKind.FilterOutputClip) && !mapped.Map.IsAxisAligned && source.Region is SvgClipBox viewport)
                    {
                        mapped.AffineViewport = new(viewport, mapped.Map);
                        mapped.RegionKind = SvgLayerRegionKind.AffineViewport;
                    }
                    else mapped.Region = source.Region is SvgClipBox region ? mapped.Map.Apply(region) : null;
                    mapped.Resolved = true;
                }
                if (operation.AffineViewport is {} clip && operation.ContentIndex < 0)
                {
                    (operation.Region, operation.ContentIndex) = clip.Register(state, scene, token);
                    if (clip.IsExclusion) operation.Region = null;
                }
                if (operation.NativeClip is {} nativeClip && operation.ContentIndex < 0)
                {
                    if (!state.ClipPolygons.TryGetValue(nativeClip.Id, out SvgClipPolygon content))
                        throw new InvalidOperationException("Retained native clip content is missing.");
                    if (operation.RegionKind == SvgLayerRegionKind.Mask && content.NestedClipId is not null)
                    {
                        (_, operation.ContentIndex) = SvgClipPathSyntax.RegisterNestedResourceMask(nativeClip.Id, content, nativeClip.Map, scene, state, token);
                        operation.Region = null;
                    }
                    else content.Register(nativeClip.Map, nativeClip.CanvasMap, content.UsesNativeReplay, state, scene, token).Bind(operation, state);
                }
                if (operation.NativeMask is {} nativeMask && operation.ContentIndex < 0)
                {
                    if (nativeMask.ConstantAlpha is byte constant && nativeMask.Map.IsAxisAligned)
                    {
                        SvgClipBox localBox = nativeMask.ObjectBounds;
                        SvgRectangle local = new(localBox.X, localBox.Y, localBox.Width, localBox.Height, default, false, null);
                        SvgMaskTarget scalarTarget = SvgMaskTarget.Create(nativeMask.Id, nativeMask.Map, local, state);
                        SvgClipBox region = operation.Region ?? scalarTarget.SceneBounds(token);
                        operation.Region = region;
                        operation.Value = constant;
                        if (SvgMaskTarget.SourcePaintRegion(nativeMask.Id, nativeMask.Map, nativeMask.Map.Apply(local), nativeMask.LocalVisualBounds.Map(nativeMask.Map, token), region, state, token) is {} paintRegion)
                            operation.ContentIndex = scene.BindMaskPaintRegion(-1, paintRegion);
                    }
                    else
                    {
                        operation.Value = 255;
                        if (!state.MaskContents.TryGetValue(nativeMask.Id, out SvgMaskContent content))
                            throw new InvalidOperationException("Retained mask content is missing.");
                        SvgClipBox box = nativeMask.ObjectBounds;
                        SvgRectangle bounds = new(box.X, box.Y, box.Width, box.Height, default, false, null);
                        SvgMaskTarget target = SvgMaskTarget.Create(nativeMask.Id, nativeMask.Map, bounds, state);
                        SvgSceneBuilder? previous = state.DefinitionReplayScene;
                        state.DefinitionReplayScene = scene;
                        try
                        {
                            SvgMaskContent placed = content.UniformSource is not null
                                ? SvgMaskContentReplay.ResolveUniformSource(nativeMask.Id, nativeMask.Map, state, token)
                                : SvgMaskContentReplay.PlaceForTarget(nativeMask.Id, content, nativeMask.Map, bounds, target, state, token);
                            operation.ContentIndex = scene.AddMaskContent(placed);
                            operation.Region = target.SceneBounds(token);
                        }
                        finally { state.DefinitionReplayScene = previous; }
                    }
                }
                if (operation.EnclosingClip is {} enclosing) desired.Add(new(enclosing));
                if (operation.PreservesIsolation || operation.Value != 255 || operation.Region is not null || operation.ContentIndex >= 0 || operation.SourceOpacity.Value != 1) desired.Add(new(operation));
            }
        }
    }
}
