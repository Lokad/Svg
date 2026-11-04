namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Borrows a contiguous document-staging range of independently owned clip child paths.</summary>
internal readonly record struct SvgClipChildRange(int First, int Count);
/// <summary>Keeps an allocation-free clip rectangle and its visual outset until a sibling requires native source replay.</summary>
internal readonly record struct SvgClipRectangleSource(SvgAffineViewportClip Geometry, float BoundsInflation, SvgPaintOpacity SourceOpacity);
internal enum SvgClipPaintKind : byte { Path, Rectangle }
/// <summary>Borrows a local clip and its placement independently of the source geometry's map.</summary>
internal readonly record struct SvgClipIntersection(string Id, SvgAffineTransform Map);
internal readonly record struct SvgClipChild(SvgAuthoredPath Path, bool IsEvenOdd, SvgClipPaintKind PaintKind, SvgClipIntersection? Clip)
{
    // Path clipping ignores paint opacity; picture replay preserves the authored
    // source and instance stages separately, including their rounding boundaries.
    internal SvgPaintOpacity SourceOpacity { get; init; } = SvgPaintOpacity.Opaque;
    internal SvgPaintOpacity InstanceOpacity { get; init; } = SvgPaintOpacity.Opaque;
    internal SvgClipChild(SvgAuthoredPath path, bool isEvenOdd, SvgClipPaintKind paintKind) : this(path, isEvenOdd, paintKind, null) { }
}

/// <summary>Borrows final-scene clip content and an optional enclosing viewport for a painted clip.</summary>
internal readonly record struct SvgClipRegistration(SvgClipBox Bounds, int ContentIndex, int EnclosureIndex)
{
    /// <summary>Copies final-scene indices into a parser-owned operation, reserving its enclosing operation in the document registry.</summary>
    internal void Bind(SvgLeafMaskOperation operation, SvgDocumentState state)
    {
        operation.Region = Bounds;
        operation.ContentIndex = ContentIndex;
        operation.RegionKind = EnclosureIndex < 0 ? SvgLayerRegionKind.AffineViewport : SvgLayerRegionKind.ClipMask;
        if (EnclosureIndex < 0) return;
        SvgLeafMaskOperation enclosing = SvgLeafMaskComposition.NewOperation(state, null, SvgAffineTransform.Identity);
        enclosing.Resolved = true;
        enclosing.Value = 255;
        enclosing.Region = Bounds;
        enclosing.ContentIndex = EnclosureIndex;
        enclosing.RegionKind = SvgLayerRegionKind.AffineViewport;
        operation.EnclosingClip = enclosing;
    }
}

