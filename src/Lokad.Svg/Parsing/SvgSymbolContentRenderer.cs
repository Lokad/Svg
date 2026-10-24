using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Expands retained symbol scopes through the existing instance geometry and recorded-paint machinery.</summary>
internal static class SvgSymbolContentRenderer
{
    private readonly record struct Context(SvgPaint Paint, string? PaintServerId, string? PaintServerFill, SvgPaintOpacity StrokeOpacity, SvgInheritedStrokeStyle Stroke, string? FillRule, SvgAffineTransform Transform, SvgClipBox? Clip, bool Displayed, bool Visible)
    { internal bool HasInverseStrokePose { get; init; } internal SvgViewportLengthBases LengthBases { get; init; } internal SvgPaintOpacity? OwnOpacity { get; init; } internal SvgDisplay ElementDisplay { get; init; } internal SvgTextMetricScope TextMetrics { get; init; } internal SvgPaintOpacity ElementOpacity => OwnOpacity ?? SvgPaintOpacity.Opaque; internal SvgPaintOrder PaintOrder { get; init; } internal SvgMarkerStyle Markers { get; init; } internal SvgInheritedTextStyle TextStyle { get; init; } = SvgInheritedTextStyle.Default; }
    private readonly record struct Frame(SvgSymbolContentRange Range, int Next, Context Context, int OutputStart, SvgPaintOpacity Opacity, SvgRecordingScope Scope, SvgClipBox? CompositionClip, SvgAffineViewportClip? AffineClip, string? MaskReference)
    { internal SvgRetainedFilter? Filter { get; init; } internal SvgNativeClipReference? ResourceClip { get; init; } internal SvgAffineTransform MaskMap { get; init; } = SvgAffineTransform.Identity; }

