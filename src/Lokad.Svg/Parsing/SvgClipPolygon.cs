namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Borrows a contiguous document-staging range of independently owned clip child paths.</summary>
internal readonly record struct SvgClipChildRange(int First, int Count);
internal enum SvgClipPaintKind : byte { Path, Rectangle }
internal readonly record struct SvgClipChild(SvgAuthoredPath Path, bool IsEvenOdd, SvgClipPaintKind PaintKind);

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
/// <remarks>Points hold pre-device root-user coordinates. Every retained contour closes implicitly and has at least three vertices; ends partition the points exactly. Area-free short contours contribute no clip coverage. Compound paths replay directed winding or parity through the native clip scene, including holes, concavity, self-intersections and canceling coverage. Source commands remain borrowed from document staging.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>A display-suppressed resource contributes no binding, unlike an empty silhouette.</summary>
    internal bool IsDisabled { get; init; }
    /// <summary>Borrows immutable native path commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
    /// <summary>Retains the authored child paint operation independently of its clipping path.</summary>
    internal SvgClipPaintKind PaintKind { get; init; }
    internal bool IsEvenOdd { get; init; }
    /// <summary>Selects authored replay for curves, compound contours and nontrivial unions; contained rectangles retain their primitive coverage.</summary>
    internal bool UsesNativeReplay { get; init; }
    /// <summary>Retains the native oval clip identity when its child map preserves axis-aligned oval bounds.</summary>
    internal bool IsOval { get; init; }
    /// <summary>Maps normalized content through the referencing element's geometry bounds before native replay.</summary>
    internal bool IsObjectBoundingBox { get; init; }
    /// <summary>Borrows the local clip applied to this resource's child geometry.</summary>
    internal string? NestedClipId { get; init; }
    /// <summary>Preserves the union of mapped child layout boxes independently of path union geometry.</summary>
    internal SvgPaintBounds? ResourceBounds { get; init; }
    /// <summary>Counts authored children even when containment leaves one effective rectangle.</summary>
    internal int ChildCount { get; init; } = 1;
    /// <summary>Identifies an already resolved native boundary whose curve verbs must survive registration.</summary>
    internal bool IsResolvedUnion { get; init; }
    /// <summary>Original children are retained; their ordered union is resolved once when the definition closes.</summary>
    internal bool DeferredUnion { get; init; }
    internal SvgClipChildRange? Children { get; init; }
    internal bool UsesMaskFallback { get; init; }

    /// <summary>Registers either one resolved native path or ordered native mask-source replay.</summary>
    /// <remarks>Child ranges borrow document staging. Geometry/command copies belong to a sealed vector
    /// scene adopted by the parent; no staging owner escapes parsing. All material storage reserves,
    /// copy/replay visits observe cancellation token, and failures release partial builders and geometry.</remarks>
    internal SvgClipRegistration Register(SvgAffineTransform map, bool replaySource, SvgDocumentState state,
        SvgSceneBuilder scene, CancellationToken token)
    {
        if (!UsesMaskFallback)
        {
            // Containment leaves one original rectangle. Keep object-box polygon
            // registration for admitted affine instances, but preserve rectangular
            // coverage rather than adding a generic path's enclosing clip.
            if (IsObjectBoundingBox && ChildCount > 1 && !UsesNativeReplay && ResourceBounds is {} rectangle)
                return WithoutEnclosure(new SvgAffineViewportClip(new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top), map)
                    .Register(state, scene, token));
            return WithoutEnclosure(SvgAffineViewportClip.RegisterContour(Points, ContourEnds, map,
                IsOval ? SvgClipContentKind.OvalClipPath : SvgClipContentKind.ClipPath,
                replaySource ? SourcePath : null, ResourceBounds, IsEvenOdd, state, scene, token));
        }
        SvgClipChildRange range = Children ?? throw new InvalidOperationException("Clip fallback requires original child sources.");
        BudgetedParserList<SvgClipChild> children = state.ClipChildren ?? throw new InvalidOperationException("Clip child sources are missing.");
        using SvgSceneBuilder builder = new(state.Budget, token);
        for (int index = range.First; index < range.First + range.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgClipChild child = children[index];
            using SvgPreparedPathContours prepared = child.Path.Prepare(map, child.IsEvenOdd, state.Budget, token);
            if (prepared.Points.Length == 0) continue;
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
                    new(255, 255, 255, 255), true, child.IsEvenOdd, SvgPolygonMapping.FiniteEdges, null, reservation, state.Budget));
                geometry = geometry with { AuthoredPath = child.PaintKind == SvgClipPaintKind.Rectangle
                    ? child.Path.CopyRectangleFill(map, state.Budget, token)
                    : child.Path.Copy(map, state.Budget, token) };
            }
            catch { geometry.Dispose(); reservation.Dispose(); throw; }
            builder.AddGeometry(geometry);
        }
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        SvgPaintBounds bounds = ResourceBounds ?? throw new InvalidOperationException("Clip fallback requires resource bounds.");
        SvgClipBox enclosure = new(Math.Floor(bounds.Left), Math.Floor(bounds.Top), Math.Ceiling(bounds.Right) - Math.Floor(bounds.Left), Math.Ceiling(bounds.Bottom) - Math.Floor(bounds.Top));
        (SvgClipBox region, int boundsIndex) = new SvgAffineViewportClip(enclosure, map).Register(state, scene, token);
        int contentIndex = scene.AddMaskContent(new([], 255, true)
            { VectorContent = content });
        return new(region, contentIndex, boundsIndex);

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
