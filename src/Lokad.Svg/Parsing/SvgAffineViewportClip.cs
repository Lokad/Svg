using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgClipContentKind { AffineViewport, ClipPath }

/// <summary>Retains a rectangular viewport and its composed scene map until clip command emission.</summary>
/// <remarks>Placement owns no arrays. Emission reserves its convex source contour and registers a copy with
/// the existing mask-content scene owner; failures release the temporary contour and commands.</remarks>
internal readonly record struct SvgAffineViewportClip(SvgClipBox Box, SvgAffineTransform Map)
{
    internal (SvgClipBox Bounds, int ContentIndex) Register(SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        float left = (float)Box.X, top = (float)Box.Y, right = (float)(Box.X + Box.Width), bottom = (float)(Box.Y + Box.Height);
        ReadOnlySpan<SvgDevicePoint> points = stackalloc SvgDevicePoint[] { new(left, bottom), new(left, top), new(right, top), new(right, bottom) };
        return RegisterContour(points, Map, SvgClipContentKind.AffineViewport, null, false, state, scene, work, token);
    }

    /// <summary>Registers one borrowed source contour with the shared native clip-content owner.</summary>
    /// <remarks>Callers validate contour admission. Commands and arrays reserve before allocation;
    /// mapping/copying observes caller work and cancellation. The scene owns a distinct copy after
    /// success; failure releases all temporary storage. Generic path clips retain raw classification.</remarks>
    internal static (SvgClipBox Bounds, int ContentIndex) RegisterContour(ReadOnlySpan<SvgDevicePoint> source,
        SvgAffineTransform map, SvgClipContentKind kind, SvgAuthoredPath? sourcePath, bool isEvenOdd, SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        if (source.Length < 3) throw new ArgumentOutOfRangeException(nameof(source));
        if (sourcePath is not null && !map.IsAxisAligned && !map.IsUniformScale())
            throw new SvgException(SvgErrorCode.UnsupportedFeature, "Curved clip paths under nonuniform group transforms are not implemented.");
        RenderBudget budget = state.Budget;
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, work, token);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Length) + (isEvenOdd ? 2 : 1) * ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Scene);
        SvgGeometry geometry = default;
        double x1 = double.PositiveInfinity, y1 = double.PositiveInfinity, x2 = double.NegativeInfinity, y2 = double.NegativeInfinity;
        try
        {
            SvgDevicePoint[] points = new SvgDevicePoint[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                work.Consume(token);
                SvgDevicePoint point = map.Apply(source[index].X, source[index].Y);
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Clip coordinates exceed the admitted numeric range.");
                points[index] = point;
                x1 = Math.Min(x1, point.X); y1 = Math.Min(y1, point.Y); x2 = Math.Max(x2, point.X); y2 = Math.Max(y2, point.Y);
                if (sourcePath is null) commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None,
                    new((float)source[index].X, (float)source[index].Y), default, default));
            }
            if (sourcePath is null) commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
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
            SvgClipBox? paintClip = null;
            if (kind == SvgClipContentKind.ClipPath)
            {
                double sourceLeft = double.PositiveInfinity, sourceTop = double.PositiveInfinity, sourceRight = double.NegativeInfinity, sourceBottom = double.NegativeInfinity;
                if (sourcePath is not null)
                {
                    using SvgPreparedPathContours sourceContours = sourcePath.Prepare(SvgAffineTransform.Identity, false, budget, work, token);
                    foreach (SvgDevicePoint point in sourceContours.Points) IncludeSource(point);
                    foreach (SvgQuadSpan span in sourceContours.QuadSpans) IncludeSource(span.Control);
                    foreach (SvgCubicSpan span in sourceContours.CubicSpans) { IncludeSource(span.FirstControl); IncludeSource(span.SecondControl); }
                }
                else foreach (SvgDevicePoint point in source) IncludeSource(point);
                SvgClipBox enclosing = new(Math.Floor(sourceLeft), Math.Floor(sourceTop), Math.Ceiling(sourceRight) - Math.Floor(sourceLeft), Math.Ceiling(sourceBottom) - Math.Floor(sourceTop));
                if (map.IsAxisAligned) paintClip = map.Apply(enclosing);
                else boundsClipContentIndex = new SvgAffineViewportClip(enclosing, map).Register(state, scene, work, token).ContentIndex;

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
            geometry = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, [source.Length], isEvenOdd ? [1] : null, new(255, 255, 255, 255), true, isEvenOdd, SvgPolygonMapping.FiniteEdges, paintClip, reservation, budget));
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
