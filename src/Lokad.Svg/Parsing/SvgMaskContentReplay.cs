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
        if (reader.LocalName != "use" && reader.GetAttribute("clip-path") is not null)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clip paths on mask content are not implemented.");
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, false, root, state, colors, work, token);
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
        if (!state.MaskObjectBoundingBoxContent && !group && state.MaskReplayChildCount == 1 && (reader.LocalName is "use" or "rect" or "circle"
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
        for (int index = 0; index < state.DeferredMaskContents.Count; index++)
        {
            work.Consume(token);
            SvgDeferredMaskContent pending = state.DeferredMaskContents[index];
            using SvgSceneBuilder builder = new(state.Budget, work, token);
            for (int childIndex = pending.FirstChild; childIndex < pending.FirstChild + pending.ChildCount; childIndex++)
            {
                work.Consume(token);
                SvgMaskReplayChild child = state.MaskReplayChildren[childIndex];
                if (child.GroupContent is {} group)
                {
                    SvgPaintableElementContext context = child.Context;
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
                    SvgResourceUse reference = SvgResourceUseReplay.Retain(child.Source, child.Context, root, state);
                    SvgResourceUseReplay.AppendMaskUse(reference, builder, state, work, token);
                }
                else
                {
                    SvgDefinition lowered = SvgDefinitionParser.LowerInstanceSource(child.Source, child.Context, state);
                    SvgPaintRecording.AdoptLeafScene(lowered, builder, state);
                }
            }
            SvgLeafMaskComposition.Emit(state, builder, work, token);
            SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
            try { state.Scene.AdoptVectorContent(content); }
            catch { content.Dispose(); throw; }
            state.MaskContents.Replace(pending.Id, new([], 255, pending.IsAlpha) { VectorContent = content, IsObjectBoundingBox = pending.IsObjectBoundingBox });
        }
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
