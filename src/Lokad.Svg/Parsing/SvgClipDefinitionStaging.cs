using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Stages direct and referenced clip geometry through the same ordered union.</summary>
/// <remarks>Paths remain document-owned until final registration. Appending consumes the incoming
/// source owner, including on failure. Every construction and copy observes cancellation and reserves
/// storage before allocation; parser state and its borrowed ranges belong to one conversion thread.</remarks>
internal static class SvgClipDefinitionStaging
{
    internal static void AppendText(SvgClipTextSource text, SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        BudgetedParserList<SvgClipDefinitionSource> sources = state.ClipSources ??= new(state.Budget, SvgMemoryCategory.Parser, token);
        if (state.ClipSourceStart < 0) state.ClipSourceStart = sources.Count;
        RetainGeometry(state, sources, token);
        sources.Add(new(text));
    }

    internal static void AppendUse(SvgResourceUse use, SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        BudgetedParserList<SvgClipDefinitionSource> sources = state.ClipSources ??= new(state.Budget, SvgMemoryCategory.Parser, token);
        if (state.ClipSourceStart < 0) state.ClipSourceStart = sources.Count;
        RetainGeometry(state, sources, token);
        sources.Add(new(use));
    }

    internal static void RegisterDeferred(SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!state.ClipIsDisplayed) { SvgDefinitionParser.RegisterClipBox(state, token); return; }
        BudgetedParserList<SvgClipDefinitionSource> sources = state.ClipSources ?? throw new InvalidOperationException("Deferred clip sources are missing.");
        RetainGeometry(state, sources, token);
        string id = state.Strings.Copy(state.ClipPathId ?? throw new InvalidOperationException("Clip registration requires an identifier."));
        if (state.Clips.TryGetValue(id, out _) || state.ObjectClips.TryGetValue(id, out _) || !state.ClipContents.TryAdd(id, new([], [])))
            throw state.ClipPathIdSource.DuplicateId("SVG clip-path ids must be unique.");
        BudgetedParserList<SvgDeferredClipDefinition> definitions = state.DeferredClips ??= new(state.Budget, SvgMemoryCategory.Parser, token);
        definitions.Add(new(id, state.ClipSourceStart, sources.Count - state.ClipSourceStart, state.ClipIsObjectBoundingBox, state.ClipResourceClipId));
    }

    private static void RetainGeometry(SvgDocumentState state, BudgetedParserList<SvgClipDefinitionSource> sources, CancellationToken token)
    {
        PromoteClipRectangle(state, token);
        if (state.ClipPolygon is not {} polygon) return;
        sources.Add(new(polygon));
        // Successful insertion transfers the path; failure leaves the parser's owner intact.
        state.ClipPolygon = null;
        state.ClipBox = null;
        state.ClipRectangleSource = null;
    }

    // Keep single rectangle resources on their established box route. Only
    // a sibling materializes native commands, retaining the child's own map.
    internal static void PromoteClipRectangle(SvgDocumentState state, CancellationToken token)
    {
        if (state.ClipBox is null) return;
        if (state.ClipPolygon is null && state.ClipRectangleSource is {} source)
        {
            SvgClipContent polygon = CreateClipRectangle(source.Geometry, state, token);
            try { state.ClipPolygon = polygon.WithVisualBounds(source.BoundsInflation, token) with { SourceOpacity = source.SourceOpacity }; }
            catch { polygon.SourcePath?.Dispose(); throw; }
        }
        state.ClipRectangleSource = null;
        state.ClipBox = null;
    }

    internal static SvgClipContent CreateClipRectangle(SvgAffineViewportClip source, SvgDocumentState state, CancellationToken token)
    {
        SvgClipBox box = source.Box;
        ReadOnlySpan<SvgDevicePoint> corners = stackalloc SvgDevicePoint[]
        {
            new((float)box.X, (float)box.Y), new((float)box.Right, (float)box.Y),
            new((float)box.Right, (float)box.Bottom), new((float)box.X, (float)box.Bottom)
        };
        state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4)
            + ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Scene));
        SvgDevicePoint[] points = new SvgDevicePoint[4];
        int[] ends = [4];
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(state.Budget, SvgMemoryCategory.Scene, token);
        for (int index = 0; index < corners.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            SvgDevicePoint point = corners[index];
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new SvgException(SvgErrorCode.LimitExceeded, "Native clip rectangle coordinates exceed the finite float range.");
            points[index] = source.Map.Apply(point.X, point.Y);
            commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line,
                SvgAuthoredArcFlags.None, point, default, default));
        }
        commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        SvgAuthoredPath path;
        try { path = SvgAuthoredPath.Create(owned, source.Map, state.Budget); }
        catch { owned.Dispose(); throw; }
        return new(points, ends) { SourcePath = path, PaintKind = SvgClipPaintKind.Rectangle, IsObjectBoundingBox = state.ClipIsObjectBoundingBox };
    }

    // Rounded rectangles and ovals share one native source/staging owner.
    // Resource units and nested intersections bind later in the final scene.
    internal static SvgClipContent CreateClipSource(OwnedParserArray<SvgAuthoredPathCommand> owned, SvgAffineTransform map,
        bool oval, string? nestedClip, SvgDocumentState state, CancellationToken token)
    {
        SvgAuthoredPath source;
        try { source = SvgAuthoredPath.CreateConvexShape(owned, map, state.Budget); }
        catch { owned.Dispose(); throw; }
        try
        {
            using SvgPreparedPathContours prepared = source.Prepare(SvgAffineTransform.Identity, false, state.Budget, token);
            state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(prepared.Points.Length)
                + ParserCollectionMemory.GetArrayByteCount<int>(prepared.ContourEnds.Length), SvgMemoryCategory.Scene));
            SvgDevicePoint[] points = new SvgDevicePoint[prepared.Points.Length];
            int[] ends = new int[prepared.ContourEnds.Length];
            for (int index = 0; index < points.Length; index++) { token.ThrowIfCancellationRequested(); points[index] = prepared.Points[index]; }
            for (int index = 0; index < ends.Length; index++) { token.ThrowIfCancellationRequested(); ends[index] = prepared.ContourEnds[index]; }
            return new(points, ends) { SourcePath = source, UsesNativeReplay = true, IsOval = oval,
                IsObjectBoundingBox = state.ClipIsObjectBoundingBox, ChildClip = nestedClip is null ? null : new(nestedClip, map) };
        }
        catch { source.Dispose(); throw; }
    }

    // Adopt each source once; union failure retains the first staging owner
    // and releases the incoming owner. Publish before cancellable classification.
    internal static void AppendClipChild(SvgClipContent child, float boundsInflation, SvgDocumentState state, CancellationToken token)
    {
        try { child = child.WithVisualBounds(boundsInflation, token); }
        catch { child.SourcePath?.Dispose(); throw; }
        if (child.Points.Length == 0 && state.ClipPolygon is {} painted)
        {
            child.SourcePath?.Dispose();
            if (!painted.HasIntersections && !painted.UsesNativeReplay && !painted.UsesMaskFallback
                && (!painted.IsObjectBoundingBox || painted.ChildCount == 1) && painted.ContourEnds.Length == 1
                && TryGetAxisClipBox(painted.Points, token, out SvgClipBox box))
                state.ClipBox = box;
            return;
        }
        if (state.ClipPolygon is { Points.Length: 0 } empty)
        {
            empty.SourcePath?.Dispose(); state.ClipPolygon = null; state.ClipBox = null;
        }
        if (state.ClipPolygon is {} first)
        {
            SvgClipContent combined;
            try { combined = CombineClipChildren(first, child, state, token); }
            finally { child.SourcePath?.Dispose(); }
            state.ClipPolygon = combined;
            first.SourcePath?.Dispose();
            child = combined;
        }
        else state.ClipPolygon = child;
        state.ClipBox = child.Points.Length == 0 ? new(0, 0, 0, 0)
            : !child.HasIntersections && !child.UsesNativeReplay && !child.UsesMaskFallback
                && (!child.IsObjectBoundingBox || child.ChildCount == 1) && child.ContourEnds.Length == 1
                && TryGetAxisClipBox(child.Points, token, out SvgClipBox clipRect) ? clipRect : null;

        // Retain original child owners and layout bounds. Containment keeps its
        // primitive route; other unions resolve once when the definition closes.
        // The child threshold selects painted replay before any boundary solver.
        static SvgClipContent CombineClipChildren(SvgClipContent first, SvgClipContent next, SvgDocumentState state,
            CancellationToken token)
        {
            BudgetedParserList<SvgClipChild> originals = state.ClipChildren ??= new(state.Budget, SvgMemoryCategory.Scene, token);
            SvgClipChildRange range;
            if (first.Children is {} sourceRange)
                range = sourceRange.First + sourceRange.Count == originals.Count ? sourceRange
                    : RetainRange(sourceRange, originals, state.Budget, token);
            else
            {
                range = new(originals.Count, 1);
                Retain(first.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands."), first.IsEvenOdd, first.PaintKind, first.ChildClip, first.SourceOpacity, first.InstanceOpacity, originals, state.Budget, token);
            }
            if (next.Children is {} nextRange) RetainRange(nextRange, originals, state.Budget, token);
            else Retain(next.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands."),
                next.IsEvenOdd, next.PaintKind, next.ChildClip, next.SourceOpacity, next.InstanceOpacity, originals, state.Budget, token);
            int childCount = checked(first.ChildCount + next.ChildCount);
            range = range with { Count = checked(range.Count + (next.Children?.Count ?? 1)) };
            if (childCount > 42 || first.UsesMaskFallback || next.UsesMaskFallback || first.ChildClip is not null || next.ChildClip is not null)
            {
                SvgPaintBounds a = ChildBounds(first, first.SourcePath, token);
                SvgPaintBounds b = ChildBounds(next, next.SourcePath, token);
                return first with { SourcePath = null, Children = range, UsesMaskFallback = true, IsResolvedUnion = true,
                    ChildCount = childCount, IsOval = false, ChildClip = null,
                    ResourceBounds = new(MathF.Min(a.Left, b.Left), MathF.Min(a.Top, b.Top), MathF.Max(a.Right, b.Right), MathF.Max(a.Bottom, b.Bottom)) };
            }
            SvgAuthoredPath firstPath = first.SourcePath ?? throw new InvalidOperationException("Clip children require retained source commands.");
            SvgPaintBounds firstBounds = ChildBounds(first, firstPath, token);
            SvgPaintBounds nextBounds = ChildBounds(next, next.SourcePath, token);
            // Containment preserves the original rectangle clip. Rebuilding it as
            // a path introduces a separate enclosing clip at fractional edges.
            // Keep authored children/counts for the bounded fallback and source
            // bounds for later siblings, while copying the selected path owner.
            if (!first.UsesNativeReplay && !next.UsesNativeReplay && first.ContourEnds.Length == 1 && next.ContourEnds.Length == 1
                && TryGetAxisClipBox(first.Points, token, out SvgClipBox firstBox)
                && TryGetAxisClipBox(next.Points, token, out SvgClipBox nextBox))
            {
                SvgClipContent? outer = firstBox.Contains(nextBox.X, nextBox.Y, nextBox.Right, nextBox.Bottom) ? first
                    : nextBox.Contains(firstBox.X, firstBox.Y, firstBox.Right, firstBox.Bottom) ? next : null;
                if (outer is {} containing)
                {
                    SvgAuthoredPath original = containing.SourcePath ?? throw new InvalidOperationException("A containing clip rectangle requires source commands.");
                    SvgPaintBounds bounds = new(MathF.Min(firstBounds.Left, nextBounds.Left), MathF.Min(firstBounds.Top, nextBounds.Top),
                        MathF.Max(firstBounds.Right, nextBounds.Right), MathF.Max(firstBounds.Bottom, nextBounds.Bottom));
                    return containing with { SourcePath = original.Copy(SvgAffineTransform.Identity, state.Budget, token),
                        Children = range, ChildCount = childCount, ResourceBounds = bounds };
                }
            }
            return first with { SourcePath = firstPath.Copy(SvgAffineTransform.Identity, state.Budget, token),
                Children = range, UsesNativeReplay = true, IsResolvedUnion = false, DeferredUnion = true,
                ChildCount = childCount, IsOval = false, PaintKind = SvgClipPaintKind.Path,
                ResourceBounds = new(MathF.Min(firstBounds.Left, nextBounds.Left), MathF.Min(firstBounds.Top, nextBounds.Top),
                    MathF.Max(firstBounds.Right, nextBounds.Right), MathF.Max(firstBounds.Bottom, nextBounds.Bottom)) };

            static void Retain(SvgAuthoredPath path, bool isEvenOdd, SvgClipPaintKind paintKind, SvgClipIntersection? clip, SvgPaintOpacity sourceOpacity, SvgPaintOpacity instanceOpacity, BudgetedParserList<SvgClipChild> originals,
                RenderBudget budget, CancellationToken token)
            {
                SvgAuthoredPath copy = path.Copy(SvgAffineTransform.Identity, budget, token);
                try { originals.Add(new(copy, isEvenOdd, paintKind, clip) { SourceOpacity = sourceOpacity, InstanceOpacity = instanceOpacity }); }
                catch { copy.Dispose(); throw; }
            }

            static SvgClipChildRange RetainRange(SvgClipChildRange source, BudgetedParserList<SvgClipChild> originals,
                RenderBudget budget, CancellationToken token)
            {
                int first = originals.Count;
                for (int index = source.First; index < source.First + source.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgClipChild child = originals[index];
                    Retain(child.Path, child.IsEvenOdd, child.PaintKind, child.Clip, child.SourceOpacity, child.InstanceOpacity, originals, budget, token);
                }
                return new(first, source.Count);
            }

            static SvgPaintBounds ChildBounds(SvgClipContent polygon, SvgAuthoredPath? path, CancellationToken token)
            {
                if (polygon.ResourceBounds is {} bounds) return bounds;
                if (path is null) throw new InvalidOperationException("A clip union requires source bounds or commands.");
                SvgRectangle tight = SvgPathBounds.Compute(path.Commands, token);
                return new SvgPaintBounds((float)tight.X, (float)tight.Y, (float)(tight.X + tight.Width), (float)(tight.Y + tight.Height)).Map(path.SceneMap, token);
            }
        }

    }

    private static bool TryGetAxisClipBox(SvgDevicePoint[] ring, CancellationToken cancellationToken, out SvgClipBox box)
    {
        box = default;
        if (ring.Length != 4) return false;
        double minX = Math.Min(Math.Min(ring[0].X, ring[1].X), Math.Min(ring[2].X, ring[3].X));
        double maxX = Math.Max(Math.Max(ring[0].X, ring[1].X), Math.Max(ring[2].X, ring[3].X));
        double minY = Math.Min(Math.Min(ring[0].Y, ring[1].Y), Math.Min(ring[2].Y, ring[3].Y));
        double maxY = Math.Max(Math.Max(ring[0].Y, ring[1].Y), Math.Max(ring[2].Y, ring[3].Y));
        cancellationToken.ThrowIfCancellationRequested();
        if (!(maxX > minX) || !(maxY > minY)) return false;
        for (int index = 0; index < 4; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool onX = ring[index].X == minX || ring[index].X == maxX;
            bool onY = ring[index].Y == minY || ring[index].Y == maxY;
            if (!onX || !onY) return false;
            SvgDevicePoint next = ring[(index + 1) % 4];
            if (ring[index].X != next.X && ring[index].Y != next.Y) return false;
        }
        box = new SvgClipBox(minX, minY, maxX - minX, maxY - minY);
        return true;
    }

    /// <summary>Resolves path-representable whole-resource intersections after forward sources are available.</summary>
    /// <remarks>Registered source paths remain owned until a replacement is published. Dependency scratch
    /// and temporary rectangles release on failure; completed replacements stay document-owned. All
    /// boundary work reserves before allocation, observes cancellation and is conversion-local.</remarks>
    internal static void ResolveIntersections(SvgDocumentState state, CancellationToken token)
    {
        using BudgetedParserList<SvgClipDependency> roots = new(state.Budget, SvgMemoryCategory.Parser, token);
        foreach (KeyValuePair<string, SvgClipContent> entry in state.ClipContents.Entries)
            if (!entry.Value.IsDisabled && entry.Value.ResourceClipId is not null) roots.Add(new(entry.Key));
        foreach (SvgClipDependency root in roots.AsSpan())
        {
            using BudgetedParserList<SvgClipDependency> order = SvgClipPathSyntax.OrderDependencies(root.Id, state, token);
            foreach (SvgClipDependency dependency in order.AsSpan())
            {
                token.ThrowIfCancellationRequested();
                string id = dependency.Id;
                if (!state.ClipContents.TryGetValue(id, out SvgClipContent body) || body.ResourceClipId is not {} parentId) continue;
                if (SvgClipPathSyntax.IsDisabled(parentId, state))
                {
                    state.ClipContents.Replace(id, body with { ResourceClipId = null });
                    continue;
                }
                if (body.ChildClip is not null || body.UsesMaskFallback || body.IsObjectBoundingBox || body.SourcePath is not {} source) continue;
                SvgAuthoredPath? temporary = null;
                try
                {
                    SvgAuthoredPath parent;
                    bool parity = false;
                    if (state.Clips.TryGetValue(parentId, out SvgClipBox box)) parent = temporary = Rectangle(box, state.Budget, token);
                    else if (state.ClipContents.TryGetValue(parentId, out SvgClipContent content))
                    {
                        if (content.HasIntersections || content.UsesMaskFallback || content.IsObjectBoundingBox) continue;
                        if (content.SourcePath is {} path) parent = path;
                        else if (content.Points.Length == 0) parent = temporary = Rectangle(new(0, 0, 0, 0), state.Budget, token);
                        else throw new InvalidOperationException("A clip boundary requires source commands.");
                        parity = content.IsEvenOdd;
                    }
                    else continue; // Existing binding admission reports unresolved or unsupported resource kinds.
                    SvgAuthoredPath resolved = SvgClipUnion.ResolveNativeIntersection(source, body.IsEvenOdd, parent, parity, state.Budget, token);
                    try
                    {
                        SvgClipContent replacement = CaptureBoundary(body, resolved, true, state, token) with
                            { ResourceClipId = null, IsOval = false, PaintKind = SvgClipPaintKind.Path };
                        // Empty silhouettes use the same zero-box binding as
                        // empty direct/deferred definitions; they have no path
                        // to register with the native contour constructor.
                        if (replacement.Points.Length == 0) state.Clips.TryAdd(id, new(0, 0, 0, 0));
                        state.ClipContents.Replace(id, replacement);
                        source.Dispose();
                    }
                    catch { resolved.Dispose(); throw; }
                }
                finally { temporary?.Dispose(); }
            }
        }

        static SvgAuthoredPath Rectangle(SvgClipBox box, RenderBudget budget, CancellationToken token)
        {
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, token);
            if (box.Width > 0 && box.Height > 0)
            {
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new((float)box.X, (float)box.Y), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new((float)box.Right, (float)box.Y), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new((float)box.Right, (float)box.Bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new((float)box.X, (float)box.Bottom), default, default));
                commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            }
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
            catch { owned.Dispose(); throw; }
        }
    }

    // Borrows both the old value and incoming path. The caller publishes the
    // returned replacement before releasing the old owner, and owns failure cleanup.
    internal static SvgClipContent CaptureBoundary(SvgClipContent polygon, SvgAuthoredPath source, bool parity,
        SvgDocumentState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source.Commands.Length == 0) return polygon with { Points = [], ContourEnds = [], SourcePath = source, IsEvenOdd = parity,
            IsResolvedUnion = true, DeferredUnion = false, UsesNativeReplay = true };
        using SvgPreparedPathContours prepared = source.Prepare(SvgAffineTransform.Identity, parity, state.Budget, token);
        state.DefinitionReservations.Add(state.Budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(prepared.Points.Length)
            + ParserCollectionMemory.GetArrayByteCount<int>(prepared.ContourEnds.Length), SvgMemoryCategory.Scene));
        SvgDevicePoint[] points = new SvgDevicePoint[prepared.Points.Length];
        int[] ends = new int[prepared.ContourEnds.Length];
        for (int index = 0; index < points.Length; index++) { token.ThrowIfCancellationRequested(); points[index] = prepared.Points[index]; }
        for (int index = 0; index < ends.Length; index++) { token.ThrowIfCancellationRequested(); ends[index] = prepared.ContourEnds[index]; }
        return polygon with { Points = points, ContourEnds = ends, SourcePath = source, IsEvenOdd = parity,
            IsResolvedUnion = true, DeferredUnion = false, UsesNativeReplay = true };
    }

    internal static SvgClipContent Complete(SvgDocumentState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SvgClipContent polygon = state.ClipPolygon ?? throw new InvalidOperationException("Clip polygon registration requires retained points.");
        polygon = polygon with { IsDisabled = !state.ClipIsDisplayed, ResourceClipId = state.ClipResourceClipId };
        if (!polygon.IsDisabled && polygon.DeferredUnion && !polygon.UsesMaskFallback)
        {
            SvgClipChildRange range = polygon.Children ?? throw new InvalidOperationException("Deferred union children are missing.");
            BudgetedParserList<SvgClipChild> children = state.ClipChildren ?? throw new InvalidOperationException("Deferred union owners are missing.");
            SvgAuthoredPath previous = polygon.SourcePath ?? throw new InvalidOperationException("Deferred source owner is missing.");
            SvgClipUnionResult resolved = SvgClipUnion.ResolveOrdered(children.AsSpan().Slice(range.First, range.Count), state.Budget, cancellationToken);
            try
            {
                polygon = CaptureBoundary(polygon, resolved.Path, resolved.IsEvenOdd, state, cancellationToken);
                state.ClipPolygon = polygon;
                previous.Dispose();
            }
            catch { resolved.Path.Dispose(); throw; }
        }
        else if (!polygon.IsDisabled && polygon.ChildCount > 1 && !polygon.IsResolvedUnion)
        {
            SvgAuthoredPath previous = polygon.SourcePath ?? throw new InvalidOperationException("A child union requires source commands.");
            SvgAuthoredPath normalized = NormalizeUnion(polygon, state.Budget, cancellationToken);
            polygon = polygon with { SourcePath = normalized };
            // Publish ownership before releasing the old path or registering the
            // id, so duplicate ids and cancellation retain one cleanup owner.
            state.ClipPolygon = polygon;
            previous.Dispose();
        }
        return polygon;

        // Preserve the qualified contour ordering and cyclic starts. Same-directed
        // nonzero contours represent the union of overlapping convex children;
        // disjoint content retains its established parity replay.
        static SvgAuthoredPath NormalizeUnion(SvgClipContent polygon, RenderBudget budget, CancellationToken token)
        {
            if (SvgClipUnion.ResolveCrossings(polygon, budget, token) is {} resolved) return resolved;
            int count = polygon.ContourEnds.Length;
            if (count > 42) throw new InvalidOperationException("A retained union exceeded its admitted child limit.");
            Span<int> order = stackalloc int[42];
            Span<SvgPaintBounds> bounds = stackalloc SvgPaintBounds[42];
            int start = 0;
            for (int contour = 0; contour < count; contour++)
            {
                float left = float.PositiveInfinity, top = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.NegativeInfinity;
                int end = polygon.ContourEnds[contour];
                for (int index = start; index < end; index++)
                {
                    token.ThrowIfCancellationRequested();
                    SvgDevicePoint point = polygon.Points[index];
                    left = MathF.Min(left, (float)point.X); top = MathF.Min(top, (float)point.Y);
                    right = MathF.Max(right, (float)point.X); bottom = MathF.Max(bottom, (float)point.Y);
                }
                bounds[contour] = new(left, top, right, bottom);
                int position = contour;
                while (position > 0)
                {
                    token.ThrowIfCancellationRequested();
                    SvgPaintBounds preceding = bounds[order[position - 1]];
                    if (preceding.Top < top || (preceding.Top == top && preceding.Left <= left)) break;
                    order[position] = order[position - 1]; position--;
                }
                order[position] = contour;
                start = end;
            }
            using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, token);
            for (int position = 0; position < count; position++)
            {
                token.ThrowIfCancellationRequested();
                int contour = order[position];
                start = contour == 0 ? 0 : polygon.ContourEnds[contour - 1];
                int end = polygon.ContourEnds[contour];
                while (end > start && polygon.Points[end - 1] == polygon.Points[start]) { token.ThrowIfCancellationRequested(); end--; }
                int next = start + 1;
                while (next < end && polygon.Points[next] == polygon.Points[start]) { token.ThrowIfCancellationRequested(); next++; }
                if (next >= end) throw new InvalidOperationException("A retained union contour became degenerate.");
                SvgDevicePoint first = polygon.Points[next];
                commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, first, default, default));
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, polygon.Points[start], default, default));
                for (int index = end - 1; index > next; index--)
                {
                    token.ThrowIfCancellationRequested();
                    if (polygon.Points[index] != polygon.Points[index + 1 < end ? index + 1 : start])
                        commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, polygon.Points[index], default, default));
                }
                commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, first, default, default));
                commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            }
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { return SvgAuthoredPath.Create(owned, SvgAffineTransform.Identity, budget); }
            catch { owned.Dispose(); throw; }
        }
    }

}