    internal static SvgGeometry[] Apply(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? viewport, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        ValidateRenderedGroup(definition);
        SvgSymbolContentRange root = definition.SymbolContent ?? throw new InvalidOperationException("Retained content requires a source range.");
        using BudgetedParserList<SvgGeometry> output = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        using BudgetedParserList<Frame> stack = new(state.Budget, SvgMemoryCategory.Parser, work, token);
        Context context = Place(definition, reference, viewport, reference.IsDisplayed, reference.IsVisible, out SvgAffineViewportClip? affineClip);
        int previousFilterDepth = state.FilterGroupDepth;
        try
        {
            if (reference.IsDisplayed && reference.Filter is {} ownFilter && ownFilter.Trim() != "none") state.FilterGroupDepth++;
            stack.Add(new(root, root.Start, context, 0, SvgPaintOpacity.Opaque, state.NewRecordingScope(), null, affineClip, null));
            while (stack.Count > 0)
            {
                work.Consume(token);
                Frame frame = stack[^1];
                if (frame.Next == frame.Range.Start + frame.Range.Count)
                {
                    if (frame.Filter is {} filter)
                    {
                        if (output.Count == frame.OutputStart)
                        {
                            // Retained effects attach to a geometry batch. A non-painting
                            // empty target carries the scope through the existing emitter.
                            output.Add(SvgPaintRecording.CreateEffectAnchor(state.Scene, state, frame.Scope));
                        }
                        SvgPaintRecording.WrapFilter(output.AsMutableSpan().Slice(frame.OutputStart), filter, state, work, token);
                        state.FilterGroupDepth--;
                    }
                    if (frame.Context.Displayed)
                        SvgReferenceResolver.ApplyMask(output.AsMutableSpan().Slice(frame.OutputStart), null, null, frame.MaskReference, frame.MaskMap, state, work, token);
                    Span<SvgGeometry> contents = output.AsMutableSpan().Slice(frame.OutputStart);
                    if (frame.AffineClip is not null && frame.Opacity.Value != 1)
                    {
                        // Instance opacity surrounds the instantiated viewport. Keep its
                        // clip inside that layer so nested isolation restores coverage
                        // before the outer opacity's separate byte scaling.
                        SvgPaintRecording.WrapInstance(contents, SvgPaintOpacity.Opaque, frame.Scope, null, frame.AffineClip, state, work, token);
                        SvgPaintRecording.WrapInstance(contents, frame.Opacity, frame.Scope, state, work, token);
                    }
                    else SvgPaintRecording.WrapInstance(contents, frame.Opacity, frame.Scope, frame.CompositionClip, frame.AffineClip, state, work, token);
                    // A resource clip encloses group isolation. Children render
                    // into the layer before its opacity and AA clip restore once.
                    if (frame.ResourceClip is {} resourceClip)
                        SvgClipPathSyntax.ApplyResourceClip(contents, resourceClip.Id, resourceClip.Map, state, work, token);
                    stack.RemoveLast(); continue;
                }
                SvgSymbolContentNode node = frame.Range.Content.Nodes[frame.Next];
                stack[^1] = frame with { Next = frame.Next + 1 };
                if (node.Kind == SvgSymbolContentKind.Group)
                {
                    SvgInheritedTextStyle textStyle = ResolveTextStyle(node.Style.TextStyleSource, frame.Context);
                    SvgPaintOpacity opacity = node.Style.ResolveOpacity(frame.Context.ElementOpacity);
                    Context child = frame.Context with { TextMetrics = frame.Context.TextMetrics.Enter(node.Style.Filter, state.PaintResources), OwnOpacity = opacity, ElementDisplay = node.Style.ResolveDisplay(frame.Context.ElementDisplay), Markers = node.Style.Markers.InheritFrom(frame.Context.Markers), PaintOrder = node.Style.PaintOrder ?? frame.Context.PaintOrder, Paint = node.Style.Resolve(frame.Context.Paint), PaintServerId = node.Style.HasAuthoredFill ? node.Style.PaintServerId : frame.Context.PaintServerId, PaintServerFill = node.Style.HasAuthoredFill ? node.Style.PaintServerFill : frame.Context.PaintServerFill, StrokeOpacity = node.Style.StrokeOpacity ?? frame.Context.StrokeOpacity, Stroke = node.Style.Stroke.InheritFrom(frame.Context.Stroke), FillRule = node.Style.FillRule ?? frame.Context.FillRule, Transform = frame.Context.HasInverseStrokePose ? frame.Context.Transform.Compose(node.Transform) : frame.Context.Transform.ComposeChecked(node.Transform), Displayed = frame.Context.Displayed && node.Style.IsDisplayed && (node.Style.ElementDisplay != SvgDisplay.Inherit || frame.Context.ElementDisplay != SvgDisplay.None), Visible = node.Style.IsVisible ?? frame.Context.Visible };
                    child = child with { TextStyle = textStyle };
                    stack[^1] = frame with { Next = node.End + 1 };
                    SvgSymbolContentRange contents = new(frame.Range.Content, frame.Next + 1, node.End - frame.Next - 1);
                    SvgRetainedFilter? filter = child.Displayed ? SvgRetainedFilter.Create(node.Style.Filter, child.Transform, child.LengthBases, state) : null;
                    if (filter is not null) state.FilterGroupDepth++;
                    SvgClipBox? compositionClip = opacity.Value != 1 || filter is not null ? child.Clip : null;
                    // An inherited viewport clip surrounds the isolated container. Apply it
                    // once when restoring the group; children retain only their local clips.
                    if (compositionClip is not null) child = child with { Clip = null };
                    stack.Add(new(contents, contents.Start, child, output.Count, opacity, state.NewRecordingScope(), compositionClip, null, null)
                        { Filter = filter, ResourceClip = node.Style.ClipId is {} clipId ? new(clipId, child.Transform) : null });
                    continue;
                }
                if (node.Kind == SvgSymbolContentKind.Unsupported) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Styled group use instances with text, images, viewports, clips or masks are not implemented.");
                if (node.Kind == SvgSymbolContentKind.EndGroup) throw new InvalidOperationException("Symbol range includes an unmatched end marker.");
                bool displayed = frame.Context.Displayed && node.Style.IsDisplayed && (node.Style.ElementDisplay != SvgDisplay.Inherit || frame.Context.ElementDisplay != SvgDisplay.None);
                bool visible = node.Style.IsVisible ?? frame.Context.Visible;
                if (node.Kind == SvgSymbolContentKind.Leaf)
                {
                    SvgUseReference leaf = new(string.Empty, 0, 0, null, null, frame.Context.Transform, SvgAffineTransform.Identity, 255, displayed && visible, null, null, null)
                    { HasInverseStrokePose = frame.Context.HasInverseStrokePose, InheritedTextStyle = frame.Context.TextStyle, LengthBases = frame.Context.LengthBases, TextMetrics = frame.Context.TextMetrics, ParentElementOpacity = frame.Context.ElementOpacity, ElementParentDisplay = frame.Context.ElementDisplay, InheritedMarkers = frame.Context.Markers, InheritedPaintOrder = frame.Context.PaintOrder, InheritedFill = frame.Context.Paint, PaintServerId = frame.Context.PaintServerId, PaintServerFill = frame.Context.PaintServerFill, InheritedStrokeOpacity = frame.Context.StrokeOpacity, InheritedStroke = frame.Context.Stroke, InheritedFillRule = frame.Context.FillRule, RecordingScope = state.NewRecordingScope(), AllowsSolidPaintAnalysis = true, IsDisplayed = displayed, IsVisible = visible };
                    AddMapped(node.Definition, leaf, frame.Context.Clip);
                    continue;
                }
                if (!SvgReferenceResolver.TryGetDefinition(node.Reference, state.Definitions, out SvgDefinition target) && !state.RenderedGroupDefinitions.TryGetValue(node.Reference.Id, out target))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local references in symbol content are implemented.");
                SvgPaint instancePaint = node.Style.Resolve(frame.Context.Paint);
                SvgPaintOpacity useOpacity = node.Style.ResolveOpacity(frame.Context.ElementOpacity);
                SvgUseReference use = node.Reference with { HasInverseStrokePose = frame.Context.HasInverseStrokePose, InheritedTextStyle = ResolveTextStyle(node.Style.TextStyleSource, frame.Context), LengthBases = frame.Context.LengthBases, TextMetrics = frame.Context.TextMetrics.Enter(node.Reference.Filter, state.PaintResources), SourceOpacity = useOpacity, ParentElementOpacity = null, ElementParentDisplay = node.Style.ResolveDisplay(frame.Context.ElementDisplay), Transform = frame.Context.HasInverseStrokePose ? frame.Context.Transform.Compose(node.Reference.Transform) : frame.Context.Transform.ComposeChecked(node.Reference.Transform), InheritedTransform = frame.Context.Transform, InheritedMarkers = node.Style.Markers.InheritFrom(frame.Context.Markers), InheritedPaintOrder = node.Style.PaintOrder ?? frame.Context.PaintOrder, InheritedFill = instancePaint, PaintServerId = node.Reference.PaintServerId ?? (node.Style.HasAuthoredFill ? null : frame.Context.PaintServerId), PaintServerFill = node.Reference.PaintServerFill ?? (node.Style.HasAuthoredFill ? null : frame.Context.PaintServerFill), InheritedStrokeOpacity = node.Style.StrokeOpacity ?? frame.Context.StrokeOpacity, InheritedStroke = node.Style.Stroke.InheritFrom(frame.Context.Stroke), InheritedFillRule = node.Reference.InheritedFillRule ?? frame.Context.FillRule, FillOverride = node.Reference.FillOverride is null ? null : instancePaint, Opacity = 255, IsPainted = displayed && visible, IsDisplayed = displayed, IsVisible = visible, RecordingScope = state.NewRecordingScope() };
                if (!target.HasEmptyViewport(use) && target.SymbolContent is {} range)
                {
                    ValidateRenderedGroup(target);
                    Context child = Place(target, use, ClipFrame(frame.Context.Clip), displayed, visible, out SvgAffineViewportClip? nestedClip);
                    SvgRetainedFilter? filter = child.Displayed ? SvgRetainedFilter.Create(use.Filter, SvgReferenceResolver.PlainInstancePlacement(use, null), use.LengthBases, state) : null;
                    if (filter is not null) state.FilterGroupDepth++;
                    stack.Add(new(range, range.Start, child, output.Count, useOpacity, use.RecordingScope, null, nestedClip, node.Reference.SourceMaskReference)
                        { Filter = filter, MaskMap = SvgReferenceResolver.PlainInstancePlacement(use, null), ResourceClip = node.Style.ClipId is {} clipId ? new(clipId, SvgReferenceResolver.PlainInstancePlacement(use, null)) : null });
                }
                else
                {
                    int start = output.Count;
                    AddMapped(target with { SymbolFillBindings = target.SourceFillBindings ?? target.SymbolFillBindings }, use, frame.Context.Clip);
                    if (use.IsDisplayed)
                        SvgPaintRecording.WrapFilter(output.AsMutableSpan().Slice(start), use.Filter, SvgReferenceResolver.PlainInstancePlacement(use, null), use.LengthBases, state, work, token);
                    if (use.IsDisplayed)
                        SvgReferenceResolver.ApplyMask(output.AsMutableSpan().Slice(start), null, null, node.Reference.SourceMaskReference,
                            SvgReferenceResolver.PlainInstancePlacement(use, null), state, work, token);
                    if (node.Style.ClipId is {} clipId)
                        SvgClipPathSyntax.ApplyResourceClip(output.AsMutableSpan().Slice(start), clipId, SvgReferenceResolver.PlainInstancePlacement(use, null), state, work, token);
                    SvgPaintRecording.WrapInstance(output.AsMutableSpan().Slice(start), useOpacity, use.RecordingScope, state, work, token);
                }
            }
            SvgGeometry[] result = SvgReferenceResolver.RetainInstanceBatch(output.AsSpan(), state, work, token);
            output.Clear();
            return result;
        }
        catch { foreach (SvgGeometry geometry in output.AsSpan()) geometry.Dispose(); throw; }
        finally { state.FilterGroupDepth = previousFilterDepth; }

        void ValidateRenderedGroup(SvgDefinition source)
        {
            if (source.RenderedGroupId is null) return;
            if (!state.GroupSnapshots.TryGetValue(source.RenderedGroupId, out SvgGroupSnapshot snapshot)) return;
            if (snapshot.IsolatesOpacity) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group use instances with group opacity are not implemented.");
            if (snapshot.HasClip || snapshot.HasMask) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group use instances with group clip or mask scopes are not implemented.");
            if (snapshot.HasViewport) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Group use instances with viewport-scoped source groups are not implemented.");
        }

        SvgInheritedTextStyle ResolveTextStyle(SvgRetainedTextSource? source, Context inheritedContext)
        {
            if (source is null) return inheritedContext.TextStyle;
            using SvgRetainedTextSource.Reader attributes = source.OpenReader(0, work, token); attributes.MoveToContent();
            InlineStyle inline = SvgInlineStyleParser.Parse(attributes.GetAttribute("style"), work, token, true);
            InlineStyle sheet = attributes.StyleSheet;
            SvgComputedStyle computed = SvgComputedStyle.Resolve(attributes, inline, sheet);
            SvgInheritedPresentation inherited = new(inheritedContext.Paint, inheritedContext.Displayed, inheritedContext.Visible, SvgAffineTransform.Identity)
                { TextMetrics = inheritedContext.TextMetrics, OwnOpacity = inheritedContext.ElementOpacity, ElementDisplay = inheritedContext.ElementDisplay, PaintOrder = inheritedContext.PaintOrder, Markers = inheritedContext.Markers, PaintServerFill = inheritedContext.PaintServerFill };
            return SvgContainerInheritance.Resolve(attributes, inline, computed, sheet, inherited, inheritedContext.FillRule,
                inheritedContext.TextStyle, inheritedContext.Stroke, SvgRootParser.CssNamedColors, state.PaintResources, work, token).TextStyle;
        }

        void AddMapped(SvgDefinition source, SvgUseReference use, SvgClipBox? clip)
        {
            SvgGeometry[] mapped = SvgReferenceResolver.ApplyFilterSource(source, use, ClipFrame(clip), state, work, token);
            int transferred = 0;
            try { for (; transferred < mapped.Length; transferred++) output.Add(mapped[transferred]); }
            catch { for (int index = transferred; index < mapped.Length; index++) mapped[index].Dispose(); throw; }
        }
    }

