using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgClipContentKind { AffineViewport, ClipPath, OvalClipPath, BinaryViewport, BinaryExclusion }

/// <summary>Retains a rectangular viewport, optional exclusions and its composed map until clip command emission.</summary>
/// <remarks>Placement borrows exclusion storage. Emission reserves source contours and registers a copy
/// with the existing mask-content scene owner; failures release temporary contours and commands.</remarks>
internal readonly record struct SvgAffineViewportClip(SvgClipBox Box, SvgAffineTransform Map)
{
    internal bool IsBinary { get; init; }
    internal SvgViewportDrawMap? DrawMap { get; init; }
    internal bool IsExclusion { get; init; }
    /// <summary>Borrows disjoint rectangular exclusions until the parser emits one bounded compound clip.</summary>
    internal ReadOnlyMemory<SvgClipBox> ExcludedBoxes { get; init; }
    internal (SvgClipBox Bounds, int ContentIndex) Register(SvgDocumentState state, SvgSceneBuilder scene, CancellationToken token)
    {
        if (!ExcludedBoxes.IsEmpty)
        {
            using BudgetedParserList<SvgDevicePoint> vertices = new(state.Budget, SvgMemoryCategory.Geometry, token);
            using BudgetedParserList<int> ends = new(state.Budget, SvgMemoryCategory.Geometry, token);
            if (!IsExclusion) Append(Box, vertices, ends);
            foreach (SvgClipBox hole in ExcludedBoxes.Span) Append(hole, vertices, ends);
            return RegisterContour(vertices.AsSpan(), ends.AsSpan(), Map, IsExclusion ? SvgClipContentKind.BinaryExclusion : IsBinary ? SvgClipContentKind.BinaryViewport : SvgClipContentKind.AffineViewport, null, null, true, state, scene, token, DrawMap);
        }
        float left = (float)Box.X, top = (float)Box.Y, right = (float)(Box.X + Box.Width), bottom = (float)(Box.Y + Box.Height);
        // Native rectangles start at top-left. A clipped closing edge must remain adjacent
        // to the preceding vertical piece so the existing edge builder can combine them.
        ReadOnlySpan<SvgDevicePoint> points = stackalloc SvgDevicePoint[] { new(left, top), new(right, top), new(right, bottom), new(left, bottom) };
        return RegisterContour(points, [points.Length], Map, IsBinary ? SvgClipContentKind.BinaryViewport : SvgClipContentKind.AffineViewport, null, null, false, state, scene, token, DrawMap);

        static void Append(SvgClipBox box, BudgetedParserList<SvgDevicePoint> vertices, BudgetedParserList<int> ends)
        {
            float left = (float)box.X, top = (float)box.Y, right = (float)box.Right, bottom = (float)box.Bottom;
            vertices.Add(new(left, bottom)); vertices.Add(new(left, top)); vertices.Add(new(right, top)); vertices.Add(new(right, bottom));
            ends.Add(vertices.Count);
        }
    }

    /// <summary>Registers borrowed source contours with the shared native clip-content owner.</summary>
    /// <remarks>Callers validate contour admission. Commands and arrays reserve before allocation;
    /// mapping/copying observes caller work and cancellation. The scene owns a distinct copy after
    /// success; failure releases all temporary storage. Generic path clips materialize child
    /// coordinates before reference placement, retaining curves and raw classification.</remarks>
    internal static (SvgClipBox Bounds, int ContentIndex) RegisterContour(ReadOnlySpan<SvgDevicePoint> source, ReadOnlySpan<int> sourceEnds,
        SvgAffineTransform map, SvgClipContentKind kind, SvgAuthoredPath? sourcePath, SvgPaintBounds? resourceBounds, bool isEvenOdd, SvgDocumentState state, SvgSceneBuilder scene, CancellationToken token)
        => RegisterContour(source, sourceEnds, map, kind, sourcePath, resourceBounds, isEvenOdd, state, scene, token, null);

    internal static (SvgClipBox Bounds, int ContentIndex) RegisterContour(ReadOnlySpan<SvgDevicePoint> source, ReadOnlySpan<int> sourceEnds,
        SvgAffineTransform map, SvgClipContentKind kind, SvgAuthoredPath? sourcePath, SvgPaintBounds? resourceBounds, bool isEvenOdd, SvgDocumentState state, SvgSceneBuilder scene, CancellationToken token, SvgViewportDrawMap? drawMap)
    {
        if (source.Length < 3) throw new ArgumentOutOfRangeException(nameof(source));
        if (sourceEnds.IsEmpty || sourceEnds[^1] != source.Length) throw new ArgumentOutOfRangeException(nameof(sourceEnds));
        RenderBudget budget = state.Budget;
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, token);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Length) + (isEvenOdd ? 2 : 1) * ParserCollectionMemory.GetArrayByteCount<int>(sourceEnds.Length), SvgMemoryCategory.Scene);
        SvgGeometry geometry = default;
        double x1 = double.PositiveInfinity, y1 = double.PositiveInfinity, x2 = double.NegativeInfinity, y2 = double.NegativeInfinity;
        try
        {
            SvgDevicePoint[] points = new SvgDevicePoint[source.Length];
            int[] ends = new int[sourceEnds.Length];
            int[]? signs = isEvenOdd ? new int[sourceEnds.Length] : null;
            int contour = 0, contourStart = 0;
            for (int index = 0; index < source.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                SvgDevicePoint point = SvgAuthoredPath.MapPoint(map, source[index], new(0, 0));
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || drawMap is null && (Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue))
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Clip coordinates exceed the admitted numeric range.");
                points[index] = point;
                x1 = Math.Min(x1, point.X); y1 = Math.Min(y1, point.Y); x2 = Math.Max(x2, point.X); y2 = Math.Max(y2, point.Y);
                if (sourcePath is null) commands.Add(new(index == contourStart ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None,
                    new((float)source[index].X, (float)source[index].Y), default, default));
                if (index + 1 == sourceEnds[contour])
                {
                    if (index + 1 - contourStart < 3) throw new ArgumentOutOfRangeException(nameof(sourceEnds));
                    ends[contour] = index + 1;
                    if (signs is not null) signs[contour] = 1;
                    if (sourcePath is null) commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
                    contourStart = index + 1;
                    contour++;
                }
            }
            if (contour != sourceEnds.Length) throw new ArgumentOutOfRangeException(nameof(sourceEnds));
            if (sourcePath is not null)
            {
                using SvgPreparedPathContours prepared = sourcePath.Prepare(map, false, budget, token);
                x1 = y1 = double.PositiveInfinity;
                x2 = y2 = double.NegativeInfinity;
                foreach (SvgDevicePoint point in prepared.Points) Include(point);
                foreach (SvgQuadSpan span in prepared.QuadSpans) Include(span.Control);
                foreach (SvgCubicSpan span in prepared.CubicSpans) { Include(span.FirstControl); Include(span.SecondControl); }
            }
            int boundsClipContentIndex = -1;
            if (kind == SvgClipContentKind.ClipPath)
            {
                double sourceLeft = double.PositiveInfinity, sourceTop = double.PositiveInfinity, sourceRight = double.NegativeInfinity, sourceBottom = double.NegativeInfinity;
                if (sourcePath is not null)
                {
                    // Layout resource bounds use native curve extrema, then the child's map.
                    // Control bounds still govern raster construction and culling above.
                    SvgRectangle tight = SvgPathBounds.Compute(sourcePath.Commands, token);
                    SvgPaintBounds visual = new((float)tight.X, (float)tight.Y,
                        (float)(tight.X + tight.Width), (float)(tight.Y + tight.Height));
                    visual = resourceBounds ?? visual.Map(sourcePath.SceneMap, token);
                    IncludeSource(new(visual.Left, visual.Top));
                    IncludeSource(new(visual.Right, visual.Bottom));
                }
                else foreach (SvgDevicePoint point in source) IncludeSource(point);
                SvgClipBox enclosing = new(Math.Floor(sourceLeft), Math.Floor(sourceTop), Math.Ceiling(sourceRight) - Math.Floor(sourceLeft), Math.Ceiling(sourceBottom) - Math.Floor(sourceTop));
                boundsClipContentIndex = new SvgAffineViewportClip(enclosing, map).Register(state, scene, token).ContentIndex;

                void IncludeSource(SvgDevicePoint point)
                {
                    token.ThrowIfCancellationRequested();
                    sourceLeft = Math.Min(sourceLeft, point.X); sourceTop = Math.Min(sourceTop, point.Y);
                    sourceRight = Math.Max(sourceRight, point.X); sourceBottom = Math.Max(sourceBottom, point.Y);
                }
            }
            // Blink path clips retain an enclosing integer rectangle alongside the
            // path. Its source rectangle rotates with the reference map; path and
            // rectangle coverage remain separate, including their common edge.
            // Axis-aligned bounds also retain native coverage: its trimmed bitmap
            // bounds can clip and reconstruct a curve at a fractional root scale.
            geometry = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, ends, signs, new(255, 255, 255, 255), true, isEvenOdd, SvgPolygonMapping.FiniteEdges, null, reservation, budget));
            if (sourcePath is not null) geometry = geometry with { AuthoredPath = kind == SvgClipContentKind.ClipPath ? BakeClipSource(sourcePath, map, budget, token) : sourcePath.Copy(map, budget, token) };
            else
            {
                OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                try { geometry = geometry with { AuthoredPath = kind == SvgClipContentKind.AffineViewport ? SvgAuthoredPath.CreateConvexShape(owned, map, budget) : SvgAuthoredPath.Create(owned, map, budget) }; }
                catch { owned.Dispose(); throw; }
            }
            using RenderBudget.BudgetReservation array = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1), SvgMemoryCategory.Parser);
            int contentIndex = scene.AddMaskContent(new([geometry], 255, true) { ClipKind = kind, BoundsClipContentIndex = boundsClipContentIndex, DrawMap = drawMap });
            return (new(x1, y1, x2 - x1, y2 - y1), contentIndex);
        }
        finally { geometry.Dispose(); }

        // Materialize the child path in resource coordinates before the reference
        // map reaches a device bitmap. Copy into the registered geometry's command
        // owner; source commands remain borrowed and unchanged during copying.
        static SvgAuthoredPath BakeClipSource(SvgAuthoredPath source, SvgAffineTransform placement, RenderBudget budget, CancellationToken token)
        {
            using BudgetedParserList<SvgAuthoredPathCommand> output = new(budget, SvgMemoryCategory.Scene, token);
            SvgDevicePoint current = default, first = default;
            Span<SvgEndpointConic> arcs = stackalloc SvgEndpointConic[SvgEndpointArc.MaximumConicCount];
            foreach (SvgAuthoredPathCommand command in source.Commands)
            {
                token.ThrowIfCancellationRequested();
                switch (command.Verb)
                {
                    case SvgAuthoredPathVerb.Move:
                    case SvgAuthoredPathVerb.Line:
                        output.Add(command with { First = SvgAuthoredPath.MapPoint(source.SceneMap, command.First, new(0, 0)) });
                        current = command.First;
                        if (command.Verb == SvgAuthoredPathVerb.Move) first = current;
                        break;
                    case SvgAuthoredPathVerb.Quadratic:
                    case SvgAuthoredPathVerb.Conic:
                        output.Add(command with { First = SvgAuthoredPath.MapPoint(source.SceneMap, command.First, new(0, 0)), Second = SvgAuthoredPath.MapPoint(source.SceneMap, command.Second, new(0, 0)) });
                        current = command.Second;
                        break;
                    case SvgAuthoredPathVerb.Cubic:
                        output.Add(command with { First = SvgAuthoredPath.MapPoint(source.SceneMap, command.First, new(0, 0)), Second = SvgAuthoredPath.MapPoint(source.SceneMap, command.Second, new(0, 0)), Third = SvgAuthoredPath.MapPoint(source.SceneMap, command.Third, new(0, 0)) });
                        current = command.Third;
                        break;
                    case SvgAuthoredPathVerb.Arc:
                        int count = SvgEndpointArc.Convert(current, command.First.X, command.First.Y, command.Second.X,
                            (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0, command.Third, arcs, token);
                        if (count == 0) output.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, SvgAuthoredPath.MapPoint(source.SceneMap, command.Third, new(0, 0)), default, default));
                        else for (int index = 0; index < count; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Add(new(SvgAuthoredPathVerb.Conic, SvgAuthoredArcFlags.None,
                                SvgAuthoredPath.MapPoint(source.SceneMap, arcs[index].Control, new(0, 0)),
                                SvgAuthoredPath.MapPoint(source.SceneMap, arcs[index].End, new(0, 0)), new(arcs[index].Weight, 0)));
                        }
                        current = command.Third;
                        break;
                    case SvgAuthoredPathVerb.End:
                    case SvgAuthoredPathVerb.Close:
                        output.Add(command);current = first;
                        break;
                    default:throw new InvalidOperationException("Invalid clip source verb.");
                }
            }
            OwnedParserArray<SvgAuthoredPathCommand> owned = output.Detach();
            try { return SvgAuthoredPath.Create(owned, placement, budget); }
            catch { owned.Dispose(); throw; }
        }

        void Include(SvgDevicePoint point)
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Clip coordinates exceed the admitted numeric range.");
            x1 = Math.Min(x1, point.X); y1 = Math.Min(y1, point.Y);
            x2 = Math.Max(x2, point.X); y2 = Math.Max(y2, point.Y);
        }
    }
}
