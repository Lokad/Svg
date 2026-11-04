using System.Xml;

using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one direct leaf geometry range whose clip-path binding resolves at document end.</summary>
/// <remarks>The conversion owns Scene through binding and emission; this range borrows its builder.</remarks>
internal readonly record struct PendingLeafClip(int FirstGeometryIndex, int GeometryCount, string ClipId, SvgViewportFrame? Parent, SvgAffineTransform Transform, SvgSceneBuilder Scene, bool HasPaintableTarget);

/// <summary>Retains a group command range and its referencing transform for clip resolution after use expansion.</summary>
/// <remarks>The conversion owns Scene through binding; source-local suffixes resolve before parent transfer.</remarks>
internal readonly record struct PendingGroupClip(string ClipId, SvgViewportFrame? Parent, SvgAffineTransform Transform, int FirstCommandIndex, int EndCommandIndex, int Depth, SvgSceneBuilder Scene)
{
    /// <summary>Preserves glyph source rendering instead of lowering a clip into non-glyph geometry.</summary>
    internal bool UsesNativeClip { get; init; }
    /// <summary>Identifies the consumer's own opacity scope independently of isolated descendants.</summary>
    internal SvgRecordingScope? OwnOpacityScope { get; init; }
}

/// <summary>Parses the bounded clip-path reference syntax shared by clipped containers and leaves.</summary>
internal static class SvgClipPathSyntax
{
    private readonly record struct CommandScope(int First, int End, SvgDrawCommand Begin, int Next);
    private readonly record struct SceneScopeHead(SvgSceneBuilder Scene, int First);

    internal static bool IsDisabled(string id, SvgDocumentState state)
        => state.ClipContents.TryGetValue(id, out SvgClipContent clip) && clip.IsDisabled;

    internal static string ParseClipId(string? value)
    {
        if (value is null) throw new SvgException(SvgErrorCode.MalformedInput, "A clip-path reference is required.");
        if (!SvgLocalUrl.TryParseId(value, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local clip-path references are implemented.");
        return id;
    }

    /// <summary>Creates a rectangular clip boundary without introducing an opacity or mask owner.</summary>
    /// <remarks>The conversion-thread caller borrows box/map and parser state. Native content transfers
    /// to scene; an analytic region owns no content. Every mapping/construction step consumes caller
    /// work and cancellation. Failed construction leaves adopted content with scene teardown.</remarks>
    internal static SvgDrawCommand CreateRectangleScope(SvgClipBox box, SvgAffineTransform map, SvgDocumentState state,
        SvgSceneBuilder scene, CancellationToken cancellationToken)
    {
        if (map.IsAxisAligned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgClipBox mapped = map.Apply(box);
            if (RequiresAnalyticClip(mapped))
                return new(SvgDrawCommandKind.BeginGroup, -1, 255, mapped, -1, 255) { RegionKind = SvgLayerRegionKind.Viewport };
        }
        (SvgClipBox bounds, int contentIndex) = new SvgAffineViewportClip(box, map).Register(state, scene, cancellationToken);
        return new(SvgDrawCommandKind.BeginGroup, -1, 255, bounds, contentIndex, 255) { RegionKind = SvgLayerRegionKind.AffineViewport };

        // Ordinary scopes retain native coverage and bitmap hints. Large finite
        // axis regions can clip a small output without overflowing native edges.
        bool RequiresAnalyticClip(SvgClipBox bounds)
        {
            if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom)) return false;
            if (Math.Abs(bounds.X) > int.MaxValue || Math.Abs(bounds.Y) > int.MaxValue || Math.Abs(bounds.Right) > int.MaxValue || Math.Abs(bounds.Bottom) > int.MaxValue) return true;
            if (!state.OutputMap.IsAxisAligned) return false;
            SvgClipBox device = state.OutputMap.Apply(bounds);
            const double maximum = SvgBinaryViewportCoverage.MaximumCoordinate;
            return double.IsFinite(device.X) && double.IsFinite(device.Y) && double.IsFinite(device.Right) && double.IsFinite(device.Bottom)
                && (Math.Abs(device.X) > maximum || Math.Abs(device.Y) > maximum || Math.Abs(device.Right) > maximum || Math.Abs(device.Bottom) > maximum);
        }
    }

    /// <summary>Resolves an explicit inherited declaration against the immediate container's computed clip.</summary>
    /// <remarks>Clip paths are otherwise non-inherited. The returned id borrows the group owner or parsed attribute;
    /// callers retain it only when needed. Root inheritance has the initial empty reference.</remarks>
    internal static string? ResolveClipId(XmlReader reader, SvgDocumentState state)
    {
        if (state.DeferringFilterClip) return null;
        string? reference = SvgLocalUrl.ReadReference(reader, "clip-path");
        if (reference is null) return null;
        string? id = SvgCssWhitespace.Trim(reference).Equals("inherit", StringComparison.Ordinal)
            ? state.Groups.Count == 0 ? null : state.Groups[state.Groups.Count - 1].ClipPathId
            : ParseClipId(reference);
        return id is not null && IsDisabled(id, state) ? null : id;
    }