    private static SvgViewportFrame? ClipFrame(SvgClipBox? clip)
        => clip is {} value ? new(0, 0, 1, 1, null, null, value, -1) : null;

    private static Context Place(SvgDefinition definition, SvgUseReference reference, SvgViewportFrame? frame, bool displayed, bool visible, out SvgAffineViewportClip? affineClip)
    {
        affineClip = null;
        SvgAffineTransform map = definition.SymbolViewBox is {} box ? SvgReferenceResolver.SymbolInstancePlacement(box, reference, frame) : SvgReferenceResolver.PlainInstancePlacement(reference, frame);
        SvgClipBox? clip = frame?.Clip;
        if ((definition.EstablishesViewport || definition.SymbolViewBox is not null || reference.IsMarker) && (!reference.IsMarker || definition.Marker is { ClipsViewport: true }))
        {
            if (reference.Width is null || reference.Height is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Symbol placement requires known instance dimensions.");
            SvgClipBox sourceViewport = new(reference.X, reference.Y, reference.Width.Value, reference.Height.Value);
            if (!reference.IsMarker || !reference.Transform.IsAxisAligned)
            {
                SvgAffineTransform clipMap = reference.Transform;
                if (frame is {} parent) clipMap = SvgAffineTransform.FromScaleOffset(parent.ScaleX, parent.ScaleY, parent.OffsetX, parent.OffsetY).ComposeChecked(clipMap);
                affineClip = new(sourceViewport, clipMap) { IsBinary = reference.IsMarker };
            }
            else
            {
                SvgClipBox local = reference.Transform.Apply(sourceViewport);
                if (frame is {} viewport) local = new(local.X * viewport.ScaleX + viewport.OffsetX, local.Y * viewport.ScaleY + viewport.OffsetY, local.Width * viewport.ScaleX, local.Height * viewport.ScaleY);
                if (reference.IsMarker) local = local with { Antialiasing = SvgClipAntialiasing.Binary };
                clip = clip is {} parent ? local.Intersect(parent) : local;
            }
        }
        return new(reference.FillOverride ?? reference.InheritedFill, reference.PaintServerId, reference.PaintServerFill, reference.InheritedStrokeOpacity, reference.InheritedStroke, reference.InheritedFillRule, map, clip, displayed, visible) { HasInverseStrokePose = reference.HasInverseStrokePose, TextStyle = reference.InheritedTextStyle, LengthBases = definition.InstanceLengthBases(reference), TextMetrics = reference.TextMetrics, OwnOpacity = reference.ElementParentOpacity, ElementDisplay = reference.ElementParentDisplay, Markers = reference.InheritedMarkers, PaintOrder = reference.InheritedPaintOrder };
    }
}
