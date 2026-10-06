using Lokad.Svg.Rendering;

namespace Lokad.Svg.Parsing;

internal enum SvgAuthoredPathVerb { Move, Line, Quadratic, Cubic, Arc, End }

[Flags]
internal enum SvgAuthoredArcFlags : byte { None = 0, Large = 1, Sweep = 2 }

/// <summary>Retains validated authored float operands, before any scene or output map.</summary>
internal readonly record struct SvgAuthoredPathCommand(SvgAuthoredPathVerb Verb, SvgAuthoredArcFlags ArcFlags, SvgDevicePoint First, SvgDevicePoint Second, SvgDevicePoint Third);

/// <summary>Owns immutable authored commands and their scene map until one geometry releases them.</summary>
/// <remarks>Creation, copying and disposal belong to one conversion's single-threaded budget.
/// Readers borrow immutable storage. Copies reserve and own distinct storage; no global state or shared mutable owner exists.</remarks>
internal sealed class SvgAuthoredPath : IDisposable
{
    // Covers the owner, geometry slot and transient old/new owner during transfer.
    private const long OwnerBytes = 256;
    private OwnedParserArray<SvgAuthoredPathCommand> _commands;
    private RenderBudget.BudgetReservation? _ownerReservation;

    private SvgAuthoredPath(OwnedParserArray<SvgAuthoredPathCommand> commands, SvgAffineTransform sceneMap, RenderBudget.BudgetReservation ownerReservation)
    {
        _commands = commands;
        SceneMap = sceneMap;
        _ownerReservation = ownerReservation;
    }

    internal ReadOnlySpan<SvgAuthoredPathCommand> Commands => _commands.Items;
    internal SvgAffineTransform SceneMap { get; }

    /// <summary>Transfers storage to a replacement owner; later disposal of this dropped owner is harmless.</summary>
    internal SvgAuthoredPath Transfer()
    {
        RenderBudget.BudgetReservation owner = _ownerReservation ?? throw new InvalidOperationException("Authored path ownership has already transferred.");
        SvgAuthoredPath result = new(_commands, SceneMap, owner);
        _commands = new([], null);
        _ownerReservation = null;
        return result;
    }

