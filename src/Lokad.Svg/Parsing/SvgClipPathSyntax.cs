using System.Xml;

using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one direct leaf geometry range whose clip-path binding resolves at document end.</summary>
/// <remarks>The conversion owns Scene through binding and emission; this range borrows its builder.</remarks>
internal readonly record struct PendingLeafClip(int FirstGeometryIndex, int GeometryCount, string ClipId, SvgViewportFrame? Parent, SvgAffineTransform Transform, SvgSceneBuilder Scene);

/// <summary>Retains a group command range and its referencing transform for clip resolution after use expansion.</summary>
/// <remarks>The conversion owns Scene through binding; source-local suffixes resolve before parent transfer.</remarks>
internal readonly record struct PendingGroupClip(string ClipId, SvgViewportFrame? Parent, SvgAffineTransform Transform, int FirstCommandIndex, int EndCommandIndex, int Depth, SvgSceneBuilder Scene)
{
    /// <summary>Preserves glyph source rendering instead of lowering a clip into non-glyph geometry.</summary>
    internal bool UsesNativeClip { get; init; }
}

/// <summary>Parses the bounded clip-path reference syntax shared by clipped containers and leaves.</summary>
internal static class SvgClipPathSyntax
{
    private readonly record struct CommandScope(int First, int End, SvgDrawCommand Begin, int Next);
    private readonly record struct SceneScopeHead(SvgSceneBuilder Scene, int First);

