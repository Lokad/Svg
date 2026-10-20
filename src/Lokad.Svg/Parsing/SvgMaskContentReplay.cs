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
/// All snapshots, replay and copies charge shared work and observe cancellation. Failure disposes
/// partial builders and geometry; borrowed definitions and source attributes remain unchanged.</remarks>
internal static class SvgMaskContentReplay
{
    internal static bool Retain(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, ParserWorkCounter work, CancellationToken token)
    {
        bool group = reader.LocalName == "g";
        if (!group && reader.LocalName != "use" && !SvgStructuralVocabulary.IsDefinitionLeaf(reader.LocalName)) return false;
        switch (reader.LocalName)
        {
            case "g": SvgElementAttributePolicy.ValidateGroup(reader, work, token); break;
            case "use": SvgElementAttributePolicy.ValidateUse(reader, work, token); break;
            case "rect": SvgElementAttributePolicy.ValidateRectangle(reader, work, token); break;
            case "circle": case "ellipse": SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, work, token); break;
            case "path": SvgElementAttributePolicy.ValidatePath(reader, work, token); break;
            case "line": SvgElementAttributePolicy.ValidateLine(reader, work, token); break;
            default: SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, work, token); break;
        }
        if (reader.GetAttribute("mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
        bool clipped = reader.GetAttribute("clip-path") is not null;
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, colors, work, token);
        SvgDefinitionLeafSource source = SvgDefinitionLeafSource.Capture(reader, context, state);
        if (state.MaskReplayChildCount == 0) state.MaskReplayChildStart = state.MaskReplayChildren.Count;
        state.MaskReplayChildren.Add(new(source, context with { AuthoredStyle = source.AuthoredStyle, StyleSheet = default }));
        state.MaskReplayChildCount++;
        if (group)
        {
            state.MaskGroupChildIndex = state.MaskReplayChildren.Count - 1;
            state.SymbolDefinition = SvgSymbolDefinitionBuilder.Create(state.Budget, work, token,
                new(SvgPaint.Default, default, null, false, null, null, SvgPaintOpacity.Opaque));
            _ = SvgSymbolContentParser.TryParse(reader, root, state, colors, work, token, true);
            if (reader.IsEmptyElement) SvgDefinitionParser.RegisterSymbol(state, work, token);
            else state.SymbolDepth = reader.Depth;
        }
        bool requiresVectorPaint = false;
        if (reader.LocalName is "rect" or "circle" && !SvgGradientSyntax.IsGradientFill(source.AuthoredStyle.Fill))
        {
            SvgResolvedShapePresentation paint = context.ResolveShape(reader, true).Presentation;
            SvgColor fill = paint.Color;
            requiresVectorPaint = paint.HasFill && paint.StrokeColor is not null
                || !state.MaskIsAlpha && (fill.Red != fill.Green || fill.Red != fill.Blue
                    || paint.StrokeColor is {} stroke && (stroke.Red != stroke.Green || stroke.Red != stroke.Blue));
        }
        if (!clipped && !requiresVectorPaint && !state.MaskObjectBoundingBoxContent && !group && state.MaskReplayChildCount == 1 && (reader.LocalName is "use" or "rect" or "circle"
            || SvgGradientSyntax.IsGradientFill(context.ResolveStyle(reader).Fill))) return false;
        if (!state.MaskHasReplayContent)
        {
            state.MaskContentBuilder?.Dispose();
            state.MaskContentBuilder = null;
            if (state.MaskContentGeometries is {} geometries) foreach (SvgGeometry geometry in geometries) geometry.Dispose();
            state.MaskContentGeometries = null;
            state.MaskContentBox = null;
            state.MaskValue = null;
            state.MaskUse = null;
            state.MaskHasReplayContent = true;
        }
        state.HasUnsupportedContent |= !group && !reader.IsEmptyElement;
        return true;
    }

    internal static void Register(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        string id = state.Strings.Copy(state.MaskId ?? throw new InvalidOperationException("Mask content registration requires an identifier."));
        if (state.Masks.TryGetValue(id, out _) || !state.MaskContents.TryAdd(id, new([], 255, state.MaskIsAlpha)))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskCustomRegion is {} region && !state.MaskRegions.TryAdd(id, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        if (state.MaskFractionRegion is {} fractions && !state.MaskFractionRegions.TryAdd(id, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        state.DeferredMaskContents.Add(new(id, state.MaskReplayChildStart, state.MaskReplayChildCount, state.MaskIsAlpha, state.MaskObjectBoundingBoxContent));
    }

    internal static void Resolve(SvgRootAttributes root, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        state.MaskReplayBases = new(root.UserViewportWidth, root.UserViewportHeight);
        foreach (SvgDeferredMaskContent pending in state.DeferredMaskContents.AsSpan())
        {
            work.Consume(token);
            SvgMaskSourceRange source = new(pending.FirstChild, pending.ChildCount);
            SvgMaskContent content = Build(source, SvgAffineTransform.Identity, pending.IsAlpha, null, state, work, token);
            state.MaskContents.Replace(pending.Id, content with { IsObjectBoundingBox = pending.IsObjectBoundingBox, ReplaySource = source });
        }
    }

    /// <summary>Replays borrowed source commands into one parent-owned mask scene at the complete target map.</summary>
    /// <remarks>Preparation alone borrows snapshots. Native constructors retain curve/paint distinctions;
    /// partial builders and owners release on failure, and all traversal observes shared work/token.</remarks>
    private static SvgMaskContent Build(SvgMaskSourceRange range, SvgAffineTransform placement, bool isAlpha, SvgAffineViewportClip? region,
        SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        using SvgSceneBuilder builder = new(state.Budget, work, token);
        for (int childIndex = range.FirstChild; childIndex < range.FirstChild + range.ChildCount; childIndex++)
        {
            work.Consume(token);
            SvgMaskReplayChild child = state.MaskReplayChildren[childIndex];
            SvgPaintableElementContext context = child.Context with { InheritedPresentation = child.Context.InheritedPresentation with { Transform = placement.ComposeChecked(child.Context.InheritedPresentation.Transform) } };
            if (child.GroupContent is {} group)
            {
                SvgUseReference reference = new(string.Empty, 0, 0, null, null,
                    context.InheritedPresentation.Transform, SvgAffineTransform.Identity, 255,
                    context.InheritedPresentation.IsDisplayed && context.InheritedPresentation.IsVisible, null, null, null)
                {
                    InheritedFill = context.InheritedPresentation.Paint,
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
                SvgGeometry[] mapped = SvgReferenceResolver.Apply(new([], false, null, null) { SymbolContent = group }, reference, null, state, work, token);
                SvgPaintRecording.AdoptLeafScene(new(mapped, false, null, null), builder, state);
            }
            else if (child.Source.LocalName == "use")
            {
                SvgResourceUse reference = SvgResourceUseReplay.Retain(child.Source, context, state.MaskReplayBases, state);
                SvgResourceUseReplay.AppendMaskUse(reference, builder, state, work, token);
            }
            else
            {
                SvgDefinition lowered = SvgDefinitionParser.LowerInstanceSource(child.Source, context, state);
                SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
            }
        }
        if (region is {} clip)
        {
            (SvgClipBox bounds, int index) = clip.Register(state, builder, work, token);
            builder.InsertCommands(0, [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, bounds, index, 255) { RegionKind = SvgLayerRegionKind.AffineViewport }]);
            builder.EndOpacityScope();
        }
        SvgLeafMaskComposition.Emit(state, builder, work, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        return new([], 255, isAlpha) { VectorContent = content };
    }

    /// <summary>Places one borrowed mask through existing source replay when an affine map requires native reconstruction.</summary>
    /// <remarks>Only preparation borrows source slots. Returned scenes own independent geometry and no staging
    /// reference; partial lowering, clips and scene owners release on failure under shared work/cancellation.</remarks>
    internal static SvgMaskContent PlaceForTarget(SvgMaskContent source, SvgAffineTransform map, SvgRectangle bounds,
        SvgMaskTarget? target, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
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
        if (map.IsAxisAligned) return source with { CoordinateMap = map, ReplaySource = null };
        SvgMaskSourceRange range = source.ReplaySource ?? throw new SvgException(SvgErrorCode.UnsupportedFeature, "Affine mask content requires retained native sources.");
        SvgAffineViewportClip? region = target is {} clipped ? new(clipped.Region, clipped.Map) { IsBinary = true } : null;
        return Build(range, map, source.IsAlpha, region, state, work, token) with { UsesAffineTarget = true };
    }

    /// <summary>Materializes one uniform rectangle's native spatial paint when its scalar value cannot cover the target.</summary>
    /// <remarks>Source attributes and context remain borrowed from document staging. Identity replay caches one immutable
    /// scene; mapped replay owns a distinct scene. Both adopt through the existing parent owner, reserve material storage,
    /// charge lowering/copy work and observe cancellation. Returned content never retains a staging source reference.</remarks>
    internal static SvgMaskContent ResolveUniformSource(string id, SvgAffineTransform placement, SvgDocumentState state,
        ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
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
        using SvgSceneBuilder builder = new(state.Budget, work, token);
        SvgDefinition lowered = SvgDefinitionParser.LowerInstanceSource(child.Source, context, state);
        SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
        SvgLeafMaskComposition.Emit(state, builder, work, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        if (placement == SvgAffineTransform.Identity) state.MaskContents.Replace(id, mask with { VectorContent = content });
        return new([], 255, mask.IsAlpha) { VectorContent = content };
    }
}
