using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal readonly record struct SvgResourceUse(SvgUseReference Reference, string? ClipId, bool IsEvenOdd);

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
        InlineStyle inline = SvgInlineStyleParser.ReadUse(reader, token);
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

    internal static void ResolveClips(SvgDocumentState state, CancellationToken token)
    {
        if (state.DeferredClips is not {} definitions) return;
        BudgetedParserList<SvgClipDefinitionSource> sources = state.ClipSources ?? throw new InvalidOperationException("Deferred clip sources are missing.");
        bool previousDisplay = state.ClipIsDisplayed;
        bool previousUnits = state.ClipIsObjectBoundingBox;
        string? previousResourceClip = state.ClipResourceClipId;
        try
        {
            state.ClipIsDisplayed = true;
            foreach (SvgDeferredClipDefinition definition in definitions.AsSpan())
            {
                token.ThrowIfCancellationRequested();
                state.ClipIsObjectBoundingBox = definition.IsObjectBoundingBox;
                state.ClipResourceClipId = definition.ResourceClipId;
                bool hasText = false;
                for (int index = definition.FirstSource; index < definition.FirstSource + definition.SourceCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgClipDefinitionSource source = sources[index];
                    hasText |= source.Kind == SvgClipDefinitionSourceKind.Text && source.Text.Contributes
                        || source.Kind == SvgClipDefinitionSourceKind.Use
                            && SvgReferenceResolver.TryGetDefinition(source.Use.Reference, state.Definitions, out SvgDefinition target)
                            && target.TextSource is not null && source.Use.Reference.IsDisplayed && target.SourceDisplayed
                            && (target.SourceVisible ?? source.Use.Reference.IsVisible);
                }
                if (hasText)
                {
                    for (int index = definition.FirstSource; index < definition.FirstSource + definition.SourceCount; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        SvgClipDefinitionSource source = sources[index];
                        if (source.Kind != SvgClipDefinitionSourceKind.Use) continue;
                        if (SvgReferenceResolver.TryGetDefinition(source.Use.Reference, state.Definitions, out SvgDefinition target)
                            && target.TextSource is not null) continue;
                        SvgClipContent? geometry = LowerReferencedClip(source.Use, state, token);
                        sources[index] = geometry is {} value ? new(value) : default;
                    }
                    state.ClipContents.Replace(definition.Id, new([], [])
                    {
                        PictureSources = new(definition.Id, definition.FirstSource, definition.SourceCount),
                        UsesMaskFallback = true, IsObjectBoundingBox = definition.IsObjectBoundingBox,
                        ResourceClipId = definition.ResourceClipId
                    });
                    continue;
                }
                for (int index = definition.FirstSource; index < definition.FirstSource + definition.SourceCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgClipDefinitionSource source = sources[index];
                    sources[index] = default;
                    SvgClipContent? child = source.Kind switch
                    {
                        SvgClipDefinitionSourceKind.Geometry => source.Geometry,
                        SvgClipDefinitionSourceKind.Use => LowerReferencedClip(source.Use, state, token),
                        SvgClipDefinitionSourceKind.Text => null,
                        _ => throw new InvalidOperationException("A deferred clip source was already consumed.")
                    };
                    if (child is {} geometry) SvgClipDefinitionStaging.AppendClipChild(geometry, 0, state, token);
                }
                if (definition.ResourceClipId is not null) SvgClipDefinitionStaging.PromoteClipRectangle(state, token);
                if (state.ClipPolygon is null || state.ClipBox is not null)
                {
                    if (definition.IsObjectBoundingBox) state.ObjectClips.TryAdd(definition.Id, state.ClipBox ?? new(0, 0, 0, 0));
                    else state.Clips.TryAdd(definition.Id, state.ClipBox ?? new(0, 0, 0, 0));
                    state.ClipPolygon?.SourcePath?.Dispose();
                }
                else
                {
                    SvgClipContent polygon = SvgClipDefinitionStaging.Complete(state, token);
                    state.ClipContents.Replace(definition.Id, polygon);
                }
                // Registered dictionaries now own the completed path; the next
                // definition starts with independent parser staging.
                state.ClipPolygon = null;
                state.ClipBox = null;
            }
        }
        finally
        {
            state.ClipPolygon?.SourcePath?.Dispose();
            state.ClipPolygon = null;
            state.ClipBox = null;
            state.ClipIsDisplayed = previousDisplay;
            state.ClipIsObjectBoundingBox = previousUnits;
            state.ClipResourceClipId = previousResourceClip;
        }

        static SvgClipContent? LowerReferencedClip(SvgResourceUse use, SvgDocumentState state, CancellationToken token)
        {
            if (state.GroupSnapshots.TryGetValue(use.Reference.Id, out _)
                || state.RenderedGroupDefinitions.TryGetValue(use.Reference.Id, out _)) return null;
            if (!SvgReferenceResolver.TryGetDefinition(use.Reference, state.Definitions, out SvgDefinition source))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local geometry references in resource content are implemented.");
            if (source.TextSource is not null) return null;
            if (source.StrokeContent is not null || source.SymbolContent is not null || source.RenderedGroupId is not null)
            {
                // Container references contribute no clip contour, including a one-child group.
                return null;
            }
            SvgUseReference reference = use.Reference with
            {
                FillOverride = new(new(new(255, 255, 255, 255), 1), true, SvgPaintOpacity.Opaque, new(new(255, 255, 255, 255), 1), false),
                PaintServerId = null, PaintServerFill = null, StrokeOverride = null, Opacity = 255, IsPainted = true
            };
            SvgGeometry[] mapped;
            float boundsInflation = 0;
            SvgPaintOpacity sourceOpacity = SvgPaintOpacity.Opaque;
            if (source.LeafSource is {} leaf)
            {
                (SvgDefinition silhouette, float inflation, SvgPaintOpacity opacity) = leaf.LowerClip(reference, state);
                sourceOpacity = opacity;
                try { mapped = SvgReferenceResolver.Apply(silhouette, reference, null, state, token); }
                finally { foreach (SvgGeometry geometry in silhouette.Geometries) geometry.Dispose(); }
                boundsInflation = inflation;
            }
            else mapped = SvgReferenceResolver.Apply(source, reference, null, state, token);
            try
            {
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
                    return null;
                }
                state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(points.Length) + ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Geometry));
                SvgDevicePoint[] copy = new SvgDevicePoint[points.Length];
                for (int point = 0; point < points.Length; point++) { token.ThrowIfCancellationRequested(); copy[point] = points[point]; }
                SvgClipContent polygon = new(copy, [copy.Length]) { SourceOpacity = sourceOpacity, InstanceOpacity = use.Reference.SourceOpacity, IsEvenOdd = use.IsEvenOdd, IsObjectBoundingBox = state.ClipIsObjectBoundingBox,
                    ChildClip = use.ClipId is {} clipId ? new(clipId, SvgReferenceResolver.PlainInstancePlacement(reference, null)) : null };
                SvgAuthoredPath? copied = path?.Copy(SvgAffineTransform.Identity, state.Budget, token);
                // Referenced contours retain the same authored construction as
                // direct clip children, including their controls and placement.
                try { return (polygon with { SourcePath = copied, UsesNativeReplay = copied is not null }).WithVisualBounds(boundsInflation, token); }
                catch { copied?.Dispose(); throw; }
            }
            finally { foreach (SvgGeometry geometry in mapped) geometry.Dispose(); }
        }
    }

    internal static void AppendMaskUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, CancellationToken token)
        => AppendUse(use, builder, state, token);

    internal static void AppendPatternUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, CancellationToken token)
        => AppendUse(use, builder, state, token);

    /// <summary>Adopts shared reference replay into a resource scene; sealing validates tile content dependencies.</summary>
    private static void AppendUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, CancellationToken token)
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
                    SvgPaintRecording.WrapFilter(mapped, reference.Filter, placement, reference.LengthBases, reference.RecordingTransform, state, token);
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