    /// <summary>Replays authored commands and maps their vertices/controls once with the composed output matrix.</summary>
    /// <remarks>Reserves temporary builder storage before allocation. Every command and copied point consumes caller work;
    /// flattening uses its existing bounded, cancellable policy. The result exclusively owns its surface arrays.</remarks>
    internal SvgMappedAuthoredPath Prepare(SvgAffineTransform outputMap, bool isEvenOdd, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        SvgAffineTransform map = outputMap.ComposeChecked(SceneMap);
        double magnification = Math.Max(Math.Sqrt(map.A * map.A + map.B * map.B), Math.Sqrt(map.C * map.C + map.D * map.D));
        SvgFlattenPolicy policy = new(SvgOrthogonalGeometryLowering.ScaledFlatness(magnification), SvgOrthogonalGeometryLowering.FlattenMaxDepth, SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand);
        int pointCapacity = (int)Math.Min(SvgOrthogonalGeometryLowering.MaximumFlattenedContourPoints, (long)Commands.Length * (SvgOrthogonalGeometryLowering.CurveSegmentsPerCommand + 1));
        int contourCapacity = Math.Min(pointCapacity, Commands.Length);
        // Growing arrays temporarily retain old and new buffers. Span columns
        // round up from four slots; Get*Spans also materializes record arrays.
        int spanCapacity = Math.Max(4, Math.Min(pointCapacity, checked(2 * Commands.Length)));
        long scratchBytes = checked(512 + 2 * ParserCollectionMemory.GetArrayByteCount<SvgDevicePoint>(pointCapacity)
            + 4 * ParserCollectionMemory.GetArrayByteCount<int>(contourCapacity)
            + 3 * ParserCollectionMemory.GetArrayByteCount<SvgQuadSpan>(spanCapacity)
            + 3 * ParserCollectionMemory.GetArrayByteCount<SvgCubicSpan>(spanCapacity));
        using RenderBudget.BudgetReservation scratch = budget.Reserve(scratchBytes, SvgMemoryCategory.Geometry);
        SvgContourBuilder builder = SvgContourBuilder.CreateGrowing(pointCapacity, contourCapacity);
        builder.EnableQuadSpanRetention();
        builder.EnableCubicSpanRetention();
        foreach (SvgAuthoredPathCommand command in Commands)
        {
            workCounter.Consume(cancellationToken);
            switch (command.Verb)
            {
                case SvgAuthoredPathVerb.Move: builder.BeginContour(command.First); break;
                case SvgAuthoredPathVerb.Line: builder.AddLine(command.First); break;
                case SvgAuthoredPathVerb.Quadratic: builder.AddQuadratic(command.First, command.Second, policy, cancellationToken); break;
                case SvgAuthoredPathVerb.Cubic: builder.AddCubic(command.First, command.Second, command.Third, policy, cancellationToken); break;
                case SvgAuthoredPathVerb.Arc: builder.AddArc(command.First.X, command.First.Y, command.Second.X, (command.ArcFlags & SvgAuthoredArcFlags.Large) != 0, (command.ArcFlags & SvgAuthoredArcFlags.Sweep) != 0, command.Third, policy, cancellationToken); break;
                case SvgAuthoredPathVerb.End: builder.EndContour(); break;
                default: throw new InvalidOperationException("Invalid retained path verb.");
            }
        }
        using BudgetedParserList<SvgDevicePoint> points = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<int> ends = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<SvgQuadSpan> quads = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<SvgCubicSpan> cubics = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
        using BudgetedParserList<int> offsets = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        using BudgetedParserList<int> contours = new(budget, SvgMemoryCategory.Geometry, workCounter, cancellationToken);
        for (int contour = 0; contour < builder.ContourCount; contour++)
        {
            ReadOnlySpan<SvgDevicePoint> vertices = builder.GetContour(contour);
            bool degenerate = vertices.Length < 3;
            offsets.Add(degenerate ? -1 : points.Count);
            contours.Add(degenerate ? -1 : ends.Count);
            if (degenerate) continue;
            foreach (SvgDevicePoint point in vertices) points.Add(MapPoint(map, point));
            ends.Add(points.Count);
        }
        foreach (SvgQuadSpan span in builder.GetQuadSpans())
            if (offsets[span.Contour] >= 0) quads.Add(span with { Contour = contours[span.Contour], StartIndex = span.StartIndex + offsets[span.Contour], EndIndex = span.EndIndex + offsets[span.Contour], Control = MapPoint(map, span.Control) });
        foreach (SvgCubicSpan span in builder.GetCubicSpans())
            if (offsets[span.Contour] >= 0) cubics.Add(span with { Contour = contours[span.Contour], StartIndex = span.StartIndex + offsets[span.Contour], EndIndex = span.EndIndex + offsets[span.Contour], FirstControl = MapPoint(map, span.FirstControl), SecondControl = MapPoint(map, span.SecondControl) });
        return CreateMapped(points, ends, quads, cubics, isEvenOdd, budget, workCounter, cancellationToken);

        static SvgMappedAuthoredPath CreateMapped(BudgetedParserList<SvgDevicePoint> pointBuilder, BudgetedParserList<int> endBuilder, BudgetedParserList<SvgQuadSpan> quadBuilder, BudgetedParserList<SvgCubicSpan> cubicBuilder, bool isEvenOdd, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
        {
            RenderBudget.BudgetReservation owner = budget.Reserve(128, SvgMemoryCategory.Surface);
            OwnedParserArray<SvgDevicePoint> points = new([], null);
            OwnedParserArray<int> ends = new([], null), signs = new([], null);
            OwnedParserArray<SvgQuadSpan> quads = new([], null);
            OwnedParserArray<SvgCubicSpan> cubics = new([], null);
            try
            {
                points = pointBuilder.Detach(); ends = endBuilder.Detach(); quads = quadBuilder.Detach(); cubics = cubicBuilder.Detach();
                if (isEvenOdd)
                {
                    using BudgetedParserList<int> signBuilder = new(budget, SvgMemoryCategory.Surface, workCounter, cancellationToken);
                    for (int contour = 0; contour < ends.Items.Length; contour++) signBuilder.Add(0);
                    signs = signBuilder.Detach();
                    long pairs = (long)ends.Items.Length * (ends.Items.Length - 1) / 2;
                    for (long pair = 0; pair < pairs; pair++) workCounter.Consume(cancellationToken);
                    SvgContourNesting.ComputeParitySigns(points.Items, ends.Items, signs.Items, false, cancellationToken);
                }
                return new(points, ends, quads, cubics, signs, owner);
            }
            catch { points.Dispose(); ends.Dispose(); quads.Dispose(); cubics.Dispose(); signs.Dispose(); owner.Dispose(); throw; }
        }

        static SvgDevicePoint MapPoint(SvgAffineTransform map, SvgDevicePoint point)
        {
            float x = (float)point.X, y = (float)point.Y;
            float mappedX = map.IsAxisAligned ? (float)map.A * x + (float)map.E : ((float)map.A * x + (float)map.C * y) + (float)map.E;
            float mappedY = map.IsAxisAligned ? (float)map.D * y + (float)map.F : ((float)map.B * x + (float)map.D * y) + (float)map.F;
            if (!float.IsFinite(mappedX) || !float.IsFinite(mappedY)) throw new SvgException(SvgErrorCode.LimitExceeded, "Authored path mapping exceeds the admitted numeric range.");
            return new(mappedX, mappedY);
        }
    }

    /// <summary>Adopts command-array ownership only on success; the caller releases the array on failure.</summary>
    internal static SvgAuthoredPath Create(OwnedParserArray<SvgAuthoredPathCommand> commands, SvgAffineTransform sceneMap, RenderBudget budget)
    {
        RenderBudget.BudgetReservation owner = budget.Reserve(OwnerBytes, SvgMemoryCategory.Scene);
        try { return new SvgAuthoredPath(commands, sceneMap, owner); }
        catch { owner.Dispose(); throw; }
    }

    /// <summary>Copies commands with bounded work and cancellation, composing an additional scene map.</summary>
    internal SvgAuthoredPath Copy(SvgAffineTransform placement, RenderBudget budget, ParserWorkCounter workCounter, CancellationToken cancellationToken)
    {
        using BudgetedParserList<SvgAuthoredPathCommand> commands = new(budget, SvgMemoryCategory.Scene, workCounter, cancellationToken);
        foreach (SvgAuthoredPathCommand command in Commands) commands.Add(command);
        OwnedParserArray<SvgAuthoredPathCommand> owned = commands.Detach();
        try { return Create(owned, placement.ComposeChecked(SceneMap), budget); }
        catch { owned.Dispose(); throw; }
    }

    public void Dispose()
    {
        _commands.Dispose();
        _commands = new([], null);
        _ownerReservation?.Dispose();
        _ownerReservation = null;
    }
}

/// <summary>Exclusively owns one mapped authored fill and its surface-budgeted arrays.</summary>
internal sealed class SvgMappedAuthoredPath : IDisposable
{
    private OwnedParserArray<SvgDevicePoint> _points;
    private OwnedParserArray<int> _ends;
    private OwnedParserArray<SvgQuadSpan> _quads;
    private OwnedParserArray<SvgCubicSpan> _cubics;
    private OwnedParserArray<int> _signs;
    private RenderBudget.BudgetReservation? _owner;

    internal SvgMappedAuthoredPath(OwnedParserArray<SvgDevicePoint> points, OwnedParserArray<int> ends, OwnedParserArray<SvgQuadSpan> quads, OwnedParserArray<SvgCubicSpan> cubics, OwnedParserArray<int> signs, RenderBudget.BudgetReservation owner)
    {
        _points = points; _ends = ends; _quads = quads; _cubics = cubics; _signs = signs; _owner = owner;
    }

    internal SvgDevicePoint[] Points => _points.Items;
    internal int[] ContourEnds => _ends.Items;
    internal SvgQuadSpan[] QuadSpans => _quads.Items;
    internal SvgCubicSpan[] CubicSpans => _cubics.Items;
    internal int[]? Signs => _signs.Items.Length == 0 ? null : _signs.Items;


    public void Dispose()
    {
        _points.Dispose(); _ends.Dispose(); _quads.Dispose(); _cubics.Dispose(); _signs.Dispose();
        _owner?.Dispose(); _owner = null;
    }
}
