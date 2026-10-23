namespace Lokad.Svg.Parsing;

using Lokad.Svg.Rendering;

/// <summary>Borrows a contiguous document-staging range of independently owned clip child paths.</summary>
internal readonly record struct SvgClipChildRange(int First, int Count);
internal readonly record struct SvgClipChild(SvgAuthoredPath Path, bool IsEvenOdd);

/// <summary>Retains one admitted clip-path polygon and its independent winding rule in root-user space.</summary>
/// <remarks>Points hold pre-device root-user coordinates. Every retained contour closes implicitly and has at least three vertices; ends partition the points exactly. Area-free short contours contribute no clip coverage. Compound paths replay directed winding or parity through the native clip scene, including holes, concavity, self-intersections and canceling coverage. Source commands remain borrowed from document staging.</remarks>
internal readonly record struct SvgClipPolygon(SvgDevicePoint[] Points, int[] ContourEnds)
{
    /// <summary>A display-suppressed resource contributes no binding, unlike an empty silhouette.</summary>
    internal bool IsDisabled { get; init; }
    /// <summary>Borrows immutable native path commands owned by the document's clip staging registry.</summary>
    internal SvgAuthoredPath? SourcePath { get; init; }
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
    internal SvgClipChildRange? Children { get; init; }
    internal bool UsesMaskFallback { get; init; }

    /// <summary>Registers either one resolved native path or ordered native mask-source replay.</summary>
    /// <remarks>Child ranges borrow document staging. Geometry/command copies belong to a sealed vector
    /// scene adopted by the parent; no staging owner escapes parsing. All material storage reserves,
    /// copy/replay visits charge work/token, and failures release partial builders and geometry.</remarks>
    internal (SvgClipBox Bounds, int ContentIndex) Register(SvgAffineTransform map, bool replaySource, SvgDocumentState state,
        SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        if (!UsesMaskFallback)
        {
            // Containment leaves one original rectangle. Keep object-box polygon
            // registration for admitted affine instances, but preserve rectangular
            // coverage rather than adding a generic path's enclosing clip.
            if (IsObjectBoundingBox && ChildCount > 1 && !UsesNativeReplay && ResourceBounds is {} rectangle)
                return new SvgAffineViewportClip(new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top), map)
                    .Register(state, scene, work, token);
            return SvgAffineViewportClip.RegisterContour(Points, ContourEnds, map,
                IsOval ? SvgClipContentKind.OvalClipPath : SvgClipContentKind.ClipPath,
                replaySource ? SourcePath : null, ResourceBounds, IsEvenOdd, state, scene, work, token);
        }
        SvgClipChildRange range = Children ?? throw new InvalidOperationException("Clip fallback requires original child sources.");
        BudgetedParserList<SvgClipChild> children = state.ClipChildren ?? throw new InvalidOperationException("Clip child sources are missing.");
        using SvgSceneBuilder builder = new(state.Budget, work, token);
        for (int index = range.First; index < range.First + range.Count; index++)
        {
            work.Consume(token);
            SvgClipChild child = children[index];
            using SvgPreparedPathContours prepared = child.Path.Prepare(map, child.IsEvenOdd, state.Budget, work, token);
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
                geometry = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(Copy<SvgDevicePoint>(prepared.Points, work, token),
                    Copy<int>(prepared.ContourEnds, work, token), prepared.Signs is {} sourceSigns ? Copy<int>(sourceSigns, work, token) : null,
                    Copy<SvgQuadSpan>(prepared.QuadSpans, work, token), Copy<SvgCubicSpan>(prepared.CubicSpans, work, token),
                    new(255, 255, 255, 255), true, child.IsEvenOdd, SvgPolygonMapping.FiniteEdges, null, reservation, state.Budget));
                geometry = geometry with { AuthoredPath = child.Path.Copy(map, state.Budget, work, token) };
            }
            catch { geometry.Dispose(); reservation.Dispose(); throw; }
            builder.AddGeometry(geometry);
        }
        SvgVectorContent content = SvgVectorContent.Create(builder.Build(), state.Budget);
        try { state.Scene.AdoptVectorContent(content); }
        catch { content.Dispose(); throw; }
        int contentIndex = scene.AddMaskContent(new([], 255, true) { VectorContent = content });
        SvgPaintBounds bounds = ResourceBounds ?? throw new InvalidOperationException("Clip fallback requires resource bounds.");
        return (map.Apply(new SvgClipBox(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)), contentIndex);

        static T[] Copy<T>(ReadOnlySpan<T> source, ParserWorkCounter work, CancellationToken token) where T : struct
        {
            T[] output = new T[source.Length];
            for (int index = 0; index < source.Length; index++) { work.Consume(token); output[index] = source[index]; }
            return output;
        }
    }
}
