using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgClipContentKind { AffineViewport, ClipPath, OvalClipPath, BinaryViewport }

/// <summary>Retains a rectangular viewport and its composed scene map until clip command emission.</summary>
/// <remarks>Placement owns no arrays. Emission reserves its convex source contour and registers a copy with
/// the existing mask-content scene owner; failures release the temporary contour and commands.</remarks>
internal readonly record struct SvgAffineViewportClip(SvgClipBox Box, SvgAffineTransform Map)
{
    internal bool IsBinary { get; init; }
    internal (SvgClipBox Bounds, int ContentIndex) Register(SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        float left = (float)Box.X, top = (float)Box.Y, right = (float)(Box.X + Box.Width), bottom = (float)(Box.Y + Box.Height);
        ReadOnlySpan<SvgDevicePoint> points = stackalloc SvgDevicePoint[] { new(left, bottom), new(left, top), new(right, top), new(right, bottom) };
        return RegisterContour(points, [points.Length], Map, IsBinary ? SvgClipContentKind.BinaryViewport : SvgClipContentKind.AffineViewport, null, null, false, state, scene, work, token);
    }

    /// <summary>Registers borrowed source contours with the shared native clip-content owner.</summary>
    /// <remarks>Callers validate contour admission. Commands and arrays reserve before allocation;
    /// mapping/copying observes caller work and cancellation. The scene owns a distinct copy after
    /// success; failure releases all temporary storage. Generic path clips retain raw classification.</remarks>
    internal static (SvgClipBox Bounds, int ContentIndex) RegisterContour(ReadOnlySpan<SvgDevicePoint> source, ReadOnlySpan<int> sourceEnds,
        SvgAffineTransform map, SvgClipContentKind kind, SvgAuthoredPath? sourcePath, SvgPaintBounds? resourceBounds, bool isEvenOdd, SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        if (source.Length < 3) throw new ArgumentOutOfRangeException(nameof(source));
        if (sourceEnds.IsEmpty || sourceEnds[^1] != source.Length) throw new ArgumentOutOfRangeException(nameof(sourceEnds));
        RenderBudget budget = state.Budget;
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, work, token);
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
                work.Consume(token);
                SvgDevicePoint point = map.Apply(source[index].X, source[index].Y);
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue)
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
                using SvgPreparedPathContours prepared = sourcePath.Prepare(map, false, budget, work, token);
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
                    SvgRectangle tight = SvgPathBounds.Compute(sourcePath.Commands, work, token);
                    SvgPaintBounds visual = new((float)tight.X, (float)tight.Y,
                        (float)(tight.X + tight.Width), (float)(tight.Y + tight.Height));
                    visual = resourceBounds ?? visual.Map(sourcePath.SceneMap, work, token);
                    IncludeSource(new(visual.Left, visual.Top));
                    IncludeSource(new(visual.Right, visual.Bottom));
                }
                else foreach (SvgDevicePoint point in source) IncludeSource(point);
                SvgClipBox enclosing = new(Math.Floor(sourceLeft), Math.Floor(sourceTop), Math.Ceiling(sourceRight) - Math.Floor(sourceLeft), Math.Ceiling(sourceBottom) - Math.Floor(sourceTop));
                boundsClipContentIndex = new SvgAffineViewportClip(enclosing, map).Register(state, scene, work, token).ContentIndex;

                void IncludeSource(SvgDevicePoint point)
                {
                    work.Consume(token);
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
            if (sourcePath is not null) geometry = geometry with { AuthoredPath = sourcePath.Copy(map, budget, work, token) };
            else
            {
                OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
                try { geometry = geometry with { AuthoredPath = kind == SvgClipContentKind.AffineViewport ? SvgAuthoredPath.CreateConvexShape(owned, map, budget) : SvgAuthoredPath.Create(owned, map, budget) }; }
                catch { owned.Dispose(); throw; }
            }
            using RenderBudget.BudgetReservation array = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1), SvgMemoryCategory.Parser);
            int contentIndex = scene.AddMaskContent(new([geometry], 255, true) { ClipKind = kind, BoundsClipContentIndex = boundsClipContentIndex });
            return (new(x1, y1, x2 - x1, y2 - y1), contentIndex);
        }
        finally { geometry.Dispose(); }

        void Include(SvgDevicePoint point)
        {
            work.Consume(token);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue)
                throw new SvgException(SvgErrorCode.LimitExceeded, "Clip coordinates exceed the admitted numeric range.");
            x1 = Math.Min(x1, point.X); y1 = Math.Min(y1, point.Y);
            x2 = Math.Max(x2, point.X); y2 = Math.Max(y2, point.Y);
        }
    }
}