    /// <summary>Binds a retained resource clip to source geometry through existing native clip operations.</summary>
    /// <remarks>Commands and mask scenes transfer to the parent scene owner. The binding borrows parser-owned operations;
    /// source arrays remain borrowed. Nested chains use bounded iterative scratch, reject cycles and preserve
    /// source map/order. All construction observes cancellation.</remarks>
    internal static SvgLeafMaskBinding? BindResourceClip(string id, SvgAffineTransform map, SvgLeafMaskBinding? inner,
        SvgDocumentState state, CancellationToken token)
    {
        if (IsDisabled(id, state)) return inner;
        if (!state.ClipContents.TryGetValue(id, out SvgClipContent first) || !first.HasIntersections && !first.UsesMaskFallback)
            return BindSingle(id, map, inner, state, token);
        using BudgetedParserList<SvgClipDependency> dependencies = OrderDependencies(id, state, token);
        using BudgetedParserList<SvgNativeClipReference> chain = new(state.Budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserList<SvgNativeClipReference> pending = new(state.Budget, SvgMemoryCategory.Parser, token);
        pending.Add(new(id, map, map));
        while (pending.Count != 0)
        {
            token.ThrowIfCancellationRequested();
            SvgNativeClipReference current = pending[^1];
            pending.RemoveLast();
            if (IsDisabled(current.Id, state)) continue;
            chain.Add(current);
            if (!state.ClipContents.TryGetValue(current.Id, out SvgClipContent content)) continue;
            if (content.UsesMaskFallback) continue; // Its parent clip participates while painting each child.
            if (content.ResourceClipId is {} resourceId) pending.Add(new(resourceId, current.Map, current.Map));
            if (content.ChildClip is {} child)
            {
                SvgAffineTransform childMap = current.Map.ComposeChecked(child.Map);
                pending.Add(new(child.Id, childMap, childMap));
            }
        }
        for (int index = chain.Count - 1; index >= 0; index--)
        {
            SvgNativeClipReference clip = chain[index];
            inner = BindSingle(clip.Id, clip.Map, inner, state, token);
        }
        return inner;

        static SvgLeafMaskBinding? BindSingle(string id, SvgAffineTransform map, SvgLeafMaskBinding? inner,
            SvgDocumentState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsDisabled(id, state)) return inner;
            SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
            operation.Resolved = true;
            if (state.Clips.TryGetValue(id, out SvgClipBox box))
            {
                if (box.Width <= 0 || box.Height <= 0) operation.Value = 0;
                else
                {
                    operation.AffineViewport = new(box, map);
                    operation.Value = 255;
                    operation.RegionKind = SvgLayerRegionKind.AffineViewport;
                }
            }
            else if (state.ClipContents.TryGetValue(id, out SvgClipContent content))
            {
                if (content.IsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box clips on retained resource geometry are not implemented.");
                if (content.IsEmpty) operation.Value = 0;
                else
                {
                    operation.NativeClip = new(id, map, map);
                    operation.Value = 255;
                    operation.RegionKind = SvgLayerRegionKind.AffineViewport;
                }
            }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clips on retained resource geometry are implemented.");
            return SvgLeafMaskComposition.NewBinding(state, operation, inner);
        }
    }

    /// <summary>Returns an owned dependency order, rejecting back edges while allowing shared dependencies.</summary>
    /// <remarks>Ids and child ranges remain document-owned. Caller disposes the result; all scratch reserves
    /// before growth and releases on failure. Visits observe cancellation and use conversion-local state.</remarks>
    internal static BudgetedParserList<SvgClipDependency> OrderDependencies(string id, SvgDocumentState state, CancellationToken token)
    {
        using BudgetedParserList<(string Id, bool Complete)> visits = new(state.Budget, SvgMemoryCategory.Parser, token);
        using BudgetedParserDictionary<string, byte> colors = new(state.Budget, SvgMemoryCategory.Parser, 48);
        BudgetedParserList<SvgClipDependency> order = new(state.Budget, SvgMemoryCategory.Parser, token);
        try
        {
            visits.Add((id, false));
            while (visits.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                (string current, bool complete) = visits[^1];
                visits.RemoveLast();
                if (complete) { colors.Replace(current, 2); order.Add(new(current)); continue; }
                if (IsDisabled(current, state)) continue;
                if (colors.TryGetValue(current, out byte color))
                {
                    if (color == 1) throw new SvgException(SvgErrorCode.MalformedInput, "Cyclic clip-path references are not valid.");
                    continue;
                }
                colors.TryAdd(current, 1);
                visits.Add((current, true));
                if (!state.ClipContents.TryGetValue(current, out SvgClipContent polygon)) continue;
                if (polygon.ResourceClipId is {} resourceId) visits.Add((resourceId, false));
                if (polygon.PictureSources is {} picture && state.ClipSources is {} sources)
                    for (int sourceIndex = picture.First; sourceIndex < picture.First + picture.Count; sourceIndex++)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgClipDefinitionSource source = sources[sourceIndex];
                        if (source.Kind == SvgClipDefinitionSourceKind.Text && source.Text.Contributes && source.Text.ClipId is {} textClip)
                            visits.Add((textClip, false));
                        else if (source.Kind == SvgClipDefinitionSourceKind.Use)
                        {
                            if (source.Use.ClipId is {} useClip) visits.Add((useClip, false));
                            if (SvgReferenceResolver.TryGetDefinition(source.Use.Reference, state.Definitions, out SvgDefinition target)
                                && target.TextSource is {} text)
                            {
                                using SvgRetainedTextSource.Reader reader = text.OpenReader(0, token); reader.MoveToContent();
                                if (SvgLocalUrl.ReadReference(reader, "clip-path") is {} value && SvgLocalUrl.TryParseId(value, out string sourceClipId))
                                    visits.Add((sourceClipId, false));
                            }
                        }
                        else if (source.Kind == SvgClipDefinitionSourceKind.Geometry)
                        {
                            SvgClipContent geometry = source.Geometry;
                            if (geometry.ChildClip is {} clip) visits.Add((clip.Id, false));
                            if (geometry.Children is {} childRange && state.ClipChildren is {} clipChildren)
                                for (int childIndex = childRange.First; childIndex < childRange.First + childRange.Count; childIndex++)
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (clipChildren[childIndex].Clip is {} nested) visits.Add((nested.Id, false));
                                }
                        }
                    }
                if (polygon.ChildClip is {} child) visits.Add((child.Id, false));
                if (polygon.UsesMaskFallback && polygon.Children is {} range && state.ClipChildren is {} children)
                    for (int index = range.First; index < range.First + range.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (children[index].Clip is {} clip) visits.Add((clip.Id, false));
                    }
            }
            return order;
        }
        catch { order.Dispose(); throw; }
    }

    /// <summary>Binds one retained leaf or container clip after collecting its complete pre-clip target.</summary>
    /// <remarks>Geometry remains caller-owned. Clip content and binding metadata belong to the active
    /// scene/document owner. Traversal observes cancellation token; source geometry
    /// is not trimmed, so nested clips cannot change an enclosing object's bounds.</remarks>
    internal static void ApplyResourceClip(Span<SvgGeometry> geometries, string id, SvgAffineTransform map,
        SvgDocumentState state, CancellationToken token)
    {
        if (IsDisabled(id, state)) return;
        bool rectangle = state.ObjectClips.TryGetValue(id, out SvgClipBox fractions);
        bool hasPolygon = state.ClipContents.TryGetValue(id, out SvgClipContent polygon);
        if (!rectangle && !hasPolygon && !state.Clips.TryGetValue(id, out _))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clips on retained resource geometry are implemented.");
        SvgLeafMaskOperation? resourceClip = null;
        if (rectangle || polygon.IsObjectBoundingBox)
        {
            SvgObjectBoundingClip target = new(map, null);
            foreach (SvgGeometry geometry in geometries) target.Add(geometry, state, token);
            resourceClip = rectangle ? target.Register(fractions, state, token) : target.Register(polygon, id, state, token);
        }
        else if (polygon.HasIntersections)
            resourceClip = DeferNestedResourceMask(id, map, state);
        SvgLeafMaskBinding? lastInner = null, binding = null;
        foreach (ref SvgGeometry geometry in geometries)
        {
            token.ThrowIfCancellationRequested();
            if (binding is null || !ReferenceEquals(lastInner, geometry.LeafMask))
            {
                lastInner = geometry.LeafMask;
                binding = resourceClip is null ? BindResourceClip(id, map, lastInner, state, token)
                    : SvgLeafMaskComposition.NewBinding(state, resourceClip, lastInner);
            }
            geometry = geometry with { LeafMask = binding };
        }
    }

    /// <summary>Defers nested stencil registration until retained geometry reaches its final scene owner.</summary>
    /// <remarks>The document owns the id and operation; instance mapping preserves the native resource map.
    /// Emission observes cancellation through the ordinary clip replay.</remarks>
    internal static SvgLeafMaskOperation DeferNestedResourceMask(string id, SvgAffineTransform map, SvgDocumentState state)
    {
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255;
        operation.NativeClip = new(id, map, map); operation.RegionKind = SvgLayerRegionKind.Mask;
        return operation;
    }

    /// <summary>Validates the clip rule shared by attributes and style declarations.</summary>
    internal static void ValidateClipRule(ReadOnlySpan<char> value)
        => _ = ParseClipRule(value);

    /// <summary>Retains the independent nonzero or parity rule for native clip-content replay.</summary>
    internal static bool ParseClipRule(ReadOnlySpan<char> value)
    {
        if (value.Trim(SvgCssWhitespace.Characters).Equals("nonzero".AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Trim(SvgCssWhitespace.Characters).Equals("evenodd".AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only nonzero and evenodd clip rules are implemented.");
    }

    /// <summary>Parses the clip-path coordinate system, defaulting to userSpaceOnUse.</summary>
    internal static bool ParseClipUnits(string? value)
    {
        if (value is null || value.Equals("userSpaceOnUse", StringComparison.Ordinal)) return false;
        if (value.Equals("objectBoundingBox", StringComparison.Ordinal)) return true;
        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only userSpaceOnUse and objectBoundingBox clip-path units are implemented.");
    }


    /// <summary>Resolves the effective clip for one rectangle from the viewport frame with objectBoundingBox fractions scaled by its placement.</summary>
    internal static SvgViewportFrame? ResolveLeafClipOrPlacement(XmlReader reader, SvgDocumentState state, SvgViewportFrame? frame, SvgRectangle placement)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? id = ResolveClipId(reader, state);
        if (id is null) return frame;
        if (state.DeferringLeafClip) return frame;
        if (state.ObjectClips.TryGetValue(id, out SvgClipBox fractions))
        {
            SvgClipBox resolved = new(placement.X + fractions.X * placement.Width, placement.Y + fractions.Y * placement.Height, fractions.Width * placement.Width, fractions.Height * placement.Height);
            SvgClipBox clip = PlaceViewportClip(resolved, SvgAffineTransform.Identity, frame);
            double offsetX = frame?.OffsetX ?? 0;
            double offsetY = frame?.OffsetY ?? 0;
            double scaleX = frame?.ScaleX ?? 1;
            double scaleY = frame?.ScaleY ?? 1;
            return new SvgViewportFrame(offsetX, offsetY, scaleX, scaleY, frame?.ViewportWidth, frame?.ViewportHeight, clip, reader.Depth);
        }
        return ResolveLeafClip(reader, state);
    }
    /// <summary>Retains unresolved or transformed leaf clips; known clips on identity targets keep the direct route.</summary>
    internal static string? PendingClipId(XmlReader reader, SvgDocumentState state, SvgPaintableElementContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? id = ResolveClipId(reader, state);
        if (id is null) return null;
        if (state.Clips.TryGetValue(id, out _) && context.ResolveTransform(reader) == SvgAffineTransform.Identity) return null;
        return state.Strings.Copy(id);
    }

    /// <summary>Applies deferred userSpace clip paths to direct leaf geometry ranges at document end.</summary>
    internal static void ResolvePendingLeafClips(SvgDocumentState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (int pendingIndex = 0; pendingIndex < state.PendingLeafClips.Count; pendingIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingLeafClip pending = state.PendingLeafClips[pendingIndex];
            if (IsDisabled(pending.ClipId, state)) continue;
            if (!pending.HasPaintableTarget && (state.Clips.TryGetValue(pending.ClipId, out _)
                || state.ObjectClips.TryGetValue(pending.ClipId, out _) || state.ClipContents.TryGetValue(pending.ClipId, out _))) continue;
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = pending.Scene;
            try
            {
                SvgViewportFrame? parent = pending.Parent;
                SvgClipBox clip;
                if (state.Clips.TryGetValue(pending.ClipId, out SvgClipBox box))
                {
                    if (!pending.Transform.IsAxisAligned)
                    {
                        SvgAffineTransform map = parent is {} frame
                            ? SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY).ComposeChecked(pending.Transform)
                            : pending.Transform;
                        SvgLeafMaskOperation? viewport = ParentClipWindow(parent, state);
                        SvgLeafMaskBinding? binding = null;
                        for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SvgLeafMaskBinding? inner = state.Scene.GeometryAt(index).LeafMask;
                            binding = binding is null
                                ? BindResourceClip(pending.ClipId, map, inner, state, cancellationToken)
                                : SvgLeafMaskComposition.NewBinding(state, binding.Operation, inner);
                            state.Scene.AssignLeafMask(index, viewport is null ? binding : SvgLeafMaskComposition.NewBinding(state, viewport, binding));
                        }
                        continue;
                    }
                    clip = MapGroupClip(box, pending.Transform, parent, cancellationToken);
                }
                else if (state.ObjectClips.TryGetValue(pending.ClipId, out SvgClipBox fractions))
                {
                    SvgObjectBoundingClip target = new(pending.Transform, parent);
                    for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                        target.Add(state.Scene.GeometryAt(index), state, cancellationToken);
                    SvgLeafMaskOperation operation = target.Register(fractions, state, cancellationToken);
                    for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        state.Scene.AssignLeafMask(index, SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(index).LeafMask));
                    }
                    continue;
                }
                else if (state.ClipContents.TryGetValue(pending.ClipId, out SvgClipContent clipPolygon))
                {
                    if (clipPolygon.IsObjectBoundingBox)
                    {
                        SvgObjectBoundingClip bounds = new(pending.Transform, parent);
                        for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                            bounds.Add(state.Scene.GeometryAt(index), state, cancellationToken);
                        SvgLeafMaskOperation operation = bounds.Register(clipPolygon, state, cancellationToken);
                        for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            state.Scene.AssignLeafMask(index, SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(index).LeafMask));
                        }
                        continue;
                    }
                    if (pending.GeometryCount == 0) continue;
                    {
                        // Native clip coverage multiplies each paint operation independently.
                        // Keep its source contours and shader coordinates intact instead of
                        // replacing them with a geometric intersection of flattened vertices.
                        SvgAffineTransform map = pending.Transform;
                        if (parent is {} viewport) map = SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(map);
                        SvgClipRegistration registration;
                        if (clipPolygon.HasIntersections)
                        {
                            registration = RegisterNestedResourceMask(pending.ClipId, clipPolygon, map, state.Scene, state, cancellationToken);
                        }
                        else registration = clipPolygon.Register(map, map, clipPolygon.UsesNativeReplay, state, state.Scene, cancellationToken);
                        SvgClipBox bounds = registration.Bounds;
                        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                        operation.Resolved = true;
                        operation.Value = 255;
                        registration.Bind(operation, state);
                        if (clipPolygon.HasIntersections && registration.EnclosureIndex < 0) { operation.RegionKind = SvgLayerRegionKind.Mask; operation.Region = null; }
                        SvgLeafMaskOperation? contentBounds = null;
                        if (clipPolygon.HasIntersections && registration.EnclosureIndex < 0)
                        {
                            contentBounds = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                            contentBounds.Resolved = true; contentBounds.Value = 255;
                            contentBounds.RegionKind = SvgLayerRegionKind.AffineViewport;
                            contentBounds.AffineViewport = new(bounds, SvgAffineTransform.Identity);
                        }
                        for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SvgLeafMaskBinding? inner = state.Scene.GeometryAt(geometryIndex).LeafMask;
                            if (contentBounds is not null) inner = SvgLeafMaskComposition.NewBinding(state, contentBounds, inner);
                            SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, operation, inner);
                            state.Scene.AssignLeafMask(geometryIndex, binding);
                        }
                    }
                    continue;
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
                for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    state.Scene.ReplaceGeometry(geometryIndex, ApplyLeafClip(state.Scene.GeometryAt(geometryIndex), clip));
                }
            }
            finally { state.DefinitionReplayScene = previous; }
        }

    }
    // Build a bounded white stencil from borrowed native paths. The containing
    // scene adopts its immutable replay owner. Native clips construct inside
    // the mask; target alpha-mask rounding remains separate. Existing owners
    // enforce cancellation, material reservations and cleanup.
    internal static SvgClipRegistration RegisterNestedResourceMask(string id, SvgClipContent polygon,
        SvgAffineTransform map, SvgSceneBuilder owner, SvgDocumentState state, CancellationToken token)
    {
        if (polygon.UsesMaskFallback) return polygon.Register(map, map, true, state, owner, token);
        SvgRectangle bounds = polygon.SourcePath is {} source ? SvgPathBounds.Compute(source.Commands, map.ComposeChecked(source.SceneMap), token)
            : polygon.ResourceBounds is {} visual
                ? map.Apply(new SvgRectangle(visual.Left, visual.Top, visual.Right - visual.Left, visual.Bottom - visual.Top, default, true, null))
            : throw new InvalidOperationException("Intersected clip replay requires source bounds.");
        double margin = 2 / state.OutputMagnification;
        using SvgSceneBuilder builder = new(state.Budget, token);
        SvgLeafMaskOperation contour = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        contour.Resolved = true; contour.Value = 255; contour.NativeClip = new(id, map, map);
        contour.RegionKind = SvgLayerRegionKind.AffineViewport;
        SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, contour, null);
        if (polygon.ChildClip is {} child)
            binding = BindResourceClip(child.Id, map.ComposeChecked(child.Map), binding, state, token) ?? binding;
        if (polygon.ResourceClipId is {} resourceId)
            binding = BindResourceClip(resourceId, map, binding, state, token) ?? binding;
        builder.AddGeometry(SvgGeometry.FromRectangle(new(bounds.X - margin, bounds.Y - margin,
            bounds.Width + margin * 2, bounds.Height + margin * 2, new(255, 255, 255, 255), true, null)) with { LeafMask = binding });
        SvgLeafMaskComposition.Emit(state, builder, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { owner.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        int index = owner.AddMaskContent(new([], 255, true) { VectorContent = content });
        return new(new(bounds.X, bounds.Y, bounds.Width, bounds.Height), index, -1);
    }

    /// <summary>Applies deferred userSpace clip paths to direct group command ranges at document end, after use expansion.</summary>
    internal static void ResolvePendingGroupClips(SvgDocumentState state, CancellationToken cancellationToken)
        => ResolvePendingGroupClips(state, 0, cancellationToken);

    /// <summary>Binds an appended source-local suffix before its scene transfers to its parent.</summary>
    internal static void ResolvePendingGroupClips(SvgDocumentState state, int first, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (first == state.PendingGroupClips.Count) return;
        using BudgetedParserList<CommandScope> scopes = new(state.Budget, SvgMemoryCategory.Parser, cancellationToken);
        using BudgetedParserDictionary<SvgSceneBuilder, SceneScopeHead> heads = new(state.Budget, SvgMemoryCategory.Parser, 48);
        // References were recorded at group entry. Prepend inner scopes first
        // so the emitted native clip stack follows outer-to-inner document order.
        for (int pendingIndex = state.PendingGroupClips.Count - 1; pendingIndex >= first; pendingIndex--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingGroupClip pending = state.PendingGroupClips[pendingIndex];
            if (IsDisabled(pending.ClipId, state)) continue;
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = pending.Scene;
            try
            {
                if ((state.Clips.TryGetValue(pending.ClipId, out _) || state.ObjectClips.TryGetValue(pending.ClipId, out _)
                    || state.ClipContents.TryGetValue(pending.ClipId, out _))
                    && !SvgPaintRecording.HasPaintableSource(state, pending.FirstCommandIndex, pending.EndCommandIndex, cancellationToken)) continue;
                SvgViewportFrame? parent = pending.Parent;
                SvgClipBox clip;
                if (state.Clips.TryGetValue(pending.ClipId, out SvgClipBox box))
                {
                    if (ContainsIsolatedLayer(pending))
                    {
                        SvgAffineTransform map = parent is {} viewport
                            ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(pending.Transform)
                            : pending.Transform;
                        SvgDrawCommand begin = CreateRectangleScope(box, map, state, state.Scene, cancellationToken);
                        QueueScope(pending, begin);
                        continue;
                    }
                    if (!pending.Transform.IsAxisAligned || pending.UsesNativeClip || ContainsGlyph(pending))
                    {
                        SvgAffineTransform map = parent is {} frame
                            ? SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY).ComposeChecked(pending.Transform)
                            : pending.Transform;
                        SvgLeafMaskOperation? viewport = ParentClipWindow(parent, state);
                        SvgLeafMaskBinding? binding = null;
                        for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SvgDrawCommand command = state.Scene.CommandAt(index);
                            if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                            SvgLeafMaskBinding? inner = state.Scene.GeometryAt(command.GeometryIndex).LeafMask;
                            binding = binding is null
                                ? BindResourceClip(pending.ClipId, map, inner, state, cancellationToken)
                                : SvgLeafMaskComposition.NewBinding(state, binding.Operation, inner);
                            state.Scene.AssignLeafMask(command.GeometryIndex, viewport is null ? binding : SvgLeafMaskComposition.NewBinding(state, viewport, binding));
                        }
                        continue;
                    }
                    clip = MapGroupClip(box, pending.Transform, parent, cancellationToken);
                }
                else if (state.ObjectClips.TryGetValue(pending.ClipId, out SvgClipBox fractions))
                {
                    SvgObjectBoundingClip target = new(pending.Transform, parent);
                    for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SvgDrawCommand command = state.Scene.CommandAt(index);
                        if (command.Kind == SvgDrawCommandKind.Geometry) target.Add(state.Scene.GeometryAt(command.GeometryIndex), state, cancellationToken);
                    }
                    SvgLeafMaskOperation operation = target.Register(fractions, state, cancellationToken);
                    if (ContainsIsolatedLayer(pending))
                    {
                        WrapOperation(pending, operation);
                        continue;
                    }
                    for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SvgDrawCommand command = state.Scene.CommandAt(index);
                        if (command.Kind == SvgDrawCommandKind.Geometry)
                            state.Scene.AssignLeafMask(command.GeometryIndex, SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(command.GeometryIndex).LeafMask));
                    }
                    continue;
                }
                else if (state.ClipContents.TryGetValue(pending.ClipId, out SvgClipContent polygon))
                {
                    if (polygon.IsObjectBoundingBox)
                    {
                        SvgObjectBoundingClip target = new(pending.Transform, parent);
                        for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SvgDrawCommand command = state.Scene.CommandAt(index);
                            if (command.Kind == SvgDrawCommandKind.Geometry) target.Add(state.Scene.GeometryAt(command.GeometryIndex), state, cancellationToken);
                        }
                        SvgLeafMaskOperation boundingOperation = target.Register(polygon, state, cancellationToken);
                        if (ContainsIsolatedLayer(pending))
                        {
                            WrapOperation(pending, boundingOperation);
                            continue;
                        }
                        for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            SvgDrawCommand command = state.Scene.CommandAt(index);
                            if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                            state.Scene.AssignLeafMask(command.GeometryIndex, SvgLeafMaskComposition.NewBinding(state, boundingOperation, state.Scene.GeometryAt(command.GeometryIndex).LeafMask));
                        }
                        continue;
                    }
                    if (polygon.ContourEnds.Length != 1 && !polygon.UsesNativeReplay && !polygon.UsesMaskFallback) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-contour polygon clip-paths on groups are implemented.");
                    SvgAffineTransform map = pending.Transform;
                    if (parent is {} viewport)
                        map = SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(map);
                    SvgClipRegistration registration;
                    if (polygon.HasIntersections)
                    {
                        registration = RegisterNestedResourceMask(pending.ClipId, polygon, map, state.Scene, state, cancellationToken);
                    }
                    else registration = polygon.Register(map, map, polygon.UsesNativeReplay, state, state.Scene, cancellationToken);
                    SvgClipBox bounds = registration.Bounds;
                    SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                    operation.Resolved = true;
                    operation.Value = 255;
                    registration.Bind(operation, state);
                    if (polygon.HasIntersections && registration.EnclosureIndex < 0) { operation.Region = null; operation.RegionKind = SvgLayerRegionKind.Mask; }
                    if (ContainsIsolatedLayer(pending))
                    {
                        WrapOperation(pending, operation);
                        continue;
                    }
                    for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                        if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                        SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(command.GeometryIndex).LeafMask);
                        state.Scene.AssignLeafMask(command.GeometryIndex, binding);
                    }
                    continue;
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
                for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SvgDrawCommand command = state.Scene.CommandAt(commandIndex);
                    if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                    state.Scene.ReplaceGeometry(command.GeometryIndex, ApplyLeafClip(state.Scene.GeometryAt(command.GeometryIndex), clip));
                }
            }
            finally { state.DefinitionReplayScene = previous; }
        }
        foreach (SceneScopeHead head in heads.Values) WrapCommands(head);

        void QueueScope(PendingGroupClip pending, SvgDrawCommand begin)
        {
            bool exists = heads.TryGetValue(pending.Scene, out SceneScopeHead head);
            scopes.Add(new(pending.FirstCommandIndex, pending.EndCommandIndex, begin, head.First));
            SceneScopeHead replacement = new(pending.Scene, scopes.Count);
            if (exists) heads.Replace(pending.Scene, replacement);
            else heads.TryAdd(pending.Scene, replacement);
        }

        void WrapOperation(PendingGroupClip pending, SvgLeafMaskOperation operation)
        {
            // The group's own opacity restores after its clip. Descendant
            // effects stay inside that clip; their isolation is independent.
            if (operation.RegionKind == SvgLayerRegionKind.ClipMask && pending.OwnOpacityScope is {} scope
                && pending.EndCommandIndex > pending.FirstCommandIndex + 1
                && pending.Scene.CommandAt(pending.FirstCommandIndex) is { Kind: SvgDrawCommandKind.BeginGroup,
                    MaskContentIndex: < 0, MaskRegion: null } own
                && own.GeometryIndex == scope.Value && own.SourceOpacity.Value != 1
                && pending.Scene.CommandAt(pending.EndCommandIndex - 1).Kind == SvgDrawCommandKind.EndGroup)
                pending = pending with { FirstCommandIndex = pending.FirstCommandIndex + 1, EndCommandIndex = pending.EndCommandIndex - 1 };
            if (operation.AffineViewport is {} clip)
                (operation.Region, operation.ContentIndex) = clip.Register(state, pending.Scene, cancellationToken);
            if (operation.EnclosingClip is {} enclosing)
                QueueScope(pending, new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, enclosing.RecordingScope.Value,
                    enclosing.SourceOpacity, enclosing.Region, enclosing.ContentIndex, enclosing.Value) { RegionKind = enclosing.RegionKind });
            QueueScope(pending, new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, operation.RecordingScope.Value,
                operation.SourceOpacity, operation.Region, operation.ContentIndex, operation.Value) { RegionKind = operation.RegionKind });
        }

        bool ContainsGlyph(PendingGroupClip pending)
        {
            for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgDrawCommand command = state.Scene.CommandAt(index);
                if (command.Kind == SvgDrawCommandKind.Geometry && state.Scene.GeometryAt(command.GeometryIndex).Kind == SvgGeometryKind.GlyphContours) return true;
            }
            return false;
        }

        bool ContainsIsolatedLayer(PendingGroupClip pending)
        {
            // Masks and filters own isolated sources even at unit opacity. An
            // ancestor clip encloses their results; viewport scopes remain clips.
            for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pending.Scene.CommandAt(index) is { Kind: SvgDrawCommandKind.BeginGroup } command
                    && (command.SourceOpacity.Value != 1 || command.RegionKind is SvgLayerRegionKind.Mask or SvgLayerRegionKind.ClipMask or SvgLayerRegionKind.Filter)) return true;
            }
            return false;
        }

        // Descending source resolution prepends scope links, leaving each scene's
        // chain in document order. Insert all wrappers in one bounded traversal;
        // rebuilding the complete scene for each clip would make siblings quadratic.
        void WrapCommands(SceneScopeHead head)
        {
            SvgSceneBuilder scene = head.Scene;
            int count = scene.CommandCount;
            using RenderBudget.BudgetReservation mapOwner = state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<int>(count + 1), SvgMemoryCategory.Parser);
            int[] positions = new int[count + 1];
            BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, cancellationToken);
            using BudgetedParserList<int> ends = new(state.Budget, SvgMemoryCategory.Parser, cancellationToken);
            bool transferred = false;
            try
            {
                int next = head.First;
                for (int index = 0; index <= count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (ends.Count != 0 && ends[^1] == index)
                    {
                        ends.RemoveLast();
                        commands.Add(new(SvgDrawCommandKind.EndGroup, -1, 255, null, -1, 255));
                    }
                    while (next != 0 && scopes[next - 1].First == index)
                    {
                        CommandScope scope = scopes[next - 1];
                        commands.Add(scope.Begin);
                        ends.Add(scope.End);
                        next = scope.Next;
                    }
                    positions[index] = commands.Count;
                    if (index < count) commands.Add(scene.CommandAt(index));
                }
                if (next != 0 || ends.Count != 0) throw new InvalidOperationException("Forward clip scopes require balanced command ranges.");
                SvgReferenceResolver.RemapPendingCommandRanges(state, scene, positions, new(0, 0, 0, 0), state.PendingCommandEnd, cancellationToken);
                scene.AdoptCommands(commands);
                transferred = true;
            }
            finally { if (!transferred) commands.Dispose(); }
        }

    }

    /// <summary>Keeps the native clip construction window while geometry owns fractional parent coverage.</summary>
    private static SvgLeafMaskOperation? ParentClipWindow(SvgViewportFrame? parent, SvgDocumentState state)
    {
        if (parent is not {} frame || frame.HasAffineClipScope || frame.Clip == SvgClipBox.Unbounded) return null;
        double left = Math.Floor(frame.Clip.X), top = Math.Floor(frame.Clip.Y);
        SvgClipBox window = new(left, top, Math.Ceiling(frame.Clip.Right) - left, Math.Ceiling(frame.Clip.Bottom) - top);
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255;
        operation.RegionKind = SvgLayerRegionKind.AffineViewport;
        operation.AffineViewport = new(window, SvgAffineTransform.Identity) { IsBinary = true };
        return operation;
    }

    /// <summary>Places a user-space group clip beside its transformed children before intersecting the parent clip.</summary>
    /// <remarks>The group supplies its complete inherited axis-aligned map; the parent frame supplies its scalar viewBox and physical clip. Mapping checks cancellation at its four boundaries before arithmetic and preserves AA provenance through reflection. This constant-size operation owns no storage and retains no state.</remarks>
    internal static SvgClipBox MapGroupClip(SvgClipBox box, SvgAffineTransform transform, SvgViewportFrame? parent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PlaceViewportClip(box, transform, parent);
    }

    /// <summary>Places one finite axis clip through bounded value arithmetic; node traversal owns cancellation.</summary>
    private static SvgClipBox PlaceViewportClip(SvgClipBox box, SvgAffineTransform transform, SvgViewportFrame? parent)
    {
        SvgClipBox placed = transform.Apply(box);
        if (parent is not {} frame) return placed;
        placed = SvgAffineTransform.FromScaleOffset(frame.ScaleX, frame.ScaleY, frame.OffsetX, frame.OffsetY).Apply(placed);
        return frame.Clip.Intersect(placed);
    }

    /// <summary>Transfers geometry/path ownership through the existing axis-aligned leaf clip.</summary>
    internal static SvgGeometry ApplyLeafClip(SvgGeometry geometry, SvgClipBox clip)
    {
        if (geometry.Kind == SvgGeometryKind.Rectangle)
        {
            SvgRectangle rectangle = geometry.Rectangle;
            if (rectangle.Stroke is null) return SvgGeometry.FromRectangle(clip.ClipRectangle(rectangle));
            double halfWidth = rectangle.Stroke.Value.Width / 2d;
            if (!clip.Contains(rectangle.X - halfWidth, rectangle.Y - halfWidth, rectangle.X + rectangle.Width + halfWidth, rectangle.Y + rectangle.Height + halfWidth)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Rectangle strokes crossing a clip-path boundary are not implemented.");
            return geometry;
        }
        SvgGeometry result = geometry.Kind switch
        {
            SvgGeometryKind.EmbeddedImage => SvgGeometry.FromImage(geometry.Image.ClipTo(clip)),
            SvgGeometryKind.OrthogonalBand => SvgGeometry.FromOrthogonalBand(clip.ClipRectangle(geometry.Rectangle)),
            SvgGeometryKind.AxisAlignedLine => clip.ClipLine(geometry.AxisAlignedLine),
            SvgGeometryKind.PolygonFill => SvgGeometry.FromPolygonFill(geometry.Polygon.Stamped(255, clip)),
            SvgGeometryKind.GradientPolygon => SvgGeometry.FromGradientPolygon(geometry.GradientPolygon.Stamped(255, clip)),
            SvgGeometryKind.RadialPolygon => SvgGeometry.FromRadialPolygon(geometry.RadialPolygon.Stamped(255, clip)),
            SvgGeometryKind.PatternPolygon => SvgGeometry.FromPatternPolygon(geometry.PatternPolygon.Stamped(255, clip)),
            SvgGeometryKind.GradientFill => SvgGeometry.FromGradientFill(geometry.Gradient.ClipTo(clip)),
            SvgGeometryKind.RadialGradient => SvgGeometry.FromRadialFill(geometry.Radial.ClipTo(clip)),
            SvgGeometryKind.PatternFill => SvgGeometry.FromPatternFill(geometry.Pattern.ClipTo(clip)),
            SvgGeometryKind.GradientStroke => SvgGeometry.FromGradientStroke(geometry.GradientStroke.ClipTo(clip)),
            _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Forward clip-path definitions on this geometry are not implemented."),
        };
        return geometry.TransferAuthoredPathTo(result);
    }

    /// <summary>Resolves the effective clip for one rendered leaf or use instance from the viewport frame and an optional clip-path attribute.</summary>
    internal static SvgViewportFrame? ResolveLeafClip(XmlReader reader, SvgDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(state);
        string? clipId = ResolveClipId(reader, state);
        if (clipId is null) return state.EffectiveFrame;
        if (state.DeferringLeafClip) return state.EffectiveFrame;
        if (state.ObjectClips.TryGetValue(clipId, out _)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths are only implemented on rectangles, bands and straight contours.");
        if (!state.Clips.TryGetValue(clipId, out SvgClipBox box)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
        SvgViewportFrame? parent = state.EffectiveFrame;
        SvgClipBox clip = PlaceViewportClip(box, SvgAffineTransform.Identity, parent);
        double offsetX = parent?.OffsetX ?? 0;
        double offsetY = parent?.OffsetY ?? 0;
        double scaleX = parent?.ScaleX ?? 1;
        double scaleY = parent?.ScaleY ?? 1;
        return new SvgViewportFrame(offsetX, offsetY, scaleX, scaleY, parent?.ViewportWidth, parent?.ViewportHeight, clip, reader.Depth);
    }
}
