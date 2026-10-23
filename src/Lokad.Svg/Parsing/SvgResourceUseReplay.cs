using System.Xml;
using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgResourceUseKind { Mask, Clip, Pattern }
internal readonly record struct SvgResourceUse(SvgUseReference Reference, string? ClipId, bool IsEvenOdd);
internal readonly record struct SvgDeferredResourceUse(string Id, SvgResourceUseKind Kind, bool IsAlpha, SvgResourceUse Use);

/// <summary>Replays local resource references after forward geometry and paint definitions have registered.</summary>
/// <remarks>Attribute strings and pending entries belong to the document. Mapped geometry, clip arrays
/// and sealed vector scenes transfer to the existing registries; failure releases partial owners.
/// All scans and replay charge parser work and observe cancellation. Cleanup is single-threaded.</remarks>
internal static class SvgResourceUseReplay
{
    /// <summary>Consumes one admitted use leaf; a source-less filter owner retains transparent input.</summary>
    internal static SvgResourceUse? Retain(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, ParserWorkCounter work, CancellationToken token)
    {
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, colors, work, token);
        return Retain(reader, context, root, state);
    }

    internal static SvgResourceUse? Retain(XmlReader reader, SvgPaintableElementContext context, SvgRootAttributes root, SvgDocumentState state)
        => Retain(reader, context, new SvgViewportLengthBases(root.UserViewportWidth, root.UserViewportHeight), state);

    internal static SvgResourceUse? Retain(XmlReader reader, SvgPaintableElementContext context, SvgViewportLengthBases bases, SvgDocumentState state)
        => Retain(reader, context, bases, state, false);

    internal static SvgResourceUse? RetainPattern(XmlReader reader, SvgRootAttributes root, SvgDocumentState state,
        IReadOnlyDictionary<string, SvgColor> colors, ParserWorkCounter work, CancellationToken token)
    {
        SvgPaintableElementContext context = SvgPaintableElementContext.Create(reader, root, state, colors, work, token);
        return Retain(reader, context, new(root.UserViewportWidth, root.UserViewportHeight), state, true);
    }

    private static SvgResourceUse? Retain(XmlReader reader, SvgPaintableElementContext context, SvgViewportLengthBases bases, SvgDocumentState state, bool allowMask)
    {
        ParserWorkCounter work = context.WorkCounter;
        CancellationToken token = context.CancellationToken;
        SvgElementAttributePolicy.ValidateUse(reader, work, token);
        if (!allowMask && SvgLocalUrl.ReadReference(reader, "mask") is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Masks on mask content are not implemented.");
        InlineStyle inline = SvgInlineStyleParser.ParseUse(reader.GetAttribute("style"), work, token);
        context = context with { AuthoredStyle = SvgComputedStyle.Resolve(reader, inline, context.StyleSheet) };
        if (SvgLocalReferenceParser.ParseUse(reader, context, inline, state.Strings, bases.Width, bases.Height, state) is not {} reference)
        {
            SvgStructuralAdmission.ConsumeLeafChildren(reader, state, work, token);
            return null;
        }
        string? clip = SvgLocalUrl.ReadReference(reader, "clip-path");
        string? clipId = clip is null ? null : state.Strings.Copy(SvgClipPathSyntax.ParseClipId(clip));
        SvgComputedStyle style = context.ResolveStyle(reader);
        bool evenOdd = style.ClipRule is null ? state.ClipIsEvenOdd : SvgClipPathSyntax.ParseClipRule(style.ClipRule.AsSpan());
        SvgStructuralAdmission.ConsumeLeafChildren(reader, state, work, token);
        return new(reference, clipId, evenOdd);
    }

    internal static void Register(SvgDocumentState state, SvgResourceUseKind kind, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        bool clip = kind == SvgResourceUseKind.Clip;
        if (clip && !state.ClipIsDisplayed)
        {
            SvgDefinitionParser.RegisterClipBox(state, work, token);
            return;
        }
        string id = state.Strings.Copy((clip ? state.ClipPathId : state.MaskId) ?? throw new InvalidOperationException("Resource registration requires an identifier."));
        SvgResourceUse use = (clip ? state.ClipUse : state.MaskUse) ?? throw new InvalidOperationException("Resource registration requires a retained use.");
        if (clip)
        {
            if (state.Clips.TryGetValue(id, out _) || state.ObjectClips.TryGetValue(id, out _) || !state.ClipPolygons.TryAdd(id, new([], [])))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG clip-path ids must be unique.");
        }
        else
        {
            if (state.Masks.TryGetValue(id, out _) || !state.MaskContents.TryAdd(id, new([], 255, state.MaskIsAlpha)))
                throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
            if (state.MaskCustomRegion is {} region && !state.MaskRegions.TryAdd(id, region)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
            if (state.MaskFractionRegion is {} fractions && !state.MaskFractionRegions.TryAdd(id, fractions)) throw new SvgException(SvgErrorCode.MalformedInput, "SVG mask ids must be unique.");
        }
        state.DeferredResourceUses.Add(new(id, kind, state.MaskIsAlpha, use));
    }

    internal static void Resolve(SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        // Clip references resolve first; mask references can then borrow their native contours.
        for (int pass = 0; pass < 2; pass++)
        for (int index = 0; index < state.DeferredResourceUses.Count; index++)
        {
            SvgResourceUseKind kind = pass == 0 ? SvgResourceUseKind.Clip : SvgResourceUseKind.Mask;
            work.Consume(token);
            SvgDeferredResourceUse pending = state.DeferredResourceUses[index];
            if (pending.Kind != kind) continue;
            if (kind == SvgResourceUseKind.Clip
                && (state.GroupSnapshots.TryGetValue(pending.Use.Reference.Id, out _)
                    || state.RenderedGroupDefinitions.TryGetValue(pending.Use.Reference.Id, out _)))
            {
                state.Clips.TryAdd(pending.Id, new(0, 0, 0, 0));
                continue;
            }
            if (!SvgReferenceResolver.TryGetDefinition(pending.Use.Reference, state.Definitions, out SvgDefinition source))
                throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local geometry references in resource content are implemented.");
            if (kind == SvgResourceUseKind.Clip && (source.StrokeContent is not null || source.SymbolContent is not null || source.RenderedGroupId is not null))
            {
                // A clip use must directly reference a path/text/basic shape.
                // Container references contribute no geometry, including a one-child group.
                // The paired empty polygon remains the definition's staging marker.
                state.Clips.TryAdd(pending.Id, new(0, 0, 0, 0));
                continue;
            }
            SvgUseReference reference = pending.Use.Reference;
            if (kind == SvgResourceUseKind.Clip)
                reference = reference with { FillOverride = new(new(255, 255, 255, 255), true, SvgPaintOpacity.Opaque, new(255, 255, 255, 255), false), PaintServerId = null, PaintServerFill = null, Opacity = 255 };
            SvgGeometry[] mapped = kind == SvgResourceUseKind.Clip
                ? SvgReferenceResolver.Apply(source, reference with { Opacity = 255 }, null, state, work, token)
                : SvgReferenceResolver.ApplyFilterSource(source, reference with { Opacity = 255 }, null, state, work, token);
            bool adopted = false;
            try
            {
                if (kind == SvgResourceUseKind.Clip)
                {
                    if (pending.Use.ClipId is not null) throw new SvgException(SvgErrorCode.UnsupportedFeature, "Clips on referenced clip content are not implemented.");
                    SvgDevicePoint[]? points = null;
                    SvgAuthoredPath? path = null;
                    foreach (SvgGeometry geometry in mapped)
                    {
                        work.Consume(token);
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
                    for (int point = 0; point < points.Length; point++) { work.Consume(token); copy[point] = points[point]; }
                    SvgClipPolygon polygon = new(copy, [copy.Length]) { IsEvenOdd = pending.Use.IsEvenOdd };
                    SvgAuthoredPath? copied = path?.Copy(SvgAffineTransform.Identity, state.Budget, work, token);
                    try { state.ClipPolygons.Replace(pending.Id, polygon with { SourcePath = copied }); }
                    catch { copied?.Dispose(); throw; }
                }
                else
                {
                    using SvgSceneBuilder builder = new(state.Budget, work, token);
                    AppendMaskUse(pending.Use, reference, mapped, builder, state, work, token);
                    adopted = true;
                    SvgLeafMaskComposition.Emit(state, builder, work, token);
                    SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
                    try { state.Scene.AdoptVectorContent(content); }
                    catch { content.Dispose(); throw; }
                    SvgMaskSourceRange? replaySource = state.MaskContents.TryGetValue(pending.Id, out SvgMaskContent previous) ? previous.ReplaySource : null;
                    state.MaskContents.Replace(pending.Id, new([], 255, pending.IsAlpha) { VectorContent = content, ReplaySource = replaySource });
                }
            }
            finally { if (!adopted) foreach (SvgGeometry geometry in mapped) geometry.Dispose(); }
        }
    }

    internal static void AppendMaskUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        => AppendUse(use, builder, state, SvgResourceUseKind.Mask, work, token);

    internal static void AppendPatternUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
        => AppendUse(use, builder, state, SvgResourceUseKind.Pattern, work, token);

    /// <summary>Adopts shared reference replay into a resource scene; tile paints reject recursive pattern dependencies.</summary>
    private static void AppendUse(SvgResourceUse use, SvgSceneBuilder builder, SvgDocumentState state, SvgResourceUseKind kind, ParserWorkCounter work, CancellationToken token)
    {
        work.Consume(token);
        if (!SvgReferenceResolver.TryGetDefinition(use.Reference, state.Definitions, out SvgDefinition source)
            && !state.RenderedGroupDefinitions.TryGetValue(use.Reference.Id, out source))
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Only defined local geometry references in resource content are implemented.");
        bool previousPicture = state.IsResourcePicture;
        SvgGeometry[] mapped;
        state.IsResourcePicture = true;
        try { mapped = SvgReferenceResolver.ApplyFilterSource(source, use.Reference with { Opacity = 255 }, null, state, work, token); }
        finally { state.IsResourcePicture = previousPicture; }
        bool adopted = false;
        try
        {
            if (kind == SvgResourceUseKind.Pattern)
                foreach (SvgGeometry geometry in mapped)
                {
                    work.Consume(token);
                    SvgPatternUseReplay.RejectNestedPaint(geometry);
                }
            AppendMaskUse(use, use.Reference, mapped, builder, state, work, token);
            adopted = true;
        }
        finally { if (!adopted) foreach (SvgGeometry geometry in mapped) geometry.Dispose(); }
    }

    private static void AppendMaskUse(SvgResourceUse use, SvgUseReference reference, SvgGeometry[] mapped,
        SvgSceneBuilder builder, SvgDocumentState state, ParserWorkCounter work, CancellationToken token)
    {
        SvgAffineTransform placement = SvgReferenceResolver.PlainInstancePlacement(reference, null);
        foreach (ref SvgGeometry geometry in mapped.AsSpan())
        {
            work.Consume(token);
            geometry = SvgPaintRecording.RebindLeafPaint(geometry, state.Scene, builder);
        }
        SvgSceneBuilder? previous = state.DefinitionReplayScene;
        state.DefinitionReplayScene = builder;
        try
        {
            if (reference.IsDisplayed)
                SvgPaintRecording.WrapFilter(mapped, reference.Filter, placement, reference.LengthBases, state, work, token);
            SvgReferenceResolver.ApplyMask(mapped, null, null, reference.SourceMaskReference, placement, state, work, token);
            // Native use clipping surrounds the isolated mask/filter result. Its bounds
            // also constrain mask-content construction; source effects remain inside.
            if (use.ClipId is {} id)
                SvgClipPathSyntax.ApplyResourceClip(mapped, id, placement, state, work, token);
            SvgPaintRecording.WrapInstance(mapped, reference.SourceOpacity, reference.RecordingScope, state, work, token);
            builder.AddGeometries(mapped);
        }
        finally { state.DefinitionReplayScene = previous; }
    }
}
