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
        return RegisterContour(points, Map, SvgClipContentKind.AffineViewport, state, scene, work, token);
    }

    /// <summary>Registers one borrowed source contour with the shared native clip-content owner.</summary>
    /// <remarks>Callers validate contour admission. Commands and arrays reserve before allocation;
    /// mapping/copying observes caller work and cancellation. The scene owns a distinct copy after
    /// success; failure releases all temporary storage. Generic path clips retain raw classification.</remarks>
    internal static (SvgClipBox Bounds, int ContentIndex) RegisterContour(ReadOnlySpan<SvgDevicePoint> source,
        SvgAffineTransform map, SvgClipContentKind kind, SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        if (source.Length < 3) throw new ArgumentOutOfRangeException(nameof(source));
        RenderBudget budget = state.Budget;
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, work, token);
        using RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(source.Length) + ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Scene);
        SvgGeometry geometry = default;
        try
        {
            SvgDevicePoint[] points = new SvgDevicePoint[source.Length];
            double x1 = double.PositiveInfinity, y1 = double.PositiveInfinity, x2 = double.NegativeInfinity, y2 = double.NegativeInfinity;
            for (int index = 0; index < source.Length; index++)
            {
                work.Consume(token);
                SvgDevicePoint point = map.Apply(source[index].X, source[index].Y);
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Clip coordinates exceed the admitted numeric range.");
                points[index] = point;
                x1 = Math.Min(x1, point.X); y1 = Math.Min(y1, point.Y); x2 = Math.Max(x2, point.X); y2 = Math.Max(y2, point.Y);
                commands.Add(new(index == 0 ? SvgAuthoredPathVerb.Move : SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None,
                    new((float)source[index].X, (float)source[index].Y), default, default));
            }
            commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
            // Blink path clips retain an enclosing integer rectangle alongside the
            // path. After root placement its fractional AA coverage remains separate.
            SvgClipBox? paintClip = kind == SvgClipContentKind.ClipPath
                ? new(Math.Floor(x1), Math.Floor(y1), Math.Ceiling(x2) - Math.Floor(x1), Math.Ceiling(y2) - Math.Floor(y1)) : null;
            geometry = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, [source.Length], null, new(255, 255, 255, 255), true, false, SvgPolygonMapping.FiniteEdges, paintClip, reservation, budget));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { geometry = geometry with { AuthoredPath = kind == SvgClipContentKind.AffineViewport ? SvgAuthoredPath.CreateConvexShape(owned, map, budget) : SvgAuthoredPath.Create(owned, map, budget) }; }
            catch { owned.Dispose(); throw; }
            using RenderBudget.BudgetReservation array = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1), SvgMemoryCategory.Parser);
            int contentIndex = scene.AddMaskContent(new([geometry], 255, true));
            return (new(x1, y1, x2 - x1, y2 - y1), contentIndex);
        }
        finally { geometry.Dispose(); }
    }
}
