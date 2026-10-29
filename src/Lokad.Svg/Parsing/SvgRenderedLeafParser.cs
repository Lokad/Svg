using System.Xml;
using Lokad.Svg.Fonts;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Routes supported rendered leaves through their bounded lowerers outside XML traversal.</summary>
internal static class SvgRenderedLeafParser
{
    internal static bool TryParse(XmlReader reader, bool isEligible, SvgRootAttributes root, SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, IReadOnlyDictionary<string, SvgColor> namedColors, SvgFontSnapshot? fontSnapshot, CancellationToken cancellationToken)
    {
        if (!isEligible || reader.NamespaceURI != "http://www.w3.org/2000/svg") return false;
        if (state.DefinitionsDepth >= 0) return false;
        if (!ValidateLeafAttributes(reader, cancellationToken)) return false;
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, namedColors, cancellationToken).PrepareStyle(reader);

        string? textId = reader.LocalName == "text" ? reader.GetAttribute("id") : null;
        if (reader.LocalName == "text" && (state.RenderedGroupContent is not null || textId is not null))
        {
            if (textId is not null && (state.Definitions.TryGetValue(textId, out _) || state.GroupSnapshots.TryGetValue(textId, out _) || state.RenderedGroupDefinitions.TryGetValue(textId, out _)))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG definition ids must be unique.");
            int sourceDepth = reader.Depth;
            SvgComputedStyle style = context.AuthoredStyle ?? throw new InvalidOperationException("A rendered leaf requires prepared declarations.");
            bool displayed = style.GetSourceDisplay() != SvgDisplay.None;
            bool? visible = style.Visibility is null ? null : SvgPresentationResolver.ParseVisibility(style.Visibility, true);
            SvgRetainedTextSource source = SvgRetainedTextSource.Capture(reader, context, state, limits);
            SvgDefinition definition = new([], false, null, null) { TextSource = source, SourceDisplayed = displayed, SourceDisplay = style.GetSourceDisplay(), SourceVisible = visible };
            state.RenderedGroupContent?.AddLeaf(definition, displayed, visible);
            if (textId is not null) state.Definitions.TryAdd(state.Strings.Copy(textId), definition);
            using SvgRetainedTextSource.Reader replay = source.OpenReader(sourceDepth, cancellationToken);
            replay.MoveToContent();
            return ParseResolved(replay, context, root, state, limits, budget, fontSnapshot, cancellationToken);
        }
        SvgRenderedGroupSourceParser.CaptureLeaf(reader, context, state);
        if (SvgDeferredPaintLeaves.TryRetain(reader, context, state)) return true;
        return ParseResolved(reader, context, root, state, limits, budget, fontSnapshot, cancellationToken);
    }

    /// <summary>Applies the same bounded attribute policy to rendered and definition-content leaves.</summary>
    internal static bool ValidateLeafAttributes(XmlReader reader, CancellationToken cancellationToken)
    {
        switch (reader.LocalName)
        {
            case "rect":
                SvgElementAttributePolicy.ValidateRectangle(reader, cancellationToken);
                break;
            case "line":
                SvgElementAttributePolicy.ValidateLine(reader, cancellationToken);
                break;
            case "path":
                SvgElementAttributePolicy.ValidatePath(reader, cancellationToken);
                break;
            case "polygon" or "polyline":
                SvgElementAttributePolicy.ValidatePointList(reader, reader.LocalName, cancellationToken);
                break;
            case "circle" or "ellipse":
                SvgElementAttributePolicy.ValidateCircle(reader, reader.LocalName, cancellationToken);
                break;
            case "text":
                SvgElementAttributePolicy.ValidateText(reader, cancellationToken);
                break;
            case "image":
                SvgElementAttributePolicy.ValidateImage(reader, cancellationToken);
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>Lowers one validated live or retained leaf with its original inherited context.</summary>
    internal static bool ParseResolved(XmlReader reader, SvgPaintableElementContext context, SvgRootAttributes root,
        SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, SvgFontSnapshot? fontSnapshot,
CancellationToken cancellationToken)
    {
        SvgSourceContext source = SvgSourceContext.Capture(reader);
        try { return ParseResolvedCore(reader, context, root, state, limits, budget, fontSnapshot, cancellationToken); }
        catch (SvgException exception) { exception.AttachSource(source.Describe(string.Empty, string.Empty)); throw; }
    }

    private static bool ParseResolvedCore(XmlReader reader, SvgPaintableElementContext context, SvgRootAttributes root,
        SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, SvgFontSnapshot? fontSnapshot,
CancellationToken cancellationToken)
    {
        context = context.PrepareStyle(reader);
        SvgComputedStyle style = context.ResolveStyle(reader, reader.LocalName == "text");
        context = context with { TextMetrics = context.InheritedPresentation.TextMetrics.Enter(style.Filter, state.PaintResources) };
        if (style.Filter is not string filter || filter.Trim() == "none")
            return ParseContent(reader, context, root, state, limits, budget, fontSnapshot, cancellationToken);
        if (!SvgLocalUrl.TryParseId(filter, out string id)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only local filter url references are implemented.");
        if (!context.InheritedPresentation.IsDisplayed || style.GetDisplay() == SvgDisplay.None || !SvgPaintRecording.IsFilterSource(reader, context))
            return ParseContent(reader, context with { AuthoredStyle = style with { Filter = null } }, root,
                state, limits, budget, fontSnapshot, cancellationToken);
        int start = state.Scene.CommandCount, firstGeometry = state.Scene.GeometryCount;
        SvgPendingCommandStart deferredStart = state.PendingCommandEnd;
        string? clip = SvgClipPathSyntax.ResolveClipId(reader, state);
        string? mask = SvgLocalUrl.ReadReference(reader, "mask") is {} maskReference ? state.Strings.Copy(SvgMaskSyntax.ParseMaskId(maskReference)) : null;
        SvgViewportFrame? parent = state.EffectiveFrame;
        SvgAffineTransform transform = context.ResolveTransform(reader);
        SvgAffineTransform filterTransform = SvgViewportFrame.MapGeometry(transform, parent);
        SvgClipBox? viewportClip = parent is { HasAffineClipScope: false } viewport && viewport.Clip != SvgClipBox.Unbounded ? viewport.Clip : null;
        bool previousFilterClip = state.DeferringFilterClip;
        bool previousFilterMask = state.DeferringFilterMask;
        int previousViewportDepth = state.IsolatedViewportDepth;
        bool parsed;
        state.DeferringFilterClip = clip is not null;
        state.DeferringFilterMask = mask is not null;
        state.IsolatedViewportDepth = reader.Depth;
        state.FilterGroupDepth++;
        try
        {
            parsed = ParseContent(reader, context with { AuthoredStyle = style with { Filter = null, Opacity = null }, CanPaintEmptyNativeBounds = true }, root,
                state, limits, budget, fontSnapshot, cancellationToken);
        }
        finally
        {
            state.DeferringFilterClip = previousFilterClip; state.DeferringFilterMask = previousFilterMask;
            state.IsolatedViewportDepth = previousViewportDepth; state.FilterGroupDepth--;
        }
        SvgPendingCommandStart deferredEnd = state.PendingCommandEnd;
        bool deferredPaint = deferredEnd.Text != deferredStart.Text || deferredEnd.Paint != deferredStart.Paint;
        if (state.Scene.GeometryCount == firstGeometry && !deferredPaint) return parsed;
        SvgPaintBounds? sourceObjects = null, sourceVisuals = null;
        for (int index = firstGeometry; index < state.Scene.GeometryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SvgRecordedPaintValue paint = state.Scene.GetRecordedPaintValue(state.Scene.GeometryAt(index).RecordedPaint);
            if (paint.ObjectBounds is not {} box) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Filter source requires retained object bounds.");
            if (!paint.IsMarkerContent) sourceObjects = Union(sourceObjects, box);
            if (!paint.IsEffectAnchor) sourceVisuals = Union(sourceVisuals, paint.VisualBounds ?? box);
        }
        SvgFilterArena filters = state.Scene.Filters;
        if (!ReferenceEquals(filters, state.DocumentFilters)) filters.BorrowResourcesFrom(state.DocumentFilters);
        SvgRecordingScope maskScope = mask is null ? default : state.NewRecordingScope();
        // The same element owns both boundaries. Emission places its output
        // clip outside the mask rather than between mask and filter.
        int filterScope = mask is not null && clip is not null ? maskScope.Value : 0;
        int binding = filters.AddBinding(id, sourceObjects ?? default, sourceVisuals ?? default, filterTransform, context.LengthBases, deferredPaint, clip);
        SvgDrawCommand begin = new(SvgDrawCommandKind.BeginGroup, filterScope, mask is null ? style.GetOpacity() : SvgPaintOpacity.Opaque, null, binding, 255) { RegionKind = SvgLayerRegionKind.Filter };
        state.Scene.InsertCommands(state.Scene.CommandCount, [new(SvgDrawCommandKind.EndGroup, 0, 255, null, -1, 255)]);
        state.Scene.InsertCommands(start, [begin]);
        int maskFirst = start, maskEnd = -1;
        if (mask is not null)
        {
            SvgDrawCommand outer = new(SvgDrawCommandKind.BeginGroup, maskScope.Value, style.GetOpacity(), null, -1, 255);
            state.Scene.InsertCommands(start, [outer]);
            maskEnd = state.Scene.CommandCount;
            state.Scene.InsertCommands(maskEnd, [new(SvgDrawCommandKind.EndGroup, 0, 255, null, -1, 255)]);
        }
        if (viewportClip is {} outputClip)
        {
            state.Scene.InsertCommands(start, [new SvgDrawCommand(SvgDrawCommandKind.BeginGroup, -1, 255, outputClip, -1, 255) { RegionKind = parent is { RecordingScope.Value: 0 } ? SvgLayerRegionKind.FilterOutputClip : SvgLayerRegionKind.Viewport }]);
            state.Scene.InsertCommands(state.Scene.CommandCount, [new(SvgDrawCommandKind.EndGroup, 0, 255, null, -1, 255)]);
            maskFirst++; maskEnd++;
        }
        if (deferredStart != deferredEnd)
        {
            // Source slots and source-local mask/clip ranges move inside the
            // new filter wrappers. Filter bounds resolve after queued emission.
            int prefix = 1 + (mask is null ? 0 : 1) + (viewportClip is null ? 0 : 1);
            MoveSourceSlots(deferredStart, deferredEnd, prefix);
        }
        if (mask is not null) state.PendingGroupMasks.Add(new(mask, maskFirst, maskEnd, 0, parent, transform, state.Scene, true));
        return parsed;

        // Rendered leaves cannot queue uses. Only the appended source-local
        // placeholders and clip/mask ranges move inside the inserted wrappers.
        void MoveSourceSlots(SvgPendingCommandStart first, SvgPendingCommandStart last, int prefix)
        {
            for (int index = first.Paint; index < last.Paint; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgDeferredPaintLeaf pending = state.DeferredPaintLeaves[index];
                if (ReferenceEquals(pending.Scene, state.Scene))
                    state.DeferredPaintLeaves[index] = pending with { CommandIndex = checked(pending.CommandIndex + prefix) };
            }
            for (int index = first.Text; index < last.Text; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgPendingTextRun pending = state.PendingTextRuns[index];
                if (ReferenceEquals(pending.Scene, state.Scene))
                    state.PendingTextRuns[index] = pending with { CommandIndex = checked(pending.CommandIndex + prefix) };
            }
            for (int index = first.Clip; index < last.Clip; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PendingGroupClip pending = state.PendingGroupClips[index];
                if (ReferenceEquals(pending.Scene, state.Scene))
                    state.PendingGroupClips[index] = pending with { FirstCommandIndex = checked(pending.FirstCommandIndex + prefix),
                        EndCommandIndex = pending.EndCommandIndex < 0 ? pending.EndCommandIndex : checked(pending.EndCommandIndex + prefix) };
            }
            for (int index = first.Mask; index < last.Mask; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PendingGroupMask pending = state.PendingGroupMasks[index];
                if (ReferenceEquals(pending.Scene, state.Scene))
                    state.PendingGroupMasks[index] = pending with { FirstCommandIndex = checked(pending.FirstCommandIndex + prefix),
                        EndCommandIndex = pending.EndCommandIndex < 0 ? pending.EndCommandIndex : checked(pending.EndCommandIndex + prefix) };
            }
        }

        static SvgPaintBounds Union(SvgPaintBounds? previous, SvgPaintBounds value)
            => previous is {} box ? new(MathF.Min(box.Left, value.Left), MathF.Min(box.Top, value.Top), MathF.Max(box.Right, value.Right), MathF.Max(box.Bottom, value.Bottom)) : value;
    }

    private static bool ParseContent(XmlReader reader, SvgPaintableElementContext context, SvgRootAttributes root,
        SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, SvgFontSnapshot? fontSnapshot,
CancellationToken cancellationToken)
    {
        if (reader.LocalName is not ("line" or "path" or "polygon" or "polyline") && SvgMarkerStyle.Resolve(context.ResolveStyle(reader, reader.LocalName == "text"), context.InheritedPresentation.Markers).HasMarkers)
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Markers on shapes other than lines and paths are not implemented.");
        switch (reader.LocalName)
        {
            case "rect":
                string? deferredRectClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredRectMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferRectClip = deferredRectClip is not null;
                int rectFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferRectClip;
                try
                {
                    using (SvgParsedGeometry rectangle = SvgLeafParser.ParseRectangle(reader, context, state.EffectiveFrame, state))
                    {
                        if (rectangle.Geometries.Length == 1)
                        {
                            SvgGeometry single = rectangle.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, rectangle.Storage.Items);
                            try { state.Scene.AddGeometries(rectangle.Geometries); }
                            catch { foreach (SvgGeometry geometry in rectangle.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferRectClip && deferredRectClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(rectFirstGeometry, state.Scene.GeometryCount - rectFirstGeometry, deferredRectClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredRectMask is not null) SvgLeafMaskComposition.Retain(state, rectFirstGeometry, state.Scene.GeometryCount - rectFirstGeometry, deferredRectMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, rectFirstGeometry, cancellationToken);
                return true;
            case "line":
                string? deferredLineClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredLineMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferLineClip = deferredLineClip is not null;
                int lineFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferLineClip;
                try
                {
                    using (SvgParsedGeometry line = SvgLeafParser.ParseLine(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                    {
                        if (line.Geometries.Length == 1)
                        {
                            SvgGeometry single = line.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, line.Storage.Items);
                            try { state.Scene.AddGeometries(line.Geometries); }
                            catch { foreach (SvgGeometry geometry in line.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferLineClip && deferredLineClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(lineFirstGeometry, state.Scene.GeometryCount - lineFirstGeometry, deferredLineClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredLineMask is not null) SvgLeafMaskComposition.Retain(state, lineFirstGeometry, state.Scene.GeometryCount - lineFirstGeometry, deferredLineMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, lineFirstGeometry, cancellationToken);
                return true;
            case "circle":
                string? deferredCircleClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredCircleMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferCircleClip = deferredCircleClip is not null;
                int circleFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferCircleClip;
                try
                {
                    using (SvgParsedGeometry circle = SvgLeafParser.ParseCircle(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                    {
                        if (circle.Geometries.Length == 1)
                        {
                            SvgGeometry single = circle.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, circle.Storage.Items);
                            try { state.Scene.AddGeometries(circle.Geometries); }
                            catch { foreach (SvgGeometry geometry in circle.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferCircleClip && deferredCircleClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(circleFirstGeometry, state.Scene.GeometryCount - circleFirstGeometry, deferredCircleClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredCircleMask is not null) SvgLeafMaskComposition.Retain(state, circleFirstGeometry, state.Scene.GeometryCount - circleFirstGeometry, deferredCircleMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, circleFirstGeometry, cancellationToken);
                return true;
            case "ellipse":
                string? deferredEllipseClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredEllipseMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferEllipseClip = deferredEllipseClip is not null;
                int ellipseFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferEllipseClip;
                try
                {
                    using (SvgParsedGeometry ellipse = SvgLeafParser.ParseEllipse(reader, context, SvgClipPathSyntax.ResolveLeafClip(reader, state), state))
                    {
                        if (ellipse.Geometries.Length == 1)
                        {
                            SvgGeometry single = ellipse.Geometries[0];
                            SvgMaskSyntax.ValidateMaskScopes(state, single);
                            state.Scene.AddGeometry(single);
                        }
                        else
                        {
                            SvgMaskSyntax.ValidateMaskScopeBands(state, ellipse.Storage.Items);
                            try { state.Scene.AddGeometries(ellipse.Geometries); }
                            catch { foreach (SvgGeometry geometry in ellipse.Geometries) geometry.Dispose(); throw; }
                        }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferEllipseClip && deferredEllipseClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(ellipseFirstGeometry, state.Scene.GeometryCount - ellipseFirstGeometry, deferredEllipseClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredEllipseMask is not null) SvgLeafMaskComposition.Retain(state, ellipseFirstGeometry, state.Scene.GeometryCount - ellipseFirstGeometry, deferredEllipseMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, ellipseFirstGeometry, cancellationToken);
                return true;
            case "path":
                string? deferredPathClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredPathMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferPathClip = deferredPathClip is not null;
                int pathFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferPathClip;
                try
                {
                    using (SvgParsedGeometry path = SvgOrthogonalShapeParser.ParsePath(reader, context, limits.MaximumPathCommands - state.GeometryElementCount, budget, ResolveShapeClipFrame(reader, "path", true, state, limits, budget, context), state))
                    {
                        state.GeometryElementCount += path.ElementCount;
                        SvgMaskSyntax.ValidateMaskScopeBands(state, path.Storage.Items);
                        try { state.Scene.AddGeometries(path.Geometries); }
                            catch { foreach (SvgGeometry geometry in path.Geometries) geometry.Dispose(); throw; }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferPathClip && deferredPathClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(pathFirstGeometry, state.Scene.GeometryCount - pathFirstGeometry, deferredPathClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredPathMask is not null) SvgLeafMaskComposition.Retain(state, pathFirstGeometry, state.Scene.GeometryCount - pathFirstGeometry, deferredPathMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, pathFirstGeometry, cancellationToken);
                return true;
            case "polygon" or "polyline":
                string? deferredPointListClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredPointListMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferPointListClip = deferredPointListClip is not null;
                int pointListFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferPointListClip;
                try
                {
                    using (SvgParsedGeometry pointList = SvgOrthogonalShapeParser.ParsePointList(reader, reader.LocalName, context, limits.MaximumPathCommands - state.GeometryElementCount, budget, ResolveShapeClipFrame(reader, reader.LocalName, false, state, limits, budget, context), state))
                    {
                        state.GeometryElementCount += pointList.ElementCount;
                        SvgMaskSyntax.ValidateMaskScopeBands(state, pointList.Storage.Items);
                        try { state.Scene.AddGeometries(pointList.Geometries); }
                            catch { foreach (SvgGeometry geometry in pointList.Geometries) geometry.Dispose(); throw; }
                    }
                }
                finally { state.DeferringLeafClip = false; }
                if (deferPointListClip && deferredPointListClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(pointListFirstGeometry, state.Scene.GeometryCount - pointListFirstGeometry, deferredPointListClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredPointListMask is not null) SvgLeafMaskComposition.Retain(state, pointListFirstGeometry, state.Scene.GeometryCount - pointListFirstGeometry, deferredPointListMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, pointListFirstGeometry, cancellationToken);
                return true;
            case "image":
            {
                string? deferredImageClip = SvgClipPathSyntax.PendingClipId(reader, state, context);
                string? deferredImageMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                bool deferImageClip = deferredImageClip is not null;
                int imageFirstGeometry = state.Scene.GeometryCount;
                state.DeferringLeafClip = deferImageClip;
                try
                {
                    SvgViewportFrame? imageFrame = SvgClipPathSyntax.ResolveLeafClip(reader, state);
                    SvgViewportFrame? imageParent = state.EffectiveFrame;
                    SvgGeometry image = SvgLeafParser.ParseImage(reader, context, imageFrame, imageParent?.ViewportWidth ?? root.UserViewportWidth, imageParent?.ViewportHeight ?? root.UserViewportHeight, limits, state);
                    try { SvgMaskSyntax.ValidateMaskScopes(state, image); }
                    catch { image.Dispose(); throw; }
                    state.Scene.AddGeometry(image);
                }
                finally { state.DeferringLeafClip = false; }
                if (deferImageClip && deferredImageClip is not null) state.PendingLeafClips.Add(new PendingLeafClip(imageFirstGeometry, state.Scene.GeometryCount - imageFirstGeometry, deferredImageClip, state.EffectiveFrame, context.ResolveTransform(reader), state.Scene));
                if (deferredImageMask is not null) SvgLeafMaskComposition.Retain(state, imageFirstGeometry, state.Scene.GeometryCount - imageFirstGeometry, deferredImageMask, state.EffectiveFrame, context.ResolveTransform(reader), cancellationToken);
                SvgPaintRecording.FinishScene(state, imageFirstGeometry, cancellationToken);
                return true;
            }
            case "text":
            {
                static bool HasServer(SvgTextGlyphPaint paint) => paint.FillServer is not null
                    || paint.HasStroke && paint.Stroke?.Style.Stroke is {} stroke && SvgLocalUrl.TryParseId(stroke, out _);

                // Fail fast with the conversion-level category: the operation is already rejected as unsupported, so a later font lookup must not surface UnresolvedResource instead.
                if (state.HasUnsupportedContent) throw new SvgException(SvgErrorCode.UnsupportedFeature, "This SVG child element is not implemented yet.");
                SvgResolvedDirectTextElement resolvedText = context.ResolveTextShape(reader);
                SvgResolvedPaintableElement textElement = resolvedText.PaintableElement;
                string? textClip = SvgClipPathSyntax.ResolveClipId(reader, state);
                if (textClip is not null) textClip = state.Strings.Copy(textClip);
                string? textMask = SvgMaskSyntax.PendingMaskId(reader, state, context);
                SvgViewportFrame? resourceParent = state.EffectiveFrame;
                int resourceStart = state.Scene.CommandCount;
                SvgPaintOpacity textOpacity = textElement.Style.GetOpacity();
                bool textScope = textClip is not null || textMask is not null || textOpacity.Value != 1;
                if (textScope)
                    state.Scene.BeginSourceOpacityScope(textOpacity, state.NewRecordingScope());
                SvgAffineTransform textPlacement = textElement.Transform;
                if (state.EffectiveFrame is {} textFrame)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    textPlacement = textFrame.MapText(textPlacement);
                }
                // ResolveTextShape validates fill-rule syntax; SVG text keeps glyph winding independent of that property.
                SvgTextStyle textStyle = SvgTextSyntax.Parse(reader, resolvedText.InlineFont, context.InheritedTextStyle, resolvedText.LetterSpacing, resolvedText.WordSpacing);
                SvgTextWhitespaceMode whitespaceMode = SvgTextSyntax.ParseWhitespaceMode(reader, resolvedText.WhiteSpace, context.InheritedTextStyle.WhitespaceMode);
                SvgTextPaintStyle textPaint = SvgTextPaintStyle.ResolveElement(textElement.Style, context.InheritedPresentation.Paint, context.InheritedPresentation.IsDisplayed, context.InheritedPresentation.IsVisible, context.InheritedStroke.Stroke);
                textPaint = textPaint with { TextOpacity = SvgPaintOpacity.Opaque, StrokeStyle = context.InheritedStroke.ResolveChild(textElement.Style), PaintOrder = textElement.Style.PaintOrder ?? context.InheritedPresentation.PaintOrder };
                textPaint = textPaint with { Decoration = SvgTextDecorationSyntax.Resolve(resolvedText.TextDecoration ?? reader.GetAttribute("text-decoration"), context.InheritedTextStyle.Decoration, (textPaint with { IsDisplayed = true, IsVisible = true }).GlyphPaint) };
                textPaint = textPaint.ResolveResources(context.TextMetrics.NeedsBounds ? null : state.PaintResources, context.TextMetrics.CanSuppress);
                using BudgetedParserList<SvgExtractedTextRun> textRuns = new(budget, SvgMemoryCategory.Parser, cancellationToken);
                try
                {
                    SvgInheritedTextStyle rootTextFont = new(textStyle.Font.Family, textStyle.Font.Weight, textStyle.Font.Italic, textStyle.FontSize, whitespaceMode, textStyle.LetterSpacing, textStyle.Anchor) { WordSpacing = textStyle.WordSpacing, Decoration = textPaint.Decoration };
                    ReadDirectTextContent(reader, state, whitespaceMode, textStyle.LetterSpacing, textPaint, rootTextFont, SvgTextLimits.MaximumCharacters - state.TextCharacterCount, limits.MaximumNodes - state.NodeCount, limits.MaximumXmlDepth, limits.MaximumAttributesPerElement, budget, cancellationToken, textRuns, out int sourceCharacterCount, out int childNodeCount);
                    state.TextCharacterCount += sourceCharacterCount;
                    state.NodeCount += childNodeCount;
                    bool sharedPathContext = false, hasOrdinaryContinuation = false, sharedPaint = false;
                    foreach (SvgExtractedTextRun source in textRuns.AsSpan())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sharedPathContext |= source.Path is not null || source.ResumeAfterPath is not null;
                        hasOrdinaryContinuation |= source.ResumeAfterPath is not null;
                        if (source.PaintSpans.Items.Length == 0) sharedPaint |= textPaint.GlyphPaint.IsPainted;
                        else foreach (SvgTextPaintSpan span in source.PaintSpans.Items) { cancellationToken.ThrowIfCancellationRequested(); sharedPaint |= span.Paint.IsPainted; }
                    }
                    if (sharedPathContext && (textRuns.Count > 1 || hasOrdinaryContinuation) && sharedPaint)
                    {
                        if (fontSnapshot is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG text requires a prepared font snapshot.");
                        double sharedMetricScale = state.OutputTextScale * SvgGlyphDeviceMetrics.GetScreenScale(textPlacement);
                        SvgGlyphDeviceMetrics? sharedMetrics = OperatingSystem.IsWindows() ? new(sharedMetricScale, SvgGlyphMetricTarget.Windows, false)
                            : OperatingSystem.IsLinux() ? new(sharedMetricScale, SvgGlyphMetricTarget.Linux, false) : null;
                        RenderBudget.BudgetReservation sharedOwner = budget.Reserve(512, SvgMemoryCategory.Parser);
                        OwnedParserArray<SvgExtractedTextRun> sharedSources = new([], null);
                        SvgPendingTextLayout? sharedLayout = null;
                        try
                        {
                            sharedSources = textRuns.Detach();
                            sharedLayout = new(sharedSources, textStyle, textPaint.GlyphPaint, sharedMetrics, fontSnapshot);
                            state.PendingTextRuns.Add(new(null, null, textPlacement, textElement.Presentation.Color, state.Scene.AddPaintPlaceholder(), sharedOwner, state.Scene)
                                { RecordingTransform = context.InheritedPresentation.RecordingTransform, ViewportClip = SvgViewportFrame.ResolveGlyphClip(state, cancellationToken), SharedLayout = sharedLayout, FilterGroupDepth = state.FilterGroupDepth });
                        }
                        catch
                        {
                            if (sharedLayout is not null) sharedLayout.Dispose();
                            else { foreach (SvgExtractedTextRun source in sharedSources.Items) source.Dispose(); sharedSources.Dispose(); }
                            sharedOwner.Dispose();
                            throw;
                        }
                    }
                    for (int textIndex = 0; textIndex < textRuns.Count; textIndex++)
                    {
                        SvgExtractedTextRun extracted = textRuns[textIndex];
                        BudgetedTextLayout text = extracted.Layout;
                        SvgTextPathReference? textPath = extracted.Path;
                        if (text.Value.Length == 0) continue;
                        bool hasFill = extracted.PaintSpans.Items.Length == 0 && textPaint.GlyphPaint.HasFill;
                        bool hasStroke = extracted.PaintSpans.Items.Length == 0 && textPaint.GlyphPaint.HasStroke;
                        bool hasDecoration = extracted.PaintSpans.Items.Length == 0 && textPaint.GlyphPaint.Decoration is not null;
                        bool hasServer = extracted.PaintSpans.Items.Length == 0 && HasServer(textPaint.GlyphPaint);
                        foreach (SvgTextPaintSpan span in extracted.PaintSpans.Items)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            hasFill |= span.Paint.HasFill;
                            hasStroke |= span.Paint.HasStroke;
                            hasDecoration |= span.Paint.Decoration is not null;
                            hasServer |= HasServer(span.Paint);
                        }
                        if (!hasFill && !hasStroke && !hasDecoration) continue;
                        if (fontSnapshot is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "SVG text requires a prepared font snapshot.");
                        SvgTextStyle runStyle = textPath is {} anchoredPath ? textStyle with { Y = 0, PositionLists = textStyle.PositionLists with { IgnoreY = true }, Anchor = anchoredPath.Anchor ?? textStyle.Anchor } : textStyle;
                        if (extracted.CharacterOffset != 0 || extracted.ResumeAfterPath is not null)
                            runStyle = runStyle with { X = 0, Y = 0, Dx = 0, Dy = 0,
                                PositionLists = runStyle.PositionLists with { CharacterOffset = extracted.CharacterOffset } };
                        double textMetricScale = state.OutputTextScale * SvgGlyphDeviceMetrics.GetScreenScale(textPlacement);
                        SvgGlyphDeviceMetrics? deviceMetrics = OperatingSystem.IsWindows() ? new(textMetricScale, SvgGlyphMetricTarget.Windows, textPath is not null)
                            : OperatingSystem.IsLinux() ? new(textMetricScale, SvgGlyphMetricTarget.Linux, textPath is not null) : null;
                        RenderBudget.BudgetReservation? pendingTextOwner = textPath is null && !hasServer ? null : budget.Reserve(256, SvgMemoryCategory.Parser);
                        SvgTextRun run;
                        try
                        {
                            run = SvgTextRunBuilder.Create(runStyle, text.Value, text.LetterSpacingSpans, text.PositionAdjustments, extracted.PaintSpans.Items, extracted.FontSpans.Items, extracted.NodeBoundaries.Items, textPaint.GlyphPaint, deviceMetrics, fontSnapshot, budget, cancellationToken);
                            try { run.RetainRecordingScopes(extracted.RecordingScopes.Items, extracted.ElementStroke, budget, cancellationToken); }
                            catch { run.Dispose(); throw; }
                        }
                        catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnresolvedResource && state.DeferredPaintLeaves.Count > 0)
                        {
                            pendingTextOwner?.Dispose();
                            // Content has been consumed and its layout will be disposed. Resolve earlier paint
                            // failures first, then propagate this same font failure if those paints are valid.
                            state.DeferredTextFailure ??= exception;
                            return true;
                        }
                        catch { pendingTextOwner?.Dispose(); throw; }
                        if (textPath is not null || hasServer)
                        {
                            try { state.PendingTextRuns.Add(new(run, textPath, textPlacement, textElement.Presentation.Color, state.Scene.AddPaintPlaceholder(), pendingTextOwner ?? throw new InvalidOperationException("A retained text run requires its metadata owner."), state.Scene) { RecordingTransform = context.InheritedPresentation.RecordingTransform, ViewportClip = SvgViewportFrame.ResolveGlyphClip(state, cancellationToken), FilterGroupDepth = state.FilterGroupDepth }); }
                            catch { pendingTextOwner?.Dispose(); run.Dispose(); throw; }
                            continue;
                        }
                        using (run)
                        {
                            SvgTextGlyphPainter.EmitDirect(run, textPlacement, null, textElement.Presentation.Color, state, limits.MaximumPathCommands, budget, context.InheritedPresentation.RecordingTransform, cancellationToken);
                        }
                    }
                    if (textScope)
                    {
                        // Deferred paths occupy paint slots inside the same element scope. Their
                        // resolution shifts these shared ranges before clip/mask binding.
                        state.Scene.EndOpacityScope();
                        if (textClip is not null)
                            state.PendingGroupClips.Add(new(textClip, resourceParent, textElement.Transform, resourceStart, state.Scene.CommandCount, -1, state.Scene) { UsesNativeClip = true });
                        if (textMask is not null)
                            state.PendingGroupMasks.Add(new(textMask, resourceStart, state.Scene.CommandCount, -1, resourceParent, textElement.Transform, state.Scene, false));
                    }
                    return true;
                }
                finally
                {
                    foreach (SvgExtractedTextRun extracted in textRuns.AsSpan()) extracted.Dispose();
                }
            }
        }
        return false;

        static SvgViewportFrame? ResolveShapeClipFrame(XmlReader reader, string elementName, bool isPath, SvgDocumentState state, SvgRenderLimits limits, RenderBudget budget, SvgPaintableElementContext context)
        {
            string? clipId = SvgClipPathSyntax.ResolveClipId(reader, state);
            if (clipId is null) return SvgClipPathSyntax.ResolveLeafClip(reader, state);
            if (!state.ObjectClips.TryGetValue(clipId, out _)) return SvgClipPathSyntax.ResolveLeafClip(reader, state);
            double minimumX;
            double minimumY;
            double maximumX;
            double maximumY;
            try
            {
                using BudgetedParserList<SvgPoint> points = TokenizeShapePoints(reader, elementName, isPath, limits.MaximumPathCommands - state.GeometryElementCount, budget, context);
                minimumX = points[0].X;
                minimumY = points[0].Y;
                maximumX = points[0].X;
                maximumY = points[0].Y;
                for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
                {
                    if ((pointIndex & 4095) == 0) context.CancellationToken.ThrowIfCancellationRequested();
                    SvgPoint vertex = points[pointIndex];
                    if (vertex.X < minimumX) minimumX = vertex.X;
                    if (vertex.Y < minimumY) minimumY = vertex.Y;
                    if (vertex.X > maximumX) maximumX = vertex.X;
                    if (vertex.Y > maximumY) maximumY = vertex.Y;
                }
            }
            catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnsupportedFeature)
            {
                (minimumX, minimumY, maximumX, maximumY) = FlattenedShapeBounds(reader, elementName, isPath, limits.MaximumPathCommands - state.GeometryElementCount, budget, context);
            }
            SvgAffineTransform transform = context.ResolveTransform(reader);
            SvgRectangle placement = transform.Apply(new SvgRectangle(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY, default, true, null));
            return SvgClipPathSyntax.ResolveLeafClipOrPlacement(reader, state, state.EffectiveFrame, placement);

            static (double MinimumX, double MinimumY, double MaximumX, double MaximumY) FlattenedShapeBounds(XmlReader shape, string name, bool pathData, int maximumGeometryElements, RenderBudget geometryBudget, SvgPaintableElementContext shapeContext)
            {
                if (pathData)
                {
                    string d = shape.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
                    int boundsCommands = 0;
                    bool boundsHasCurve = false;
                    for (int scanIndex = 0; scanIndex < d.Length; scanIndex++)
                    {
                        if ((scanIndex & 4095) == 0) shapeContext.CancellationToken.ThrowIfCancellationRequested();
                        char scanLetter = d[scanIndex];
                        if (scanLetter is 'A' or 'a' or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't') { boundsHasCurve = true; boundsCommands++; }
                        else if (scanLetter is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'Z' or 'z') boundsCommands++;
                    }

                    long wantedPoints = boundsHasCurve ? (long)boundsCommands * (SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand + 1) : (long)d.Length + boundsCommands + 1;
                    int pointCapacity = wantedPoints > SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints ? SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints : (int)wantedPoints;
                    if (pointCapacity < 1) pointCapacity = 1;
                    int contourCapacity = pointCapacity < maximumGeometryElements ? pointCapacity : maximumGeometryElements;
                    long scratchBytes;
                    try { scratchBytes = checked(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity) + 2 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)); }
                    catch (OverflowException) { throw new SvgException(SvgErrorCode.LimitExceeded, "Curve flattening exceeds the supported expansion range."); }
                    using RenderBudget.BudgetReservation scratch = geometryBudget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
                    SvgContourBuilder builder = new(pointCapacity, contourCapacity);
                    SvgPathDataParser.ParsePath(d, SvgAffineTransform.Identity, builder, new SvgFlattenPolicy(SvgOrthogonalGeometryLowering.ScaledFlatness(shapeContext.OutputMagnification), SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand), maximumGeometryElements, shapeContext.CancellationToken, null);
                    if (builder.ContourCount == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on curved or fractional contours are not implemented.");
                    return FlattenedContourBounds(builder, shapeContext.CancellationToken);
                }
                string pointsValue = shape.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, name + " points are required.");
                using BudgetedParserList<SvgFractionalPoint> fractional = SvgOrthogonalGeometryTokenizer.ParseFractionalPointList(pointsValue, name, maximumGeometryElements, geometryBudget, shapeContext.CancellationToken);
                if (fractional.Count == 0) throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on curved or fractional contours are not implemented.");
                double left = fractional[0].X;
                double top = fractional[0].Y;
                double right = fractional[0].X;
                double bottom = fractional[0].Y;
                for (int vertexIndex = 1; vertexIndex < fractional.Count; vertexIndex++)
                {
                    if ((vertexIndex & 4095) == 0) shapeContext.CancellationToken.ThrowIfCancellationRequested();
                    SvgFractionalPoint vertex = fractional[vertexIndex];
                    if (vertex.X < left) left = vertex.X;
                    if (vertex.Y < top) top = vertex.Y;
                    if (vertex.X > right) right = vertex.X;
                    if (vertex.Y > bottom) bottom = vertex.Y;
                }
                return (left, top, right, bottom);
            }

            static (double MinimumX, double MinimumY, double MaximumX, double MaximumY) FlattenedContourBounds(SvgContourBuilder builder, CancellationToken cancellationToken)
            {
                double left = double.PositiveInfinity;
                double top = double.PositiveInfinity;
                double right = double.NegativeInfinity;
                double bottom = double.NegativeInfinity;
                int vertexIndex = 0;
                for (int contour = 0; contour < builder.ContourCount; contour++)
                {
                    foreach (SvgDevicePoint vertex in builder.GetContour(contour))
                    {
                        if ((vertexIndex & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                        vertexIndex++;
                        if (vertex.X < left) left = vertex.X;
                        if (vertex.Y < top) top = vertex.Y;
                        if (vertex.X > right) right = vertex.X;
                        if (vertex.Y > bottom) bottom = vertex.Y;
                    }
                }
                return (left, top, right, bottom);
            }

            static BudgetedParserList<SvgPoint> TokenizeShapePoints(XmlReader shape, string name, bool pathData, int maximumGeometryElements, RenderBudget geometryBudget, SvgPaintableElementContext shapeContext)
            {
                try
                {
                    if (pathData)
                    {
                        string d = shape.GetAttribute("d") ?? throw new SvgException(SvgErrorCode.MalformedInput, "Path data is required.");
                        using BudgetedParserList<int> contourEnds = new(geometryBudget, SvgMemoryCategory.Geometry, shapeContext.CancellationToken);
                        return SvgOrthogonalGeometryTokenizer.ParsePath(d, maximumGeometryElements, geometryBudget, shapeContext.CancellationToken, contourEnds, false);
                    }
                    string pointsValue = shape.GetAttribute("points") ?? throw new SvgException(SvgErrorCode.MalformedInput, name + " points are required.");
                    return SvgOrthogonalGeometryTokenizer.ParsePointList(pointsValue, name, maximumGeometryElements, geometryBudget, shapeContext.CancellationToken, false);
                }
                catch (SvgException exception) when (exception.ErrorCode == SvgErrorCode.UnsupportedFeature)
                {
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "objectBoundingBox clip paths on curved or fractional contours are not implemented.");
                }
            }
        }

    }

    /// <summary>Extracts bounded normalized text and style intervals without preparing font programs.</summary>
    /// <remarks>Caller-owned output adopts layout arrays; every failure leaves them disposable. Shared
    /// cancellation and document limits apply. Inputs and immutable source styles remain borrowed.</remarks>
    internal static void ReadDirectTextContent(XmlReader element, SvgDocumentState state, SvgTextWhitespaceMode whitespaceMode, double letterSpacing, SvgTextPaintStyle paint, SvgInheritedTextStyle font, int maximumCharacters, int remainingNodeCount, int maximumXmlDepth, int maximumAttributesPerElement, RenderBudget budget, CancellationToken cancellationToken, BudgetedParserList<SvgExtractedTextRun> runs, out int sourceCharacterCount, out int childNodeCount)
    {
        SvgTextPathReference? textPath = null;
        SvgTextPathReference? previousPath = null;
        using var text = new SvgTextPaintOutput(budget, letterSpacing, paint, font, cancellationToken);
        var normalizer = new SvgTextWhitespaceNormalizer(whitespaceMode, letterSpacing, 0, false);
        if (element.IsEmptyElement)
        {
            sourceCharacterCount = normalizer.SourceCharacterCount;
            childNodeCount = 0;
            return;
        }
        childNodeCount = 0;
        sourceCharacterCount = 0;
        using var whitespaceNormalizers = new BudgetedParserList<SvgTextWhitespaceNormalizer>(budget, SvgMemoryCategory.Parser, cancellationToken);
        whitespaceNormalizers.Add(normalizer);
        using RenderBudget.BudgetReservation chunkReservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount(4_096, sizeof(char)), SvgMemoryCategory.Parser);
        char[] chunk = new char[4_096];
        int depthOffset = element.Depth;
        int descriptionDepth = -1;
        using XmlReader subtree = element.ReadSubtree();
        while (subtree.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.CssAncestors.Observe(subtree, depthOffset);
            if (descriptionDepth >= 0 && subtree.NodeType != XmlNodeType.Element)
            {
                if (subtree.NodeType == XmlNodeType.EndElement && subtree.Depth == descriptionDepth) descriptionDepth = -1;
                continue;
            }
            if (subtree.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                text.BeginTextNode();
                int read;
                do
                {
                    read = subtree.ReadValueChunk(chunk, 0, chunk.Length);
                    if (read != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (read > maximumCharacters - sourceCharacterCount) throw new SvgException(SvgErrorCode.LimitExceeded, "The document direct text character count exceeds the admitted layout limit.");
                        sourceCharacterCount += read;
                        int currentScope = whitespaceNormalizers.Count - 1;
                        if (!text.GetPaint(currentScope).IsDisplayed) continue;
                        SvgTextWhitespaceNormalizer current = whitespaceNormalizers[currentScope];
                        bool introducesOutput = !current.HasOutput && (current.Mode == SvgTextWhitespaceMode.Preserve ? SvgTextWhitespaceNormalizer.HasText(chunk.AsSpan(0, read), cancellationToken) : SvgTextWhitespaceNormalizer.HasNonWhitespaceText(chunk.AsSpan(0, read), cancellationToken));
                        if (introducesOutput)
                        {
                            for (int ancestorScope = 0; ancestorScope < currentScope; ancestorScope++)
                            {
                                SvgTextWhitespaceNormalizer ancestor = whitespaceNormalizers[ancestorScope];
                                ancestor.ObserveChildContent();
                                whitespaceNormalizers[ancestorScope] = ancestor;
                            }
                        }
                        current.Append(chunk.AsSpan(0, read), text, maximumCharacters, cancellationToken);
                        whitespaceNormalizers[currentScope] = current;
                    }
                } while (read != 0);
                continue;
            }
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth != 0)
            {
                if (subtree.Depth + depthOffset >= maximumXmlDepth) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG XML nesting exceeds the configured limit.");
                if (childNodeCount >= remainingNodeCount) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG node count exceeds the configured limit.");
                childNodeCount++;
                if (subtree.AttributeCount > maximumAttributesPerElement) throw new SvgException(SvgErrorCode.LimitExceeded, "SVG element attributes exceed the configured limit.");
                bool svg = subtree.NamespaceURI == "http://www.w3.org/2000/svg";
                if (descriptionDepth >= 0)
                {
                    if (svg && SvgStructuralVocabulary.IsStyleScriptOrAnimationElement(subtree.LocalName))
                        throw new SvgException(SvgErrorCode.UnsupportedFeature, "This content inside an SVG description is not implemented.");
                    continue;
                }
                if (svg && SvgStructuralVocabulary.IsDescriptiveElement(subtree.LocalName))
                {
                    // Inert descriptions introduce neither a text chunk nor a
                    // style/position scope. Their descendants still count above.
                    if (!subtree.IsEmptyElement) descriptionDepth = subtree.Depth;
                    continue;
                }
                SvgTextWhitespaceNormalizer inheritedNormalizer = whitespaceNormalizers[^1];
                SvgTextSpanStyle inheritedStyle = new(inheritedNormalizer.Mode, inheritedNormalizer.LetterSpacing) { Paint = text.GetPaint(whitespaceNormalizers.Count - 1), Font = text.GetFont(whitespaceNormalizers.Count - 1) };
                if (subtree.NamespaceURI != "http://www.w3.org/2000/svg") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bounded SVG text children are implemented.");
                SvgTextSpanStyle childStyle;
                if (subtree.LocalName == "textPath")
                {
                    if (subtree.Depth != 1 || textPath is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only direct text-path children are implemented.");
                    RetainLayout(text, null, previousPath, true, runs);
                    text.BeginTextChunk(false);
                    SvgTextPathReference parsed = SvgTextPathSyntax.Parse(subtree, cancellationToken);
                    textPath = parsed with { PathId = state.Strings.Copy(parsed.PathId) };
                    InlineStyle pathStyle = SvgDocumentStyleResolver.Resolve(subtree, state, cancellationToken);
                    if (!pathStyle.Equals(default(InlineStyle))) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Styles on text-path children are not implemented.");
                    childStyle = new(SvgTextSyntax.ParseWhitespaceMode(subtree, null, inheritedStyle.WhitespaceMode), inheritedStyle.LetterSpacing) { Paint = inheritedStyle.Paint, Font = inheritedStyle.Font };
                }
                else
                {
                    if (subtree.LocalName != "tspan") throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bounded text span children are implemented.");
                    SvgElementAttributePolicy.ValidateTextSpan(subtree, cancellationToken);
                    InlineStyle styleSheet = SvgDocumentStyleResolver.Resolve(subtree, state, cancellationToken);
                    if (!SvgTextSyntax.TryParseTspanStyle(subtree, inheritedStyle, styleSheet, cancellationToken, out childStyle)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only bounded whitespace, scalar positioning and solid-paint tspan children are implemented.");
                    if (textPath is not null && childStyle.PositionOffset is SvgTextPositionOffset pathOffset && (pathOffset.X is not null || pathOffset.Y is not null)) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Absolute span coordinates on text paths are not implemented.");
                }
                if (!subtree.IsEmptyElement)
                {
                    int childScopeDepth = whitespaceNormalizers.Count;
                    bool ownsPositioningSpan = !childStyle.PositionLists.IsEmpty;
                    if (ownsPositioningSpan) text.EnterPositioningSpan(childStyle.PositionLists, childScopeDepth, cancellationToken);
                    whitespaceNormalizers.Add(new SvgTextWhitespaceNormalizer(childStyle.WhitespaceMode, childStyle.LetterSpacing, childScopeDepth, ownsPositioningSpan));
                    text.EnterStyleSpan(childStyle.Paint, childStyle.Font);
                }
                else if (subtree.LocalName == "textPath")
                {
                    previousPath = textPath;
                    textPath = null;
                    whitespaceNormalizers[0] = normalizer;
                    text.BeginTextChunk(true);
                }
                continue;
            }
            if (subtree.NodeType == XmlNodeType.EndElement && subtree.Depth != 0)
            {
                SvgTextWhitespaceNormalizer current = whitespaceNormalizers[^1];
                if (current.OwnsPositioningSpan) text.ExitPositioningSpan(current.ScopeDepth);
                whitespaceNormalizers.RemoveLast();
                text.ExitPaintSpan();
                if (subtree.Depth == 1 && subtree.LocalName == "textPath")
                {
                    RetainLayout(text, textPath, null, false, runs);
                    previousPath = textPath;
                    textPath = null;
                    whitespaceNormalizers[0] = normalizer;
                    text.BeginTextChunk(true);
                }
            }
        }
        RetainLayout(text, null, previousPath, false, runs);
        bool mixed = false;
        foreach (SvgExtractedTextRun run in runs.AsSpan())
            mixed |= run.Path is null && (whitespaceMode == SvgTextWhitespaceMode.Preserve
                || SvgTextWhitespaceNormalizer.HasNonWhitespaceText(run.Layout.Value.AsSpan(), cancellationToken));
        if (!mixed && previousPath is not null)
        {
            int retained = 0;
            for (int index = 0; index < runs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SvgExtractedTextRun run = runs[index];
                if (run.Path is null) run.Dispose();
                else runs[retained++] = run;
            }
            while (runs.Count > retained) runs.RemoveLast();
        }
        if (mixed)
        {
            int offset = 0;
            for (int index = 0; index < runs.Count; index++)
            {
                SvgExtractedTextRun run = runs[index];
                runs[index] = run with { CharacterOffset = offset };
                foreach (System.Text.Rune scalar in run.Layout.Value.EnumerateRunes()) { cancellationToken.ThrowIfCancellationRequested(); offset++; }
            }
        }

        static void RetainLayout(SvgTextPaintOutput text, SvgTextPathReference? path, SvgTextPathReference? previousPath, bool preserveTrailingWhitespace, BudgetedParserList<SvgExtractedTextRun> runs)
        {
            SvgExtractedTextRun run = text.Detach(path, preserveTrailingWhitespace) with { ResumeAfterPath = previousPath };
            if (run.Layout.Value.Length == 0) { run.Dispose(); return; }
            try { runs.Add(run); }
            catch { run.Dispose(); throw; }
        }
    }
}
