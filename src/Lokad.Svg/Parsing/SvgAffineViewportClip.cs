using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

/// <summary>Retains a rectangular viewport and its composed scene map until clip command emission.</summary>
/// <remarks>Placement owns no arrays. Emission reserves its convex source contour and registers a copy with
/// the existing mask-content scene owner; failures release the temporary contour and commands.</remarks>
internal readonly record struct SvgAffineViewportClip(SvgClipBox Box, SvgAffineTransform Map)
{
    internal (SvgClipBox Bounds, int ContentIndex) Register(SvgDocumentState state, SvgSceneBuilder scene, ParserWorkCounter work, CancellationToken token)
    {
        RenderBudget budget = state.Budget;
        float left = (float)Box.X, top = (float)Box.Y, right = (float)(Box.X + Box.Width), bottom = (float)(Box.Y + Box.Height);
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, work, token);
        commands.Add(new(SvgAuthoredPathVerb.Move, SvgAuthoredArcFlags.None, new(left, bottom), default, default));
        commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(left, top), default, default));
        commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, top), default, default));
        commands.Add(new(SvgAuthoredPathVerb.Line, SvgAuthoredArcFlags.None, new(right, bottom), default, default));
        commands.Add(new(SvgAuthoredPathVerb.Close, SvgAuthoredArcFlags.None, default, default, default));
        RenderBudget.BudgetReservation reservation = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(4) + ParserCollectionMemory.GetArrayByteCount<int>(1), SvgMemoryCategory.Scene);
        SvgGeometry geometry = default;
        try
        {
            SvgDevicePoint[] points = [Map.Apply(left, bottom), Map.Apply(left, top), Map.Apply(right, top), Map.Apply(right, bottom)];
            double x1 = double.PositiveInfinity, y1 = double.PositiveInfinity, x2 = double.NegativeInfinity, y2 = double.NegativeInfinity;
            foreach (SvgDevicePoint point in points)
            {
                work.Consume(token);
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > int.MaxValue || Math.Abs(point.Y) > int.MaxValue)
                    throw new SvgException(SvgErrorCode.LimitExceeded, "Viewport clip coordinates exceed the admitted numeric range.");
                x1 = Math.Min(x1, point.X); y1 = Math.Min(y1, point.Y); x2 = Math.Max(x2, point.X); y2 = Math.Max(y2, point.Y);
            }
            geometry = SvgGeometry.FromPolygonFill(SvgPolygonFill.Create(points, [4], null, new(255, 255, 255, 255), true, false, SvgPolygonMapping.FiniteEdges, null, reservation, budget));
            OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
            try { geometry = geometry with { AuthoredPath = SvgAuthoredPath.CreateConvexShape(owned, Map, budget) }; }
            catch { owned.Dispose(); throw; }
            using RenderBudget.BudgetReservation array = budget.Reserve(ParserCollectionMemory.GetArrayByteCount<SvgGeometry>(1), SvgMemoryCategory.Parser);
            int index = scene.AddMaskContent(new([geometry], 255, true));
            return (new(x1, y1, x2 - x1, y2 - y1), index);
        }
        finally { geometry.Dispose(); reservation.Dispose(); }
    }
}