    internal static bool IsDisabled(string id, SvgDocumentState state)
        => state.ClipPolygons.TryGetValue(id, out SvgClipPolygon clip) && clip.IsDisabled;

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
        SvgSceneBuilder scene, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        if (map.IsAxisAligned)
        {
            for (int boundary = 0; boundary < 8; boundary++) workCounter.Consume(cancellationToken);
            SvgClipBox mapped = map.Apply(box);
            if (RequiresAnalyticClip(mapped))
                return new(SvgDrawCommandKind.BeginGroup, -1, 255, mapped, -1, 255) { RegionKind = SvgLayerRegionKind.Viewport };
        }
        (SvgClipBox bounds, int contentIndex) = new SvgAffineViewportClip(box, map).Register(state, scene, workCounter, cancellationToken);
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
    /// source map/order. All construction charges work and observes cancellation.</remarks>
    internal static SvgLeafMaskBinding? BindResourceClip(string id, SvgAffineTransform map, SvgLeafMaskBinding? inner,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (IsDisabled(id, state)) return inner;
        if (!state.ClipPolygons.TryGetValue(id, out SvgClipPolygon first) || first.NestedClipId is null)
            return BindSingle(id, map, inner, state, work, token);
        using BudgetedParserList<SvgNativeClipReference> chain = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserDictionary<string, byte> seen = new(state.Budget, SvgMemoryCategory.Parser, 48);
        string currentId = id;
        SvgAffineTransform currentMap = map;
        while (true)
        {
            work.Consume(token);
            if (IsDisabled(currentId, state)) break;
            if (!seen.TryAdd(currentId, 0)) throw new SvgException(SvgErrorCode.MalformedInput, "Cyclic clip-path references are not valid.");
            chain.Add(new(currentId, currentMap));
            if (!state.ClipPolygons.TryGetValue(currentId, out SvgClipPolygon content) || content.NestedClipId is not {} nestedId) break;
            if (content.IsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box clips on retained resource geometry are not implemented.");
            currentMap = currentMap.ComposeChecked(content.SourcePath?.SceneMap ?? SvgAffineTransform.Identity);
            currentId = nestedId;
        }
        for (int index = chain.Count - 1; index >= 0; index--)
        {
            SvgNativeClipReference clip = chain[index];
            inner = BindSingle(clip.Id, clip.Map, inner, state, work, token);
        }
        return inner;

        static SvgLeafMaskBinding? BindSingle(string id, SvgAffineTransform map, SvgLeafMaskBinding? inner,
            SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        {
            work.Consume(token);
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
            else if (state.ClipPolygons.TryGetValue(id, out SvgClipPolygon content))
            {
                if (content.IsObjectBoundingBox) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box clips on retained resource geometry are not implemented.");
                if (content.Points.Length == 0) operation.Value = 0;
                else
                {
                    operation.NativeClip = new(id, map);
                    operation.Value = 255;
                    operation.RegionKind = SvgLayerRegionKind.AffineViewport;
                }
            }
            else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clips on retained resource geometry are implemented.");
            return SvgLeafMaskComposition.NewBinding(state, operation, inner);
        }
    }

    /// <summary>Binds one retained leaf or container clip after collecting its complete pre-clip target.</summary>
    /// <remarks>Geometry remains caller-owned. Clip content and binding metadata belong to the active
    /// scene/document owner. Traversal charges shared work and observes cancellation; source geometry
    /// is not trimmed, so nested clips cannot change an enclosing object's bounds.</remarks>
    internal static void ApplyResourceClip(Span<SvgGeometry> geometries, string id, SvgAffineTransform map,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        if (IsDisabled(id, state)) return;
        bool rectangle = state.ObjectClips.TryGetValue(id, out SvgClipBox fractions);
        bool hasPolygon = state.ClipPolygons.TryGetValue(id, out SvgClipPolygon polygon);
        if (!rectangle && !hasPolygon && !state.Clips.TryGetValue(id, out _))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clips on retained resource geometry are implemented.");
        SvgLeafMaskOperation? resourceClip = null;
        if (rectangle || polygon.IsObjectBoundingBox)
        {
            SvgObjectBoundingClip target = new(map, null);
            foreach (SvgGeometry geometry in geometries) target.Add(geometry, state, work, token);
            resourceClip = rectangle ? target.Register(fractions, state, work, token) : target.Register(polygon, id, state, work, token);
        }
        else if (polygon.NestedClipId is not null)
            resourceClip = DeferNestedResourceMask(id, map, state);
        SvgLeafMaskBinding? lastInner = null, binding = null;
        foreach (ref SvgGeometry geometry in geometries)
        {
            work.Consume(token);
            if (binding is null || !ReferenceEquals(lastInner, geometry.LeafMask))
            {
                lastInner = geometry.LeafMask;
                binding = resourceClip is null ? BindResourceClip(id, map, lastInner, state, work, token)
                    : SvgLeafMaskComposition.NewBinding(state, resourceClip, lastInner);
            }
            geometry = geometry with { LeafMask = binding };
        }
    }

    /// <summary>Defers nested stencil registration until retained geometry reaches its final scene owner.</summary>
    /// <remarks>The document owns the id and operation; instance mapping preserves the native resource map.
    /// Emission charges construction work and observes cancellation through the ordinary clip replay.</remarks>
    internal static SvgLeafMaskOperation DeferNestedResourceMask(string id, SvgAffineTransform map, SvgDocumentState state)
    {
        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        operation.Resolved = true; operation.Value = 255;
        operation.NativeClip = new(id, map); operation.RegionKind = SvgLayerRegionKind.Mask;
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
    internal static void ResolvePendingLeafClips(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        for (int pendingIndex = 0; pendingIndex < state.PendingLeafClips.Count; pendingIndex++)
        {
            workCounter.Consume(cancellationToken);
            PendingLeafClip pending = state.PendingLeafClips[pendingIndex];
            if (IsDisabled(pending.ClipId, state)) continue;
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
                            workCounter.Consume(cancellationToken);
                            SvgLeafMaskBinding? inner = state.Scene.GeometryAt(index).LeafMask;
                            binding = binding is null
                                ? BindResourceClip(pending.ClipId, map, inner, state, workCounter, cancellationToken)
                                : SvgLeafMaskComposition.NewBinding(state, binding.Operation, inner);
                            state.Scene.AssignLeafMask(index, viewport is null ? binding : SvgLeafMaskComposition.NewBinding(state, viewport, binding));
                        }
                        continue;
                    }
                    clip = MapGroupClip(box, pending.Transform, parent, workCounter, cancellationToken);
                }
                else if (state.ObjectClips.TryGetValue(pending.ClipId, out SvgClipBox fractions))
                {
                    SvgObjectBoundingClip target = new(pending.Transform, parent);
                    for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                        target.Add(state.Scene.GeometryAt(index), state, workCounter, cancellationToken);
                    SvgLeafMaskOperation operation = target.Register(fractions, state, workCounter, cancellationToken);
                    for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        state.Scene.AssignLeafMask(index, SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(index).LeafMask));
                    }
                    continue;
                }
                else if (state.ClipPolygons.TryGetValue(pending.ClipId, out SvgClipPolygon clipPolygon))
                {
                    if (clipPolygon.IsObjectBoundingBox)
                    {
                        SvgObjectBoundingClip bounds = new(pending.Transform, parent);
                        for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                            bounds.Add(state.Scene.GeometryAt(index), state, workCounter, cancellationToken);
                        SvgLeafMaskOperation operation = bounds.Register(clipPolygon, state, workCounter, cancellationToken);
                        for (int index = pending.FirstGeometryIndex; index < pending.FirstGeometryIndex + pending.GeometryCount; index++)
                        {
                            workCounter.Consume(cancellationToken);
                            state.Scene.AssignLeafMask(index, SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(index).LeafMask));
                        }
                        continue;
                    }
                    if (parent is not null && !parent.Value.IsTrivial && clipPolygon.ContourEnds.Length != 1 && !clipPolygon.UsesNativeReplay)
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths in transformed viewports are not implemented.");
                    if (pending.GeometryCount == 0) continue;
                    if (clipPolygon.ContourEnds.Length == 1 || clipPolygon.UsesNativeReplay)
                    {
                        // Native clip coverage multiplies each paint operation independently.
                        // Keep its source contours and shader coordinates intact instead of
                        // replacing them with a geometric intersection of flattened vertices.
                        SvgAffineTransform map = pending.Transform;
                        if (parent is {} viewport) map = SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(map);
                        SvgClipRegistration registration;
                        if (clipPolygon.NestedClipId is not null)
                        {
                            (SvgClipBox nestedBounds, int nestedIndex) = RegisterNestedResourceMask(pending.ClipId, clipPolygon, map, state.Scene, state, workCounter, cancellationToken);
                            registration = new(nestedBounds, nestedIndex, -1);
                        }
                        else registration = clipPolygon.Register(map, clipPolygon.UsesNativeReplay, state, state.Scene, workCounter, cancellationToken);
                        SvgClipBox bounds = registration.Bounds;
                        SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                        operation.Resolved = true;
                        operation.Value = 255;
                        registration.Bind(operation, state);
                        if (clipPolygon.NestedClipId is not null) { operation.RegionKind = SvgLayerRegionKind.Mask; operation.Region = null; }
                        SvgLeafMaskOperation? contentBounds = null;
                        if (clipPolygon.NestedClipId is not null)
                        {
                            contentBounds = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                            contentBounds.Resolved = true; contentBounds.Value = 255;
                            contentBounds.RegionKind = SvgLayerRegionKind.AffineViewport;
                            contentBounds.AffineViewport = new(bounds, SvgAffineTransform.Identity);
                        }
                        for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                        {
                            workCounter.Consume(cancellationToken);
                            SvgLeafMaskBinding? inner = state.Scene.GeometryAt(geometryIndex).LeafMask;
                            if (contentBounds is not null) inner = SvgLeafMaskComposition.NewBinding(state, contentBounds, inner);
                            SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, operation, inner);
                            state.Scene.AssignLeafMask(geometryIndex, binding);
                        }
                    }
                    else
                    {
                        if (pending.Transform != SvgAffineTransform.Identity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Placed compound clip paths are not implemented.");
                        for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                        {
                            workCounter.Consume(cancellationToken);
                            state.Scene.ReplaceGeometry(geometryIndex, ApplyPolygonClip(state.Scene.GeometryAt(geometryIndex), clipPolygon, state.Budget, workCounter, cancellationToken));
                        }
                    }
                    continue;
                }
                else throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local clip-path references are implemented.");
                for (int geometryIndex = pending.FirstGeometryIndex; geometryIndex < pending.FirstGeometryIndex + pending.GeometryCount; geometryIndex++)
                {
                    workCounter.Consume(cancellationToken);
                    state.Scene.ReplaceGeometry(geometryIndex, ApplyLeafClip(state.Scene.GeometryAt(geometryIndex), clip));
                }
            }
            finally { state.DefinitionReplayScene = previous; }
        }

        static SvgGeometry ApplyPolygonClip(SvgGeometry geometry, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(budget);
            ArgumentNullException.ThrowIfNull(workCounter);
            return geometry.Kind switch
            {
                SvgGeometryKind.PolygonFill => ClipSolidPolygon(geometry.Polygon, clip, budget, workCounter, cancellationToken),
                SvgGeometryKind.GradientPolygon => ClipGradientPolygon(geometry.GradientPolygon, clip, budget, workCounter, cancellationToken),
                SvgGeometryKind.RadialPolygon => ClipRadialPolygon(geometry.RadialPolygon, clip, budget, workCounter, cancellationToken),
                SvgGeometryKind.PatternPolygon => ClipPatternPolygon(geometry.PatternPolygon, clip, budget, workCounter, cancellationToken),
                _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Polygon clip-paths on this geometry are not implemented."),
            };

            static SvgGeometry ClipSolidPolygon(SvgPolygonFill polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, clip, budget, workCounter, cancellationToken);
                try
                {
                    SvgPolygonFill clipped = SvgPolygonFill.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Color, polygon.HasFill, polygon.IsEvenOdd, polygon.Mapping, polygon.Clip, null, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromPolygonFill(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }

            static SvgGeometry ClipGradientPolygon(SvgGradientPolygon polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, clip, budget, workCounter, cancellationToken);
                try
                {
                    SvgGradientPolygon clipped = SvgGradientPolygon.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Gradient, polygon.Box, polygon.HasFill, polygon.IsEvenOdd, polygon.Opacity, polygon.Mapping, polygon.Clip, null, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromGradientPolygon(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }

            static SvgGeometry ClipRadialPolygon(SvgRadialPolygon polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, clip, budget, workCounter, cancellationToken);
                try
                {
                    SvgRadialPolygon clipped = SvgRadialPolygon.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, polygon.Gradient, polygon.Box, polygon.HasFill, polygon.IsEvenOdd, polygon.Opacity, polygon.Mapping, polygon.Clip, null, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromRadialPolygon(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }

            static SvgGeometry ClipPatternPolygon(SvgPatternPolygon polygon, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                (OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<int> signs) = IntersectSubjectContours(polygon.Points, polygon.ContourEnds, polygon.Signs, polygon.IsEvenOdd, clip, budget, workCounter, cancellationToken);
                try
                {
                    SvgPatternPolygon clipped = SvgPatternPolygon.Create(points.Items, ends.Items, polygon.Signs is null ? null : signs.Items, Array.Empty<SvgQuadSpan>(), Array.Empty<SvgCubicSpan>(), polygon.Content, polygon.ContentPaint, polygon.OriginX, polygon.OriginY, polygon.TileWidth, polygon.TileHeight, polygon.HasFill, polygon.IsEvenOdd, polygon.Opacity, polygon.Mapping, polygon.Clip, polygon.VectorContent, polygon.ShaderPlacement, polygon.ContentViewBox, polygon.Coordinates, polygon.PaintMatrices, budget);
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    return SvgGeometry.FromPatternPolygon(clipped);
                }
                catch
                {
                    points.Dispose();
                    ends.Dispose();
                    signs.Dispose();
                    throw;
                }
            }
        }

        static (OwnedParserArray<SvgDevicePoint> Points, OwnedParserArray<int> Ends, OwnedParserArray<int> Signs) IntersectSubjectContours(SvgDevicePoint[] points, int[] ends, int[]? signs, bool subjectIsEvenOdd, SvgClipPolygon clip, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(points);
            ArgumentNullException.ThrowIfNull(ends);
            ArgumentNullException.ThrowIfNull(clip.Points);
            ArgumentNullException.ThrowIfNull(clip.ContourEnds);
            ArgumentNullException.ThrowIfNull(budget);
            ArgumentNullException.ThrowIfNull(workCounter);
            if (subjectIsEvenOdd) RequireDisjointClipContours(clip, workCounter, cancellationToken);
            using BudgetedParserList<SvgDevicePoint> kept = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<int> keptEnds = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<int> keptSigns = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<SvgDevicePoint> ping = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            using BudgetedParserList<SvgDevicePoint> pong = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
            int clipStart = 0;
            for (int clipContour = 0; clipContour < clip.ContourEnds.Length; clipContour++)
            {
                workCounter.Consume(cancellationToken);
                int clipEnd = clip.ContourEnds[clipContour];
                ReadOnlySpan<SvgDevicePoint> clipRing = clip.Points.AsSpan(clipStart, clipEnd - clipStart);
                bool clipCounterClockwise = ClipPolygonArea(clipRing, workCounter, cancellationToken) > 0;
                int start = 0;
                for (int contour = 0; contour < ends.Length; contour++)
                {
                    workCounter.Consume(cancellationToken);
                    int end = ends[contour];
                    ping.Clear();
                    for (int index = start; index < end; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        SvgDevicePoint vertex = points[index];
                        if (ping.Count > 0 && ping[ping.Count - 1] == vertex) continue;
                        ping.Add(vertex);
                    }
                    if (ping.Count > 1 && ping[0] == ping[ping.Count - 1]) ping.RemoveLast();
                    if (ping.Count >= 3) ClipRingToConvexPolygon(ping, pong, clipRing, clipCounterClockwise, workCounter, cancellationToken);
                    else ping.Clear();
                int keptBefore = kept.Count;
                for (int index = 0; index < ping.Count; index++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgDevicePoint vertex = ping[index];
                    if (kept.Count > keptBefore && kept[kept.Count - 1] == vertex) continue;
                    kept.Add(vertex);
                }
                if (kept.Count > keptBefore && kept[keptBefore] == kept[kept.Count - 1]) kept.RemoveLast();
                if (kept.Count - keptBefore >= 3)
                {
                    keptEnds.Add(kept.Count);
                    if (signs is not null) keptSigns.Add(contour < signs.Length ? signs[contour] : 0);
                }
                else while (kept.Count > keptBefore) kept.RemoveLast();
                start = end;
                }
                clipStart = clipEnd;
            }
            return (kept.Detach(), keptEnds.Detach(), keptSigns.Detach());

            static void RequireDisjointClipContours(SvgClipPolygon clip, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                for (int first = 0; first < clip.ContourEnds.Length; first++)
                {
                    for (int second = first + 1; second < clip.ContourEnds.Length; second++)
                    {
                        workCounter.Consume(cancellationToken);
                        ReadOnlySpan<SvgDevicePoint> left = ClipContourSpan(clip, first);
                        ReadOnlySpan<SvgDevicePoint> right = ClipContourSpan(clip, second);
                        for (int leftEdge = 0; leftEdge < left.Length; leftEdge++)
                        {
                            for (int rightEdge = 0; rightEdge < right.Length; rightEdge++)
                            {
                                workCounter.Consume(cancellationToken);
                                SvgDevicePoint p1 = left[leftEdge];
                                SvgDevicePoint p2 = left[(leftEdge + 1) % left.Length];
                                SvgDevicePoint q1 = right[rightEdge];
                                SvgDevicePoint q2 = right[(rightEdge + 1) % right.Length];
                                if (SegmentsProperlyCross(p1, p2, q1, q2) || SegmentsOverlap(p1, p2, q1, q2)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Overlapping multi-contour clip paths are not implemented.");
                            }
                        }
                        if (RingStrictlyContains(left, right, workCounter, cancellationToken) || RingStrictlyContains(right, left, workCounter, cancellationToken)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Overlapping multi-contour clip paths are not implemented.");
                    }
                }

                static ReadOnlySpan<SvgDevicePoint> ClipContourSpan(SvgClipPolygon clip, int contour)
                {
                    int start = contour == 0 ? 0 : clip.ContourEnds[contour - 1];
                    return clip.Points.AsSpan(start, clip.ContourEnds[contour] - start);
                }

                static double Orient(SvgDevicePoint a, SvgDevicePoint b, SvgDevicePoint c)
                    => ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

                static bool SegmentsProperlyCross(SvgDevicePoint p1, SvgDevicePoint p2, SvgDevicePoint q1, SvgDevicePoint q2)
                {
                    double o1 = Orient(p1, p2, q1);
                    double o2 = Orient(p1, p2, q2);
                    double o3 = Orient(q1, q2, p1);
                    double o4 = Orient(q1, q2, p2);
                    return ((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0));
                }

                static bool SegmentsOverlap(SvgDevicePoint p1, SvgDevicePoint p2, SvgDevicePoint q1, SvgDevicePoint q2)
                {
                    if (Orient(p1, p2, q1) != 0 || Orient(p1, p2, q2) != 0) return false;
                    double ax = Math.Abs(p2.X - p1.X);
                    double ay = Math.Abs(p2.Y - p1.Y);
                    if (ax >= ay)
                    {
                        double lo = Math.Max(Math.Min(p1.X, p2.X), Math.Min(q1.X, q2.X));
                        double hi = Math.Min(Math.Max(p1.X, p2.X), Math.Max(q1.X, q2.X));
                        return hi > lo;
                    }
                    double loY = Math.Max(Math.Min(p1.Y, p2.Y), Math.Min(q1.Y, q2.Y));
                    double hiY = Math.Min(Math.Max(p1.Y, p2.Y), Math.Max(q1.Y, q2.Y));
                    return hiY > loY;
                }

                static bool RingStrictlyContains(ReadOnlySpan<SvgDevicePoint> outer, ReadOnlySpan<SvgDevicePoint> inner, ParserWorkCounter workCounter, CancellationToken cancellationToken)
                {
                    for (int index = 0; index < inner.Length; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        if (RingTouchesBoundary(outer, inner[index])) continue;
                        if (IsStrictlyInside(outer, inner[index])) return true;
                    }
                    return false;
                }

                static bool RingTouchesBoundary(ReadOnlySpan<SvgDevicePoint> ring, SvgDevicePoint point)
                {
                    for (int index = 0; index < ring.Length; index++)
                    {
                        if (Orient(ring[index], ring[(index + 1) % ring.Length], point) == 0) return true;
                    }
                    return false;
                }

                static bool IsStrictlyInside(ReadOnlySpan<SvgDevicePoint> ring, SvgDevicePoint point)
                {
                    bool inside = false;
                    for (int index = 0; index < ring.Length; index++)
                    {
                        SvgDevicePoint a = ring[index];
                        SvgDevicePoint b = ring[(index + 1) % ring.Length];
                        if ((a.Y > point.Y) != (b.Y > point.Y))
                        {
                            double x = a.X + ((point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                            if (x > point.X) inside = !inside;
                        }
                    }
                    return inside;
                }
            }

            static double ClipPolygonArea(ReadOnlySpan<SvgDevicePoint> ring, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                double twiceArea = 0;
                for (int index = 0; index < ring.Length; index++)
                {
                    if ((index & 1023) == 0) workCounter.Consume(cancellationToken);
                    SvgDevicePoint current = ring[index];
                    SvgDevicePoint next = ring[(index + 1) % ring.Length];
                    twiceArea += (current.X * next.Y) - (next.X * current.Y);
                }
                return twiceArea / 2;
            }

            static void ClipRingToConvexPolygon(BudgetedParserList<SvgDevicePoint> ping, BudgetedParserList<SvgDevicePoint> pong, ReadOnlySpan<SvgDevicePoint> clip, bool clipCounterClockwise, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                bool outputInPing = true;
                for (int edge = 0; edge < clip.Length; edge++)
                {
                    workCounter.Consume(cancellationToken);
                    SvgDevicePoint a = clip[edge];
                    SvgDevicePoint b = clip[(edge + 1) % clip.Length];
                    BudgetedParserList<SvgDevicePoint> source = outputInPing ? ping : pong;
                    BudgetedParserList<SvgDevicePoint> target = outputInPing ? pong : ping;
                    target.Clear();
                    if (source.Count == 0)
                    {
                        ping.Clear();
                        return;
                    }
                    SvgDevicePoint s = source[source.Count - 1];
                    bool sInside = IsInsideClipHalfPlane(s, a, b, clipCounterClockwise);
                    for (int index = 0; index < source.Count; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        SvgDevicePoint e = source[index];
                        bool eInside = IsInsideClipHalfPlane(e, a, b, clipCounterClockwise);
                        if (eInside)
                        {
                            if (!sInside) target.Add(ClipEdgeIntersection(s, e, a, b, workCounter, cancellationToken));
                            target.Add(e);
                        }
                        else if (sInside) target.Add(ClipEdgeIntersection(s, e, a, b, workCounter, cancellationToken));
                        s = e;
                        sInside = eInside;
                    }
                    outputInPing = !outputInPing;
                }
                if (outputInPing) return;
                ping.Clear();
                for (int index = 0; index < pong.Count; index++)
                {
                    workCounter.Consume(cancellationToken);
                    ping.Add(pong[index]);
                }
            }

            static bool IsInsideClipHalfPlane(SvgDevicePoint point, SvgDevicePoint a, SvgDevicePoint b, bool clipCounterClockwise)
            {
                double cross = ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X));
                return clipCounterClockwise ? cross >= 0 : cross <= 0;
            }

            static SvgDevicePoint ClipEdgeIntersection(SvgDevicePoint s, SvgDevicePoint e, SvgDevicePoint a, SvgDevicePoint b, ParserWorkCounter workCounter, CancellationToken cancellationToken)
            {
                workCounter.Consume(cancellationToken);
                double dx = e.X - s.X;
                double dy = e.Y - s.Y;
                double ex = b.X - a.X;
                double ey = b.Y - a.Y;
                double denominator = (dx * ey) - (dy * ex);
                if (denominator == 0) return e;
                double t = ((a.X - s.X) * ey - (a.Y - s.Y) * ex) / denominator;
                return new SvgDevicePoint(s.X + (t * dx), s.Y + (t * dy));
            }
        }


    }
    // Build a bounded white stencil from borrowed native paths. The containing
    // scene adopts its immutable replay owner. Native clips construct inside
    // the mask; target alpha-mask rounding remains separate. Existing owners
    // enforce work, cancellation, material reservations and cleanup.
    internal static (SvgClipBox Bounds, int ContentIndex) RegisterNestedResourceMask(string id, SvgClipPolygon polygon,
        SvgAffineTransform map, SvgSceneBuilder owner, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgAuthoredPath source = polygon.SourcePath ?? throw new InvalidOperationException("Nested clip replay requires source bounds.");
        SvgRectangle bounds = SvgPathBounds.Compute(source.Commands, map.ComposeChecked(source.SceneMap), work, token);
        double margin = 2 / state.OutputMagnification;
        using SvgSceneBuilder builder = new(state.Budget, work, token);
        SvgLeafMaskOperation contour = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        contour.Resolved = true; contour.Value = 255; contour.NativeClip = new(id, map);
        contour.RegionKind = SvgLayerRegionKind.AffineViewport;
        SvgLeafMaskBinding binding = SvgLeafMaskComposition.NewBinding(state, contour, null);
        string nestedId = polygon.NestedClipId ?? throw new InvalidOperationException("Nested clip replay requires a child clip.");
        binding = BindResourceClip(nestedId, map.ComposeChecked(source.SceneMap), binding, state, work, token) ?? binding;
        builder.AddGeometry(SvgGeometry.FromRectangle(new(bounds.X - margin, bounds.Y - margin,
            bounds.Width + margin * 2, bounds.Height + margin * 2, new(255, 255, 255, 255), true, null)) with { LeafMask = binding });
        SvgLeafMaskComposition.Emit(state, builder, work, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { owner.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        int index = owner.AddMaskContent(new([], 255, true) { VectorContent = content });
        return (new(bounds.X, bounds.Y, bounds.Width, bounds.Height), index);
    }

    /// <summary>Applies deferred userSpace clip paths to direct group command ranges at document end, after use expansion.</summary>
    internal static void ResolvePendingGroupClips(SvgDocumentState state, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        => ResolvePendingGroupClips(state, 0, workCounter, cancellationToken);

    /// <summary>Binds an appended source-local suffix before its scene transfers to its parent.</summary>
    internal static void ResolvePendingGroupClips(SvgDocumentState state, int first, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workCounter);
        if (first == state.PendingGroupClips.Count) return;
        using BudgetedParserList<CommandScope> scopes = new(state.Budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
        using BudgetedParserDictionary<SvgSceneBuilder, SceneScopeHead> heads = new(state.Budget, SvgMemoryCategory.Parser, 48);
        // References were recorded at group entry. Prepend inner scopes first
        // so the emitted native clip stack follows outer-to-inner document order.
        for (int pendingIndex = state.PendingGroupClips.Count - 1; pendingIndex >= first; pendingIndex--)
        {
            workCounter.Consume(cancellationToken);
            PendingGroupClip pending = state.PendingGroupClips[pendingIndex];
            if (IsDisabled(pending.ClipId, state)) continue;
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = pending.Scene;
            try
            {
                SvgViewportFrame? parent = pending.Parent;
                SvgClipBox clip;
                if (state.Clips.TryGetValue(pending.ClipId, out SvgClipBox box))
                {
                    if (ContainsIsolatedLayer(pending))
                    {
                        SvgAffineTransform map = parent is {} viewport
                            ? SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(pending.Transform)
                            : pending.Transform;
                        SvgDrawCommand begin = CreateRectangleScope(box, map, state, state.Scene, workCounter, cancellationToken);
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
                            workCounter.Consume(cancellationToken);
                            SvgDrawCommand command = state.Scene.CommandAt(index);
                            if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                            SvgLeafMaskBinding? inner = state.Scene.GeometryAt(command.GeometryIndex).LeafMask;
                            binding = binding is null
                                ? BindResourceClip(pending.ClipId, map, inner, state, workCounter, cancellationToken)
                                : SvgLeafMaskComposition.NewBinding(state, binding.Operation, inner);
                            state.Scene.AssignLeafMask(command.GeometryIndex, viewport is null ? binding : SvgLeafMaskComposition.NewBinding(state, viewport, binding));
                        }
                        continue;
                    }
                    clip = MapGroupClip(box, pending.Transform, parent, workCounter, cancellationToken);
                }
                else if (state.ObjectClips.TryGetValue(pending.ClipId, out SvgClipBox fractions))
                {
                    SvgObjectBoundingClip target = new(pending.Transform, parent);
                    for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        SvgDrawCommand command = state.Scene.CommandAt(index);
                        if (command.Kind == SvgDrawCommandKind.Geometry) target.Add(state.Scene.GeometryAt(command.GeometryIndex), state, workCounter, cancellationToken);
                    }
                    SvgLeafMaskOperation operation = target.Register(fractions, state, workCounter, cancellationToken);
                    if (ContainsIsolatedLayer(pending))
                    {
                        WrapOperation(pending, operation);
                        continue;
                    }
                    for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                    {
                        workCounter.Consume(cancellationToken);
                        SvgDrawCommand command = state.Scene.CommandAt(index);
                        if (command.Kind == SvgDrawCommandKind.Geometry)
                            state.Scene.AssignLeafMask(command.GeometryIndex, SvgLeafMaskComposition.NewBinding(state, operation, state.Scene.GeometryAt(command.GeometryIndex).LeafMask));
                    }
                    continue;
                }
                else if (state.ClipPolygons.TryGetValue(pending.ClipId, out SvgClipPolygon polygon))
                {
                    if (polygon.IsObjectBoundingBox)
                    {
                        SvgObjectBoundingClip target = new(pending.Transform, parent);
                        for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                        {
                            workCounter.Consume(cancellationToken);
                            SvgDrawCommand command = state.Scene.CommandAt(index);
                            if (command.Kind == SvgDrawCommandKind.Geometry) target.Add(state.Scene.GeometryAt(command.GeometryIndex), state, workCounter, cancellationToken);
                        }
                        SvgLeafMaskOperation boundingOperation = target.Register(polygon, state, workCounter, cancellationToken);
                        if (ContainsIsolatedLayer(pending))
                        {
                            WrapOperation(pending, boundingOperation);
                            continue;
                        }
                        for (int index = pending.FirstCommandIndex; index < pending.EndCommandIndex; index++)
                        {
                            workCounter.Consume(cancellationToken);
                            SvgDrawCommand command = state.Scene.CommandAt(index);
                            if (command.Kind != SvgDrawCommandKind.Geometry) continue;
                            state.Scene.AssignLeafMask(command.GeometryIndex, SvgLeafMaskComposition.NewBinding(state, boundingOperation, state.Scene.GeometryAt(command.GeometryIndex).LeafMask));
                        }
                        continue;
                    }
                    if (polygon.ContourEnds.Length != 1 && !polygon.UsesNativeReplay) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-contour polygon clip-paths on groups are implemented.");
                    SvgAffineTransform map = pending.Transform;
                    if (parent is {} viewport)
                        map = SvgAffineTransform.FromScaleOffset(viewport.ScaleX, viewport.ScaleY, viewport.OffsetX, viewport.OffsetY).ComposeChecked(map);
                    SvgClipRegistration registration;
                    if (polygon.NestedClipId is not null)
                    {
                        (SvgClipBox nestedBounds, int nestedIndex) = RegisterNestedResourceMask(pending.ClipId, polygon, map, state.Scene, state, workCounter, cancellationToken);
                        registration = new(nestedBounds, nestedIndex, -1);
                    }
                    else registration = polygon.Register(map, polygon.UsesNativeReplay, state, state.Scene, workCounter, cancellationToken);
                    SvgClipBox bounds = registration.Bounds;
                    SvgLeafMaskOperation operation = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
                    operation.Resolved = true;
                    operation.Value = 255;
                    registration.Bind(operation, state);
                    if (polygon.NestedClipId is not null) { operation.Region = null; operation.RegionKind = SvgLayerRegionKind.Mask; }
                    if (ContainsIsolatedLayer(pending))
                    {
                        WrapOperation(pending, operation);
                        continue;
                    }
                    for (int commandIndex = pending.FirstCommandIndex; commandIndex < pending.EndCommandIndex; commandIndex++)
                    {
                        workCounter.Consume(cancellationToken);
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
                    workCounter.Consume(cancellationToken);
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
            if (operation.AffineViewport is {} clip)
                (operation.Region, operation.ContentIndex) = clip.Register(state, pending.Scene, workCounter, cancellationToken);
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
                workCounter.Consume(cancellationToken);
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
                workCounter.Consume(cancellationToken);
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
            BudgetedSceneList<SvgDrawCommand> commands = new(state.Budget, workCounter, cancellationToken);
            using BudgetedParserList<int> ends = new(state.Budget, SvgMemoryCategory.Parser, workCounter, cancellationToken);
            bool transferred = false;
            try
            {
                int next = head.First;
                for (int index = 0; index <= count; index++)
                {
                    workCounter.Consume(cancellationToken);
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
                SvgReferenceResolver.RemapPendingCommandRanges(state, scene, positions, new(0, 0, 0, 0), state.PendingCommandEnd, workCounter, cancellationToken);
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
    /// <remarks>The group supplies its complete inherited axis-aligned map; the parent frame supplies its scalar viewBox and physical clip. Mapping charges four boundary visits before arithmetic and preserves AA provenance through reflection. This constant-size operation owns no storage and retains no state.</remarks>
    internal static SvgClipBox MapGroupClip(SvgClipBox box, SvgAffineTransform transform, SvgViewportFrame? parent, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        for (int boundary = 0; boundary < 4; boundary++) workCounter.Consume(cancellationToken);
        return PlaceViewportClip(box, transform, parent);
    }

    /// <summary>Places one finite axis clip through bounded value arithmetic; node traversal owns work/cancellation.</summary>
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
