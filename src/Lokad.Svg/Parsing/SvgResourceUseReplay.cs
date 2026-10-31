using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgResourceUseKind { Mask, Pattern }
internal readonly record struct SvgResourceUse(SvgUseReference Reference, string? ClipId, bool IsEvenOdd);
internal readonly record struct SvgDeferredClipUse(string Id, SvgResourceUse Use);

/// <summary>Replays local resource references after forward geometry and paint definitions have registered.</summary>
/// <remarks>Attribute strings and pending entries belong to the document. Mapped geometry, clip arrays
/// and sealed vector scenes transfer to the existing registries; failure releases partial owners.
/// All scans and replay observe cancellation. Cleanup is single-threaded.</remarks>
internal static class SvgResourceUseReplay
{
    internal static SvgResourceUse? Retain(XmlReader reader, SvgPaintableElementContext context, SvgRootAttributes root, SvgDocumentState state)
        => Retain(reader, context, new SvgViewportLengthBases(root.UserViewportWidth, root.UserViewportHeight), state);

    internal static SvgResourceUse? Retain(XmlReader reader, SvgPaintableElementContext context, SvgViewportLengthBases bases, SvgDocumentState state)
        => Retain(reader, context, bases, state, false);

    internal static SvgResourceUse? RetainPattern(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, CancellationToken token)
    {
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, colors, token);
        return Retain(reader, context, new(root.UserViewportWidth, root.UserViewportHeight), state, true);
    }

    private static SvgResourceUse? Retain(XmlReader reader, SvgPaintableElementContext context, SvgViewportLengthBases bases, SvgDocumentState state, bool allowMask)
    {
        CancellationToken token = context.CancellationToken;
        SvgElementAttributePolicy.ValidateUse(reader, token);
        if (!allowMask && SvgLocalUrl.ReadReference(reader, "mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
        InlineStyle inline = SvgInlineStyleParser.ParseUse(reader.GetAttribute("style"), token);
        context = context with { AuthoredStyle = SvgComputedStyle.Resolve(reader, inline, context.StyleSheet) };
        if (SvgLocalReferenceParser.ParseUse(reader, context, inline, state.Strings, bases.Width, bases.Height, state) is not {} reference)
        {
            SvgStructuralAdmission.ConsumeLeafChildren(reader, state, token);
            return null;
        }
        string? clip = SvgLocalUrl.ReadReference(reader, "clip-path");
        string? clipId = clip is null ? null : state.Strings.Copy(SvgClipPathSyntax.ParseClipId(clip));
        SvgComputedStyle style = context.ResolveStyle(reader);
        bool evenOdd = style.ClipRule is null ? state.ClipIsEvenOdd : SvgClipPathSyntax.ParseClipRule(style.ClipRule.AsSpan());
        SvgStructuralAdmission.ConsumeLeafChildren(reader, state, token);
        return new(reference, clipId, evenOdd);
    }

    internal static void RegisterClip(SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!state.ClipIsDisplayed)
        {
            SvgDefinitionParser.RegisterClipBox(state, token);
            return;
        }
        string id = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip registration requires an identifier."));
        SvgResourceUse use = state.ClipUse ?? throw new InvalidOperationException("Clip registration requires a retained use.");
        if (state.Clips.TryGetValue(id, out _) || state.ObjectClips.TryGetValue(id, out _) || !state.ClipPolygons.TryAdd(id, new([], [])))
            throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
        state.DeferredClipUses.Add(new(id, use));
    }

    internal static void ResolveClips(SvgDocumentState state, CancellationToken token)
    {
        for (int index = 0; index < state.DeferredClipUses.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgDeferredClipUse pending = state.DeferredClipUses[index];
            if (state.GroupSnapshots.TryGetValue(pending.Use.Reference.Id, out _)
                || state.RenderedGroupDefinitions.TryGetValue(pending.Use.Reference.Id, out _))
            {
                state.Clips.TryAdd(pending.Id, new(0, 0, 0, 0));
                continue;
            }
            if (!SvgReferenceResolver.TryGetDefinition(pending.Use.Reference, state.Definitions, out SvgDefinition source))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local geometry references in resource content are implemented.");
            if (source.StrokeContent is not null || source.SymbolContent is not null || source.RenderedGroupId is not null)
            {
                // Container references contribute no clip contour, including a one-child group.
                state.Clips.TryAdd(pending.Id, new(0, 0, 0, 0));
                continue;
            }
            SvgUseReference reference = pending.Use.Reference with
            {
                FillOverride = new(new(new(255, 255, 255, 255), 1), true, SvgPaintOpacity.Opaque, new(new(255, 255, 255, 255), 1), false),
                PaintServerId = null, PaintServerFill = null, StrokeOverride = null, Opacity = 255
            };
            SvgGeometry[] mapped;
            float boundsInflation = 0;
            if (source.LeafSource is {} leaf)
            {
                (SvgDefinition silhouette, float inflation) = leaf.LowerClip(reference, state);
                try { mapped = SvgReferenceResolver.Apply(silhouette, reference, null, state, token); }
                finally { foreach (SvgGeometry geometry in silhouette.Geometries) geometry.Dispose(); }
                boundsInflation = inflation;
            }
            else mapped = SvgReferenceResolver.Apply(source, reference, null, state, token);
            try
            {
                if (pending.Use.ClipId is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clips on referenced clip content are not implemented.");
                SvgDevicePoint[]? points = null;
                SvgAuthoredPath? path = null;
                foreach (SvgGeometry geometry in mapped)
                {
                    token.ThrowIfCancellationRequested();
                    if (state.Scene.GetRecordedPaintValue(geometry.RecordedPaint).IsStroke) continue;
                    (SvgDevicePoint[] Points, int[] Ends) fill = geometry.Kind switch
                    {
                        SvgGeometryKind.PolygonFill => (geometry.Polygon.Points, geometry.Polygon.ContourEnds),
                        SvgGeometryKind.GradientPolygon => (geometry.GradientPolygon.Points, geometry.GradientPolygon.ContourEnds),
                        SvgGeometryKind.RadialPolygon => (geometry.RadialPolygon.Points, geometry.RadialPolygon.ContourEnds),
                        SvgGeometryKind.PatternPolygon => (geometry.PatternPolygon.Points, geometry.PatternPolygon.ContourEnds),
                        _ => throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only contour geometry in referenced clip content is implemented."),
                    };
                    if (points is not null || fill.Ends.Length != 1) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only single-contour referenced clip content is implemented.");
                    points = fill.Points; path = geometry.AuthoredPath;
                }
                if (points is null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Empty referenced clip content is not implemented.");
                if (!reference.IsDisplayed || !source.SourceDisplayed || !(source.SourceVisible ?? reference.IsVisible))
                {
                    state.Clips.TryAdd(pending.Id, new(0, 0, 0, 0));
                    continue;
                }
                state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Geometry));
                SvgDevicePoint[] copy = new SvgDevicePoint[points.Length];
                for (int point = 0; point < points.Length; point++) { token.ThrowIfCancellationRequested(); copy[point] = points[point]; }
                SvgClipPolygon polygon = new(copy, [copy.Length]) { IsEvenOdd = pending.Use.IsEvenOdd };
                SvgAuthoredPath? copied = path?.Copy(SvgAffineTransform.Identity, state.Budget, token);
                // Referenced contours retain the same authored construction as
                // direct clip children, including their controls and placement.
                try { state.ClipPolygons.Replace(pending.Id, (polygon with { SourcePath = copied, UsesNativeReplay = copied is not null })
                    .WithVisualBounds(boundsInflation, token)); }
                catch { copied?.Dispose(); throw; }
            }
            finally { foreach (SvgGeometry geometry in mapped) geometry.Dispose(); }
        }
    }

    internal static void AppendMaskUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, CancellationToken token)
        => AppendUse(use, builder, state, SvgResourceUseKind.Mask, token);

    internal static void AppendPatternUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, CancellationToken token)
        => AppendUse(use, builder, state, SvgResourceUseKind.Pattern, token);

    /// <summary>Adopts shared reference replay into a resource scene; tile paints reject recursive pattern dependencies.</summary>
    private static void AppendUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, SvgResourceUseKind kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SvgReferenceResolver.TryGetDefinition(use.Reference, state.Definitions, out SvgDefinition source)
            && !state.RenderedGroupDefinitions.TryGetValue(use.Reference.Id, out source))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local geometry references in resource content are implemented.");
        bool previousPicture = state.IsResourcePicture;
        SvgGeometry[] mapped;
        state.IsResourcePicture = true;
        try { mapped = SvgReferenceResolver.ApplyFilterSource(source, use.Reference with { Opacity = 255 }, null, state, token); }
        finally { state.IsResourcePicture = previousPicture; }
        bool adopted = false;
        SvgLeafMaskMapper? masks = null;
        try
        {
            if (kind == SvgResourceUseKind.Pattern)
                foreach (SvgGeometry geometry in mapped)
                {
                    token.ThrowIfCancellationRequested();
                    SvgPatternUseReplay.RejectNestedPaint(geometry);
                }
            SvgUseReference reference = use.Reference;
            SvgAffineTransform placement = SvgReferenceResolver.PlainInstancePlacement(reference, null);
            foreach (ref SvgGeometry geometry in mapped.AsSpan())
            {
                token.ThrowIfCancellationRequested();
                if (geometry.LeafMask is not null && !ReferenceEquals(state.Scene, builder))
                    masks ??= SvgLeafMaskMapper.CreateSceneTransfer(state, state.Scene, builder, token);
                geometry = SvgPaintRecording.RebindLeafScene(geometry, state.Scene, builder, masks);
            }
            SvgSceneBuilder? previous = state.DefinitionReplayScene;
            state.DefinitionReplayScene = builder;
            try
            {
                if (reference.IsDisplayed)
                {
                    SvgPaintRecording.WrapFilter(mapped, reference.Filter, placement, reference.LengthBases, state, token);
                    SvgReferenceResolver.ApplyMask(mapped, null, null, reference.SourceMaskReference, placement, state, token);
                }
                // Native use clipping surrounds the isolated mask/filter result. Its bounds
                // also constrain mask-content construction; source effects remain inside.
                if (use.ClipId is {} id)
                    SvgClipPathSyntax.ApplyResourceClip(mapped, id, placement, state, token);
                SvgPaintRecording.WrapInstance(mapped, reference.SourceOpacity, reference.RecordingScope, state, token);
                builder.AddGeometries(mapped);
            }
            finally { state.DefinitionReplayScene = previous; }
            adopted = true;
        }
        finally { masks?.Dispose(); if (!adopted) foreach (SvgGeometry geometry in mapped) geometry.Dispose(); }
    }
}
