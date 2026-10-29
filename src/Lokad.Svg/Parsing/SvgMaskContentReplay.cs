using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains one ordered mask child with its validated attributes and inherited context.</summary>
/// <remarks>The document owns copied attributes and list storage. Replay is parser-thread confined;
/// each child lowers through the existing geometry/reference machinery after definitions resolve.</remarks>
internal readonly record struct SvgMaskReplayChild(SvgDefinitionLeafSource Source, SvgPaintableElementContext Context)
{
    /// <summary>Borrows the sealed group from its document definition owner; mask replay never disposes it.</summary>
    internal SvgSymbolContentRange? GroupContent { get; init; }
}

internal readonly record struct SvgDeferredMaskContent(string Id, int FirstChild, int ChildCount, bool IsAlpha, bool IsObjectBoundingBox);

/// <summary>Promotes multi-child masks to bounded ordered vector scenes without changing legacy single-child routes.</summary>
/// <remarks>Source snapshots belong to the document; the containing scene owns sealed vector content.
/// All snapshots, replay and copies observe cancellation token. Failure disposes
/// partial builders and geometry; borrowed definitions and source attributes remain unchanged.</remarks>
internal static class SvgMaskContentReplay
{
    internal static bool Retain(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, CancellationToken token)
    {
        bool group = reader.LocalName == "g";
        if (!group && reader.LocalName != "use" && !SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) return false;
        switch (reader.LocalName)
        {
            case "g": SvgElementAttributePolicy.ValidateGroup(reader, token); break;
            case "use": SvgElementAttributePolicy.ValidateUse(reader, token); break;
            case "rect": SvgElementAttributePolicy.ValidateRectangle(reader, token); break;
            case "circle": case "ellipse": SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, token); break;
            case "path": SvgElementAttributePolicy.ValidatePath(reader, token); break;
            case "line": SvgElementAttributePolicy.ValidateLine(reader, token); break;
            default: SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, token); break;
        }
        if (SvgLocalUrl.ReadReference(reader, "mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
        bool clipped = SvgLocalUrl.ReadReference(reader, "clip-path") is not null;
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, colors, token);
        SvgDefinitionLeafSource source = SvgDefinitionLeafSource.Capture(reader, context, state);
        if (state.MaskReplayChildCount == 0) state.MaskReplayChildStart = state.MaskReplayChildren.Count;
        state.MaskReplayChildren.Add(new(source, context with { AuthoredStyle = source.AuthoredStyle, StyleSheet = default }));
        state.MaskReplayChildCount++;
        if (group)
        {
            state.MaskGroupChildIndex = state.MaskReplayChildren.Count - 1;
            state.SymbolDefinition = SvgSymbolDefinitionBuilder.Create(state.Budget, token,
                new(SvgPaint.Default, default, null, false, null, null, SvgPaintOpacity.Opaque));
            _ = SvgSymbolContentParser.TryParse(reader, root, state, colors, token, true);
            if (reader.IsEmptyElement) SvgDefinitionParser.RegisterSymbol(state, token);
            else state.SymbolDepth = reader.Depth;
        }
        // Shader strokes and unresolved fills need the ordinary geometry route.
        // The single-color shortcut cannot resolve independent or forward paints.
        string? fillServer = source.AuthoredStyle.Fill ?? context.InheritedPresentation.PaintServerFill;
        bool pendingFill = fillServer is not null && SvgLocalUrl.TryParseId(fillServer, out string fillId)
            && !state.Gradients.TryGetValue(fillId, out _) && !state.RadialGradients.TryGetValue(fillId, out _) && !state.Patterns.TryGetValue(fillId, out _);
        bool requiresReplay = pendingFill || SvgGradientSyntax.IsGradientStrokeUrl(source.AuthoredStyle.Stroke ?? context.InheritedStroke.Stroke);
        if (!requiresReplay && reader.LocalName is "rect" or "circle" && !SvgGradientSyntax.IsGradientFill(source.AuthoredStyle.Fill))
        {
            SvgResolvedShapePresentation paint = context.ResolveShape(reader, true).Presentation;
            SvgColor fill = paint.Color;
            // Empty/hidden content and stroke-only shapes need the same ordered
            // scene behavior as their grouped forms, rather than a painted shortcut.
            requiresReplay = !paint.HasFill || paint.StrokeColor is not null
                || !state.MaskIsAlpha && (fill.Red != fill.Green || fill.Red != fill.Blue)
                || reader.LocalName == "circle" && SvgEllipseSyntax.ParseCircle(reader, context.LengthBases).RadiusX == 0;
        }
        if (!clipped && !requiresReplay && !state.MaskObjectBoundingBoxContent && !group && reader.LocalName != "use" && state.MaskReplayChildCount == 1 && (reader.LocalName is "rect" or "circle"
            || SvgGradientSyntax.IsGradientFill(context.ResolveStyle(reader).Fill))) return false;
        if (!state.MaskHasReplayContent)
        {
            state.MaskContentBuilder?.Dispose();
            state.MaskContentBuilder = null;
            if (state.MaskContentGeometries is {} geometries) foreach (SvgGeometry geometry in geometries) geometry.Dispose();
            state.MaskContentGeometries = null;
            state.MaskContentBox = null;
            state.MaskValue = null;
            state.MaskHasReplayContent = true;
        }
        if (!group) SvgStructuralAdmission.ConsumeLeafChildren(reader, state, token);
        return true;
    }

    internal static void Register(SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string id = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask content registration requires an identifier."));
        if (state.Masks.TryGetValue(id, out _) || !state.MaskContents.TryAdd(id, new([], 255, state.MaskIsAlpha)))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is {} region && !state.MaskRegions.TryAdd(id, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskFractionRegion is {} fractions && !state.MaskFractionRegions.TryAdd(id, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        state.DeferredMaskContents.Add(new(id, state.MaskReplayChildStart, state.MaskReplayChildCount, state.MaskIsAlpha, state.MaskObjectBoundingBoxContent));
    }

    internal static void Resolve(SvgRootAttributes root, SvgDocumentState state, CancellationToken token)
    {
        foreach (SvgEmptyGroupMaskReference reference in state.EmptyGroupMaskReferences.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            if (!state.Masks.TryGetValue(reference.Id, out _) && !state.MaskContents.TryGetValue(reference.Id, out _))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local mask references are implemented.");
        }
        state.MaskReplayBases = new(root.UserViewportWidth, root.UserViewportHeight);
        foreach (SvgDeferredMaskContent pending in state.DeferredMaskContents.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            SvgMaskSourceRange source = new(pending.FirstChild, pending.ChildCount);
            for (int index = source.FirstChild; index < source.FirstChild + source.ChildCount; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgMaskReplayChild child = state.MaskReplayChildren[index];
                if (child.Source.LocalName != "use") continue;
                SvgResourceUse? use = SvgResourceUseReplay.Retain(child.Source, child.Context, state.MaskReplayBases, state);
                if (use is {} retained && !SvgReferenceResolver.TryGetDefinition(retained.Reference, state.Definitions, out _)
                    && !state.RenderedGroupDefinitions.TryGetValue(retained.Reference.Id, out _))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local geometry references in resource content are implemented.");
            }
            state.MaskContents.Replace(pending.Id, new([], 255, pending.IsAlpha)
                { IsObjectBoundingBox = pending.IsObjectBoundingBox, ReplaySource = source });
        }
    }

    /// <summary>Replays borrowed source commands into one parent-owned mask scene at the complete target map.</summary>
    /// <remarks>Preparation alone borrows snapshots. Native constructors retain curve/paint distinctions;
    /// partial builders and owners release on failure, and all traversal observes cancellation.</remarks>
    private static SvgMaskContent Build(SvgMaskSourceRange range, SvgAffineTransform placement, bool isAlpha, SvgAffineViewportClip? region,
        SvgDocumentState state, CancellationToken token)
    {
        using SvgSceneBuilder builder = new(state.Budget, token);
        bool previousPicture = state.IsResourcePicture;
        state.IsResourcePicture = true;
        try
        {
            for (int childIndex = range.FirstChild; childIndex < range.FirstChild + range.ChildCount; childIndex++)
            {
                token.ThrowIfCancellationRequested();
                SvgMaskReplayChild child = state.MaskReplayChildren[childIndex];
                SvgPaintableElementContext context = child.Context with { InheritedPresentation = child.Context.InheritedPresentation with { Transform = placement.ComposeChecked(child.Context.InheritedPresentation.Transform) } };
                if (child.GroupContent is {} group)
                {
                    SvgUseReference reference = new(string.Empty, 0, 0, null, null,
                        context.InheritedPresentation.Transform, SvgAffineTransform.Identity, 255,
                        context.InheritedPresentation.IsDisplayed && context.InheritedPresentation.IsVisible, null, null, null)
                    {
                        InheritedFill = context.InheritedPresentation.Paint,
                        InheritedTextStyle = context.InheritedTextStyle,
                        PaintServerFill = context.InheritedPresentation.PaintServerFill,
                        PaintServerId = context.InheritedPresentation.PaintServerFill is {} fill ? SvgGradientSyntax.ParsePaintId(fill) : null,
                        InheritedStroke = context.InheritedStroke,
                        InheritedStrokeOpacity = context.InheritedStroke.StrokeOpacity is {} strokeOpacity ? SvgColorParser.ParsePaintOpacity(strokeOpacity) : SvgPaintOpacity.Opaque,
                        InheritedFillRule = context.InheritedFillRule,
                        InheritedPaintOrder = context.InheritedPresentation.PaintOrder,
                        InheritedMarkers = context.InheritedPresentation.Markers,
                        ParentElementOpacity = context.InheritedPresentation.ElementOpacity,
                        NonScalingStroke = context.InheritedPresentation.NonScalingStroke,
                        RecordingScope = state.NewRecordingScope(),
                        AllowsSolidPaintAnalysis = true,
                        IsDisplayed = context.InheritedPresentation.IsDisplayed,
                        IsVisible = context.InheritedPresentation.IsVisible
                    };
                    SvgGeometry[] mapped = SvgReferenceResolver.Apply(new([], false, null, null) { SymbolContent = group }, reference, null, state, token);
                    SvgPaintRecording.AdoptLeafScene(new(mapped, false, null, null), builder, state, token);
                }
                else if (child.Source.LocalName == "use")
                {
                    if (SvgResourceUseReplay.Retain(child.Source, context, state.MaskReplayBases, state) is {} reference)
                        SvgResourceUseReplay.AppendMaskUse(reference, builder, state, token);
                }
                else
                {
                    SvgDefinition lowered = SvgDefinitionParser.LowerInstanceSource(child.Source, context, state);
                    SvgPaintRecording.AdoptLeafScene(lowered, builder, state, token);
                }
            }
            if (region is {} clip)
            {
                (SvgClipBox bounds, int index) = clip.Register(state, builder, token);
                builder.InsertCommands(0, [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, bounds, index, 255) { RegionKind = SvgLayerRegionKind.AffineViewport }]);
                builder.EndOpacityScope();
            }
            SvgLeafMaskComposition.Emit(state, builder, token);
            SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
            try { state.Scene.AdoptVectorContent(content); }
            catch { content.Dispose(); throw; }
            return new([], 255, isAlpha) { VectorContent = content };
        }
        finally { state.IsResourcePicture = previousPicture; }
    }

    /// <summary>Materializes a live mask and places its borrowed content through existing native replay.</summary>
    /// <remarks>Only single-threaded preparation borrows source slots. Empty regions require no glyphs.
    /// Identity content is cached in the document registry and owned by the adopting scene; returned content
    /// keeps no staging reference. Cancellation covers replay and failures release scene owners.</remarks>
    internal static SvgMaskContent PlaceForTarget(string id, SvgMaskContent source, SvgAffineTransform map, SvgRectangle bounds,
        SvgMaskTarget? target, SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (state.MaskRegions.TryGetValue(id, out SvgClipBox explicitRegion) && (explicitRegion.Width == 0 || explicitRegion.Height == 0)
            || state.MaskFractionRegions.TryGetValue(id, out SvgClipBox fractions) && (fractions.Width == 0 || fractions.Height == 0))
            return new([], 255, source.IsAlpha);
        if (source.IsObjectBoundingBox)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return new([], 255, source.IsAlpha);
            SvgRectangle local;
            if (target is {} native) local = native.ObjectBounds;
            else
            {
                if (!map.IsAxisAligned || !map.TryInvert(out SvgAffineTransform inverse))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box mask content requires nonsingular target bounds.");
                local = inverse.Apply(bounds);
            }
            map = map.ComposeChecked(SvgAffineTransform.FromScaleOffset(local.Width, local.Height, local.X, local.Y));
        }
        map = map.ComposeChecked(source.CoordinateMap);
        if (map.IsAxisAligned)
        {
            if (source.VectorContent is null && source.ReplaySource is {} pending)
            {
                source = source with { VectorContent = Build(pending, SvgAffineTransform.Identity, source.IsAlpha, null, state, token).VectorContent };
                state.MaskContents.Replace(id, source);
            }
            return source with { CoordinateMap = map, ReplaySource = null };
        }
        SvgMaskSourceRange range = source.ReplaySource ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine mask content requires retained native sources.");
        SvgAffineViewportClip? region = target is {} clipped ? new(clipped.Region, clipped.Map) { IsBinary = true } : null;
        return Build(range, map, source.IsAlpha, region, state, token) with { UsesAffineTarget = true };
    }

    /// <summary>Materializes one uniform rectangle's native spatial paint when its scalar value cannot cover the target.</summary>
    /// <remarks>Source attributes and context remain borrowed from document staging. Identity replay caches one immutable
    /// scene; mapped replay owns a distinct scene. Both adopt through the existing parent owner, reserve material storage,
    /// observe cancellation during lowering and copying. Returned content never retains a staging source reference.</remarks>
    internal static SvgMaskContent ResolveUniformSource(string id, SvgAffineTransform placement, SvgDocumentState state,
CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!state.MaskContents.TryGetValue(id, out SvgMaskContent mask) || mask.UniformSource is not {} source)
            throw new InvalidOperationException("A uniform mask requires its retained rectangle source.");
        if (placement == SvgAffineTransform.Identity && mask.VectorContent is {} cached)
            return new([], 255, mask.IsAlpha) { VectorContent = cached };
        SvgMaskReplayChild child = state.MaskReplayChildren[source.ChildIndex];
        SvgPaintableElementContext context = child.Context with
        {
            InheritedPresentation = child.Context.InheritedPresentation with
            {
                Transform = placement.ComposeChecked(child.Context.InheritedPresentation.Transform)
            }
        };
        using SvgSceneBuilder builder = new(state.Budget, token);
        SvgDefinition lowered = SvgDefinitionParser.LowerInstanceSource(child.Source, context, state);
        SvgPaintRecording.AdoptLeafScene(lowered, builder, state, token);
        SvgLeafMaskComposition.Emit(state, builder, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        if (placement == SvgAffineTransform.Identity) state.MaskContents.Replace(id, mask with { VectorContent = content });
        return new([], 255, mask.IsAlpha) { VectorContent = content };
    }
}