/// <summary>Retains one admitted clip-path polygon and its independent winding rule in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates. Every retained contour has at least three samples; a curved chord can retain its closing endpoint when flattening produces fewer distinct vertices. Ends partition the points exactly. Short straight contours contribute no clip coverage. Compound paths replay directed winding or parity through the native clip scene, including holes, concavity, self-intersections and canceling coverage. Source commands remain borrowed from document staging.</remarks>
internal readonly record struct SvgClipContent(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>Retains a source picture instead of polygon coverage; document staging owns the borrowed range.</summary>
    internal SvgClipSourceRange? PictureSources { get; init; }
    internal bool IsEmpty => PictureSources is null && Points.Length == 0;
    /// <summary>A display-suppressed resource contributes no binding, unlike an empty silhouette.</summary>
    internal bool IsDisabled { get; init; }
    /// <summary>Borrows immutable native path commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
    /// <summary>Retains the authored child paint operation independently of its clipping path.</summary>
    internal SvgClipPaintKind PaintKind { get; init; }
    internal SvgPaintOpacity SourceOpacity { get; init; } = SvgPaintOpacity.Opaque;
    internal SvgPaintOpacity InstanceOpacity { get; init; } = SvgPaintOpacity.Opaque;
    internal bool IsEvenOdd { get; init; }
    /// <summary>Selects authored replay for curves, compound contours and nontrivial unions; contained rectangles retain their primitive coverage.</summary>
    internal bool UsesNativeReplay { get; init; }
    /// <summary>Retains the native oval clip identity when its child map preserves axis-aligned oval bounds.</summary>
    internal bool IsOval { get; init; }
    /// <summary>Maps normalized content through the referencing element's geometry bounds before native replay.</summary>
    internal bool IsObjectBoundingBox { get; init; }
    /// <summary>Borrows the local clip applied to this resource's child geometry.</summary>
    internal SvgClipIntersection? ChildClip { get; init; }
    /// <summary>Borrows the clip intersecting the complete definition after its children are united.</summary>
    internal string? ResourceClipId { get; init; }
    internal bool HasIntersections => ChildClip is not null || ResourceClipId is not null;
    /// <summary>Preserves mapped child visual boxes, including ignored stroke extents, independently of the clip silhouette.</summary>
    internal SvgPaintBounds? ResourceBounds { get; init; }
    /// <summary>Counts authored children even when containment leaves one effective rectangle.</summary>
    internal int ChildCount { get; init; } = 1;
    /// <summary>Identifies an already resolved native boundary whose curve verbs must survive registration.</summary>
    internal bool IsResolvedUnion { get; init; }
    /// <summary>Original children are retained; their ordered union is resolved once when the definition closes.</summary>
    internal bool DeferredUnion { get; init; }
    internal SvgClipChildRange? Children { get; init; }
    internal bool UsesMaskFallback { get; init; }

    /// <summary>Expands only the resource's layout enclosure; authored source commands and clip coverage stay unchanged.</summary>
    /// <remarks>Source commands remain borrowed. Bounds use native float arithmetic before the child map;
    /// the stack-only calculation observes cancellation and retains no additional owner. Failure leaves
    /// this value's source ownership with the caller.</remarks>
    internal SvgClipContent WithVisualBounds(float inflation, CancellationToken token)
    {
        if (inflation <= 0 || SourcePath is not {} source) return this;
        SvgRectangle bounds = SvgPathBounds.Compute(source.Commands, token);
        SvgPaintBounds visual = new((float)bounds.X - inflation, (float)bounds.Y - inflation,
            (float)(bounds.X + bounds.Width) + inflation, (float)(bounds.Y + bounds.Height) + inflation);
        return this with { ResourceBounds = visual.Map(source.SceneMap, token) };
    }

    /// <summary>Registers either one resolved native path or ordered native mask-source replay.</summary>
    /// <remarks>Child ranges borrow document staging. Geometry/command copies belong to a sealed vector
    /// scene adopted by the parent; no staging owner escapes parsing. All material storage reserves,
    /// copy/replay visits observe cancellation token, and failures release partial builders and geometry.
    /// Map places resource coordinates; canvasMap places the referencing element before its children.
    /// Object-box normalization belongs to the resource map and is materialized before
    /// integer path construction; the referencing canvas map stays independent.</remarks>
    internal SvgClipRegistration Register(SvgAffineTransform map, SvgAffineTransform canvasMap, bool replaySource, SvgDocumentState state,
        SvgSceneBuilder scene, CancellationToken token)
    {
        if (PictureSources is {} picture) return RegisterPicture(this, picture, map, state, scene, token);
        if (!UsesMaskFallback)
        {
            if (ResourceClipId is not null && ChildCount == 1 && PaintKind == SvgClipPaintKind.Rectangle && !UsesNativeReplay
                && SourcePath is {} rectangleSource)
            {
                SvgRectangle rectangle = SvgPathBounds.Compute(rectangleSource.Commands, token);
                return WithoutEnclosure(new SvgAffineViewportClip(new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
                    map.ComposeChecked(rectangleSource.SceneMap)).Register(state, scene, token));
            }
            // Containment leaves one original rectangle. Keep object-box polygon
            // registration for admitted affine instances, but preserve rectangular
            // coverage rather than adding a generic path's enclosing clip.
            if (IsObjectBoundingBox && ChildCount > 1 && !UsesNativeReplay)
            {
                SvgAuthoredPath rectangle = SourcePath ?? throw new InvalidOperationException("A contained clip rectangle requires source geometry.");
                SvgRectangle rectangleBounds = SvgPathBounds.Compute(rectangle.Commands, rectangle.SceneMap, token);
                return WithoutEnclosure(new SvgAffineViewportClip(new(rectangleBounds.X, rectangleBounds.Y, rectangleBounds.Width, rectangleBounds.Height), map)
                    .Register(state, scene, token));
            }
            if (IsObjectBoundingBox && !IsOval && replaySource && SourcePath is {} source)
            {
                // Integer path enclosures belong to the referencing element's
                // local canvas, after object-box normalization. Enclosing unit
                // fractions first makes the construction clip too large.
                if (!canvasMap.TryInvert(out SvgAffineTransform inverse))
                    throw new SvgException(SvgErrorCode.UnsupportedFeature, "Bounding-box clips require a nonsingular referencing map.");
                SvgAffineTransform normalization = inverse.ComposeChecked(map);
                using SvgAuthoredPath normalized = source.Copy(normalization, state.Budget, token);
                using SvgPreparedPathContours prepared = normalized.Prepare(SvgAffineTransform.Identity, IsEvenOdd, state.Budget, token);
                SvgPaintBounds? normalizedBounds = ResourceBounds is {} visual ? visual.Map(normalization, token) : null;
                return WithoutEnclosure(SvgAffineViewportClip.RegisterContour(prepared.Points, prepared.ContourEnds,
                    canvasMap, SvgClipContentKind.ClipPath, normalized, normalizedBounds, IsEvenOdd, state, scene, token,
                    new(canvasMap, SvgAffineTransform.Identity, canvasMap)));
            }
            return WithoutEnclosure(SvgAffineViewportClip.RegisterContour(Points, ContourEnds, map,
                IsOval ? SvgClipContentKind.OvalClipPath : SvgClipContentKind.ClipPath,
                replaySource ? SourcePath : null, ResourceBounds, IsEvenOdd, state, scene, token,
                IsOval ? null : new SvgCanvasDrawMap(canvasMap, SvgAffineTransform.Identity, canvasMap)));
        }
        SvgClipChildRange range = Children ?? throw new InvalidOperationException("Clip fallback requires original child sources.");
        BudgetedParserList<SvgClipChild> children = state.ClipChildren ?? throw new InvalidOperationException("Clip child sources are missing.");
        using SvgSceneBuilder builder = new(state.Budget, token);
        bool hasChildClips = false;
        for (int index = range.First; index < range.First + range.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgClipChild child = children[index];
            hasChildClips |= AppendGeometry(child, ResourceClipId, map, builder, state, token);
        }
        if (hasChildClips) SvgLeafMaskComposition.Emit(state, builder, token);
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        SvgPaintBounds bounds = ResourceBounds ?? throw new InvalidOperationException("Clip fallback requires resource bounds.");
        SvgClipBox enclosure = new(Math.Floor(bounds.Left), Math.Floor(bounds.Top), Math.Ceiling(bounds.Right) - Math.Floor(bounds.Left), Math.Ceiling(bounds.Bottom) - Math.Floor(bounds.Top));
        (SvgClipBox region, int boundsIndex) = new SvgAffineViewportClip(enclosure, map).Register(state, scene, token);
        int contentIndex = scene.AddMaskContent(new([], 255, true)
            { VectorContent = content });
        return new(region, contentIndex, boundsIndex);

        static bool AppendGeometry(SvgClipChild child, string? resourceClipId, SvgAffineTransform map,
            SvgSceneBuilder builder, SvgDocumentState state, CancellationToken token)
        {
            bool hasChildClips = false;
            using SvgPreparedPathContours prepared = child.Path.Prepare(map, child.IsEvenOdd, state.Budget, token);
            if (prepared.Points.Length == 0) return false;
            long bytes = ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(prepared.Points.Length)
                + ParserCollectionMemory.GetArrayByteCount<int>(prepared.ContourEnds.Length)
                + ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(prepared.QuadSpans.Length)
                + ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(prepared.CubicSpans.Length)
                + (prepared.Signs is {} signs ? ParserCollectionMemory.GetArrayByteCount<int>(signs.Length) : 0);
            RenderBudget.BudgetReservation reservation = state.Budget.Reserve(bytes, SvgMemoryCategory.Scene);
            SvgGeometry geometry = default;
            try
            {
                geometry = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(Copy<SvgDevicePoint>(prepared.Points, token),
                    Copy<int>(prepared.ContourEnds, token), prepared.Signs is {} sourceSigns ? Copy<int>(sourceSigns, token) : null,
                    Copy<SvgQuadSpan>(prepared.QuadSpans, token), Copy<SvgCubicSpan>(prepared.CubicSpans, token),
                    new(0, 0, 0, child.SourceOpacity.Alpha), true, child.IsEvenOdd, SvgPolygonMapping.FiniteEdges, null, reservation, state.Budget));
                geometry = geometry with { AuthoredPath = child.PaintKind == SvgClipPaintKind.Rectangle
                    ? child.Path.CopyRectangleFill(map, state.Budget, token)
                    : child.Path.Copy(map, state.Budget, token) };
                if (child.Clip is {} clip)
                {
                    geometry = geometry with { LeafMask = SvgClipPathSyntax.BindResourceClip(clip.Id, map.ComposeChecked(clip.Map), null, state, token) };
                    hasChildClips = true;
                }
                if (resourceClipId is {} resourceId)
                {
                    // Native replay retains the resource clip in both the paint
                    // context and a clipped child's effect context. Their AA
                    // coverage applications must remain separate.
                    if (child.Clip is not null)
                        geometry = geometry with { LeafMask = SvgClipPathSyntax.BindResourceClip(resourceId, map, geometry.LeafMask, state, token) };
                    geometry = geometry with { LeafMask = SvgClipPathSyntax.BindResourceClip(resourceId, map, geometry.LeafMask, state, token) };
                    hasChildClips = true;
                }
                Span<SvgGeometry> source = [geometry];
                SvgPaintRecording.WrapInstance(source, child.InstanceOpacity, state.NewRecordingScope(), state, token);
                geometry = source[0];
                hasChildClips |= child.InstanceOpacity.Value != 1;
            }
            catch { geometry.Dispose(); reservation.Dispose(); throw; }
            builder.AddGeometry(geometry);
            return hasChildClips;
        }

        static SvgClipRegistration RegisterPicture(SvgClipContent definition, SvgClipSourceRange range, SvgAffineTransform map,
            SvgDocumentState state, SvgSceneBuilder scene, CancellationToken token)
        {
            using BudgetedParserList<SvgClipDependency> dependencies = SvgClipPathSyntax.OrderDependencies(range.Id, state, token);
            if (!map.TryInvert(out SvgAffineTransform inverse)) return new(new(0, 0, 0, 0), -1, -1);
            BudgetedParserList<SvgClipDefinitionSource> sources = state.ClipSources ?? throw new InvalidOperationException("Clip picture sources are missing.");
            bool needsBounds = HasPaint(sources, range, state, token);
            using SvgSceneBuilder builder = new(state.Budget, token);
            SvgPaintBounds? bounds = null;
            bool previousPicture = state.IsResourcePicture;
            SvgAffineTransform previousTextMap = state.TextRecordingMap;
            state.IsResourcePicture = true;
            try
            {
                state.TextRecordingMap = inverse;
                for (int index = range.First; index < range.First + range.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgClipDefinitionSource source = sources[index];
                    if (source.Kind == SvgClipDefinitionSourceKind.Geometry)
                    {
                        SvgClipContent geometry = source.Geometry;
                        if (geometry.IsEmpty) continue;
                        if (geometry.ResourceBounds is {} visual) Include(visual);
                        else if (geometry.SourcePath is {} path)
                        {
                            SvgRectangle box = SvgPathBounds.Compute(path.Commands, path.SceneMap, token);
                            Include(new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)));
                        }
                        if (geometry.Children is {} childrenRange)
                        {
                            BudgetedParserList<SvgClipChild> children = state.ClipChildren ?? throw new InvalidOperationException("Clip geometry children are missing.");
                            for (int child = childrenRange.First; child < childrenRange.First + childrenRange.Count; child++)
                                AppendGeometry(children[child], definition.ResourceClipId, map, builder, state, token);
                        }
                        else if (geometry.SourcePath is {} path)
                            AppendGeometry(new(path, geometry.IsEvenOdd, geometry.PaintKind, geometry.ChildClip) { SourceOpacity = geometry.SourceOpacity, InstanceOpacity = geometry.InstanceOpacity }, definition.ResourceClipId, map, builder, state, token);
                        continue;
                    }
                    SvgRetainedTextSource text;
                    SvgUseReference reference;
                    string? childClip;
                    if (source.Kind == SvgClipDefinitionSourceKind.Text)
                    {
                        SvgClipTextSource child = source.Text;
                        if (!child.Contributes) continue;
                        text = child.Source; childClip = child.ClipId;
                        SvgInheritedContainer inherited = child.Inherited;
                        reference = new(string.Empty, 0, 0, null, null, map, SvgAffineTransform.Identity, 255, true, null, null, null)
                        {
                            InheritedFill = SvgPaint.Default, InheritedTextStyle = inherited.TextStyle,
                            InheritedStroke = SvgInheritedStrokeStyle.Default, InheritedFillRule = inherited.FillRule,
                            ParentElementOpacity = inherited.Presentation.ElementOpacity, ElementParentDisplay = inherited.Presentation.ElementDisplay,
                            RecordingScope = state.NewRecordingScope(), LengthBases = child.LengthBases,
                            IsDisplayed = inherited.Presentation.IsDisplayed, IsVisible = inherited.Presentation.IsVisible,
                            AllowsSolidPaintAnalysis = true, TextPurpose = SvgTextPaintPurpose.ClipMask, TextMetrics = new(false, needsBounds)
                        };
                    }
                    else if (source.Kind == SvgClipDefinitionSourceKind.Use)
                    {
                        SvgResourceUse use = source.Use;
                        if (!use.Reference.IsDisplayed || !use.Reference.IsVisible || !needsBounds && use.Reference.SourceOpacity.Value == 0) continue;
                        if (!SvgReferenceResolver.TryGetDefinition(use.Reference, state.Definitions, out SvgDefinition target)
                            || target.TextSource is not {} retained) continue;
                        text = retained; childClip = use.ClipId;
                        reference = use.Reference with
                        {
                            Transform = map.ComposeChecked(use.Reference.Transform),
                            InheritedTransform = map.ComposeChecked(use.Reference.InheritedTransform),
                            TextPurpose = SvgTextPaintPurpose.ClipMask, PaintServerFill = null, PaintServerId = null,
                            StrokeOverride = null, Filter = null,
                            IsPainted = use.Reference.IsDisplayed && use.Reference.IsVisible, TextMetrics = new(false, needsBounds)
                        };
                    }
                    else continue;
                    using (SvgRetainedTextSource.Reader reader = text.OpenReader(0, token))
                    {
                        reader.MoveToContent();
                        SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, reader.StyleSheet, token, true)
                            .ResolveOpacity(reference.ElementParentOpacity).ResolveDisplay(reference.ElementParentDisplay);
                        if (!reference.IsDisplayed || style.GetDisplay() == SvgDisplay.None
                            || !SvgPresentationResolver.ParseVisibility(style.Visibility, reference.IsVisible)
                            || !needsBounds && style.GetOpacity().Value == 0) continue;
                    }
                    SvgGeometry[] glyphs = text.Lower(reference, null, state, token);
                    try
                    {
                        if (source.Kind == SvgClipDefinitionSourceKind.Use)
                            SvgPaintRecording.WrapInstance(glyphs, source.Use.Reference.SourceOpacity, source.Use.Reference.RecordingScope, state, token);
                        foreach (ref SvgGeometry glyph in glyphs.AsSpan())
                        {
                            token.ThrowIfCancellationRequested();
                            SvgRecordedPaintValue paint = state.Scene.GetRecordedPaintValue(glyph.RecordedPaint);
                            if (paint.SourceObjectBounds is {} objects)
                                Include(objects.Bounds.Map(inverse.ComposeChecked(objects.SceneMap), token));
                            else if (glyph.Kind == SvgGeometryKind.GlyphContours)
                            {
                                SvgRectangle box = Lokad.Svg.Fonts.SvgGlyphOutline.Bounds(glyph.Glyph.Contours, inverse, token);
                                Include(new((float)box.X, (float)box.Y, (float)(box.X + box.Width), (float)(box.Y + box.Height)));
                            }
                            if (childClip is not null && source.Kind == SvgClipDefinitionSourceKind.Use)
                                glyph = glyph with { LeafMask = SvgClipPathSyntax.BindResourceClip(childClip, SvgReferenceResolver.PlainInstancePlacement(reference, null), glyph.LeafMask, state, token) };
                            if (definition.ResourceClipId is {} parentId)
                            {
                                if (childClip is not null) glyph = glyph with { LeafMask = SvgClipPathSyntax.BindResourceClip(parentId, map, glyph.LeafMask, state, token) };
                                glyph = glyph with { LeafMask = SvgClipPathSyntax.BindResourceClip(parentId, map, glyph.LeafMask, state, token) };
                            }
                        }
                        SvgPaintRecording.AdoptLeafScene(new(glyphs, false, null, null), builder, state, token);
                    }
                    catch { foreach (SvgGeometry glyph in glyphs) glyph.Dispose(); throw; }
                }
                SvgLeafMaskComposition.Emit(state, builder, token);
                SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
                try { state.Scene.AdoptVectorContent(content); }
                catch { content.Dispose(); throw; }
                if (bounds is not {} visualBounds) return new(new(0, 0, 0, 0), -1, -1);
                SvgClipBox enclosure = new(Math.Floor(visualBounds.Left), Math.Floor(visualBounds.Top),
                    Math.Ceiling(visualBounds.Right) - Math.Floor(visualBounds.Left), Math.Ceiling(visualBounds.Bottom) - Math.Floor(visualBounds.Top));
                (SvgClipBox region, int boundsIndex) = new SvgAffineViewportClip(enclosure, map).Register(state, scene, token);
                int contentIndex = scene.AddMaskContent(new([], 255, true) { VectorContent = content });
                return new(region, contentIndex, boundsIndex);
            }
            finally { state.IsResourcePicture = previousPicture; state.TextRecordingMap = previousTextMap; }

            // An invisible child still contributes layout bounds to a painted
            // resource enclosure. An entirely inactive picture needs no glyphs.
            static bool HasPaint(BudgetedParserList<SvgClipDefinitionSource> sources, SvgClipSourceRange range,
                SvgDocumentState state, CancellationToken token)
            {
                for (int index = range.First; index < range.First + range.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgClipDefinitionSource source = sources[index];
                    if (source.Kind == SvgClipDefinitionSourceKind.Geometry)
                    {
                        SvgClipContent geometry = source.Geometry;
                        if (geometry.IsEmpty) continue;
                        if (geometry.Children is {} childrenRange)
                        {
                            BudgetedParserList<SvgClipChild> children = state.ClipChildren ?? throw new InvalidOperationException("Clip geometry children are missing.");
                            for (int child = childrenRange.First; child < childrenRange.First + childrenRange.Count; child++)
                            {
                                token.ThrowIfCancellationRequested();
                                if (children[child].SourceOpacity.Value != 0 && children[child].InstanceOpacity.Value != 0) return true;
                            }
                        }
                        else if (geometry.SourceOpacity.Value != 0 && geometry.InstanceOpacity.Value != 0) return true;
                        continue;
                    }
                    SvgRetainedTextSource text;
                    SvgPaintOpacity parentOpacity;
                    SvgDisplay parentDisplay;
                    bool visible;
                    if (source.Kind == SvgClipDefinitionSourceKind.Text)
                    {
                        if (!source.Text.Contributes) continue;
                        text = source.Text.Source; parentOpacity = source.Text.Inherited.Presentation.ElementOpacity;
                        parentDisplay = source.Text.Inherited.Presentation.ElementDisplay; visible = source.Text.Inherited.Presentation.IsVisible;
                    }
                    else if (source.Kind == SvgClipDefinitionSourceKind.Use)
                    {
                        SvgUseReference reference = source.Use.Reference;
                        if (!reference.IsDisplayed || !reference.IsVisible || reference.SourceOpacity.Value == 0
                            || !SvgReferenceResolver.TryGetDefinition(reference, state.Definitions, out SvgDefinition target)
                            || target.TextSource is not {} retained) continue;
                        text = retained; parentOpacity = reference.ElementParentOpacity;
                        parentDisplay = reference.ElementParentDisplay; visible = reference.IsVisible;
                    }
                    else continue;
                    using SvgRetainedTextSource.Reader reader = text.OpenReader(0, token); reader.MoveToContent();
                    SvgComputedStyle style = SvgComputedStyleResolver.Resolve(reader, reader.StyleSheet, token, true)
                        .ResolveOpacity(parentOpacity).ResolveDisplay(parentDisplay);
                    if (text.CharacterCount != 0 && style.GetDisplay() != SvgDisplay.None && style.GetOpacity().Value != 0
                        && SvgPresentationResolver.ParseVisibility(style.Visibility, visible)) return true;
                }
                return false;
            }

            void Include(SvgPaintBounds box)
                => bounds = bounds is {} value ? new(MathF.Min(value.Left, box.Left), MathF.Min(value.Top, box.Top),
                    MathF.Max(value.Right, box.Right), MathF.Max(value.Bottom, box.Bottom)) : box;
        }

        static SvgClipRegistration WithoutEnclosure((SvgClipBox Bounds, int ContentIndex) registration)
            => new(registration.Bounds, registration.ContentIndex, -1);

        static T[] Copy<T>(ReadOnlySpan<T> source, CancellationToken token) where T : struct
        {
            T[] output = new T[source.Length];
            for (int index = 0; index < source.Length; index++) { token.ThrowIfCancellationRequested(); output[index] = source[index]; }
            return output;
        }
    }
}
